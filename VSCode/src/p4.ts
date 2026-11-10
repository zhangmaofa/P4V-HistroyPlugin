import { spawn } from 'node:child_process';
import * as path from 'node:path';
import * as os from 'node:os';
import * as vscode from 'vscode';
import { parseAnnotations, parseSettingValue, parseTaggedRecords } from './parsers';
import { AnnotationLoadResult, P4WorkspaceContext, ProcessResult } from './types';

export class P4Service {
  private readonly workspaceCache = new Map<string, Promise<P4WorkspaceContext | undefined>>();

  async load(filePath: string): Promise<AnnotationLoadResult> {
    const workspace = await this.resolveWorkspace(filePath);
    if (!workspace) {
      return { annotations: [], error: '当前文件不在可用的 P4 workspace 中。' };
    }

    const result = await this.runP4(
      workspace.workingDirectory,
      workspace.client,
      ['annotate', '-c', '-u', '-q', `${filePath}#have`]
    );
    if (result.exitCode !== 0) {
      return { annotations: [], error: firstUsefulError(result) };
    }

    const annotations = parseAnnotations(result.standardOutput);
    return annotations.length > 0
      ? { workspace, annotations }
      : { annotations: [], error: 'P4 annotate 没有返回文本行。' };
  }

  invalidate(filePath: string): void {
    this.workspaceCache.delete(cacheKey(path.dirname(path.resolve(filePath))));
  }

  clearCache(): void {
    this.workspaceCache.clear();
  }

  openChange(workspace: P4WorkspaceContext, change: number): void {
    const configured = vscode.workspace.getConfiguration('p4HistroyPlugin').get<string>('p4vPath', 'p4v');
    const executable = process.platform === 'win32' && configured === 'p4v' ? 'p4v.exe' : configured;
    const args = ['-p4vc'];
    if (workspace.connectionAddress) {
      args.push('-p', workspace.connectionAddress);
    }
    if (workspace.user) {
      args.push('-u', workspace.user);
    }
    args.push('-c', workspace.client, 'change', String(change));

    const child = spawn(executable, args, {
      cwd: workspace.workingDirectory,
      detached: true,
      windowsHide: true,
      stdio: 'ignore',
      shell: false
    });
    child.on('error', error => {
      void vscode.window.showErrorMessage(`无法启动 P4V：${error.message}`);
    });
    child.unref();
  }

  private resolveWorkspace(filePath: string): Promise<P4WorkspaceContext | undefined> {
    const directory = path.dirname(path.resolve(filePath));
    const key = cacheKey(directory);
    const existing = this.workspaceCache.get(key);
    if (existing) {
      return existing;
    }

    const resolution = this.resolveWorkspaceCore(filePath, directory).catch(error => {
      this.workspaceCache.delete(key);
      throw error;
    });
    this.workspaceCache.set(key, resolution);
    return resolution;
  }

  private async resolveWorkspaceCore(
    filePath: string,
    directory: string
  ): Promise<P4WorkspaceContext | undefined> {
    const infoResult = await this.runP4(directory, undefined, ['info', '-s']);
    const info = parseTaggedRecords(infoResult.standardOutput)[0];
    const user = info?.get('userName');
    if (!user) {
      return undefined;
    }

    const configuredClient = info.get('clientName') ?? '';
    const configuredHost = info.get('clientHost') ?? '';
    const reportedServerAddress = info.get('serverAddress') ?? '';
    const settingAddress = await this.readSetting(directory, 'P4PORT');
    const connectionAddress = settingAddress || reportedServerAddress;

    if (configuredClient && await this.mapsFile(directory, configuredClient, filePath)) {
      return { client: configuredClient, user, connectionAddress, workingDirectory: directory };
    }

    const clientsResult = await this.runP4(directory, undefined, ['clients', '-u', user]);
    if (clientsResult.exitCode !== 0) {
      return undefined;
    }

    const fullFilePath = path.resolve(filePath);
    const candidates = parseTaggedRecords(clientsResult.standardOutput)
      .map(record => ({
        client: record.get('client') ?? '',
        root: record.get('Root') ?? '',
        host: record.get('Host') ?? ''
      }))
      .filter(candidate => candidate.client && candidate.root
        && hostMatches(candidate.host, configuredHost)
        && pathWithinRoot(fullFilePath, candidate.root))
      .sort((left, right) => path.resolve(right.root).length - path.resolve(left.root).length);

    for (const candidate of candidates) {
      if (await this.mapsFile(directory, candidate.client, fullFilePath)) {
        return {
          client: candidate.client,
          user,
          connectionAddress,
          workingDirectory: directory
        };
      }
    }

    return undefined;
  }

  private async mapsFile(directory: string, client: string, filePath: string): Promise<boolean> {
    const result = await this.runP4(directory, client, ['where', filePath]);
    if (result.exitCode !== 0) {
      return false;
    }

    return parseTaggedRecords(result.standardOutput).some(record => {
      const mappedPath = record.get('path');
      return mappedPath ? pathsEqual(mappedPath, filePath) : false;
    });
  }

  private async readSetting(directory: string, settingName: string): Promise<string> {
    const result = await runExecutable(
      p4Executable(),
      ['-d', directory, 'set', '-q', settingName],
      directory
    );
    return result.exitCode === 0
      ? parseSettingValue(result.standardOutput, settingName)
      : '';
  }

  private runP4(
    directory: string,
    client: string | undefined,
    commandArguments: readonly string[]
  ): Promise<ProcessResult> {
    const args: string[] = [];
    if (client) {
      args.push('-c', client);
    }
    args.push('-d', directory, '-ztag', ...commandArguments);
    return runExecutable(p4Executable(), args, directory);
  }
}

function p4Executable(): string {
  const configured = vscode.workspace.getConfiguration('p4HistroyPlugin').get<string>('p4Path', 'p4');
  return process.platform === 'win32' && configured === 'p4' ? 'p4.exe' : configured;
}

function runExecutable(
  executable: string,
  args: readonly string[],
  workingDirectory: string
): Promise<ProcessResult> {
  return new Promise(resolve => {
    let settled = false;
    let standardOutput = '';
    let standardError = '';
    const child = spawn(executable, args, {
      cwd: workingDirectory,
      windowsHide: true,
      shell: false
    });

    child.stdout.setEncoding('utf8');
    child.stderr.setEncoding('utf8');
    child.stdout.on('data', data => { standardOutput += String(data); });
    child.stderr.on('data', data => { standardError += String(data); });
    child.on('error', error => {
      if (!settled) {
        settled = true;
        resolve({ exitCode: -1, standardOutput, standardError: error.message });
      }
    });
    child.on('close', code => {
      if (!settled) {
        settled = true;
        resolve({ exitCode: code ?? -1, standardOutput, standardError });
      }
    });
  });
}

function hostMatches(candidateHost: string, configuredHost: string): boolean {
  if (!candidateHost) {
    return true;
  }
  return candidateHost.localeCompare(os.hostname(), undefined, { sensitivity: 'accent' }) === 0
    || (!!configuredHost
      && candidateHost.localeCompare(configuredHost, undefined, { sensitivity: 'accent' }) === 0);
}

function pathWithinRoot(filePath: string, root: string): boolean {
  const fullFile = comparablePath(filePath);
  const fullRoot = comparablePath(root).replace(/[\\/]$/, '');
  return fullFile === fullRoot || fullFile.startsWith(`${fullRoot}${path.sep}`);
}

function pathsEqual(left: string, right: string): boolean {
  return comparablePath(left) === comparablePath(right);
}

function comparablePath(value: string): string {
  const resolved = path.resolve(value).replace(/[\\/]$/, '');
  return process.platform === 'win32' ? resolved.toLocaleLowerCase() : resolved;
}

function cacheKey(directory: string): string {
  return comparablePath(directory);
}

function firstUsefulError(result: ProcessResult): string {
  const error = result.standardError.trim() || result.standardOutput.trim();
  return error.split(/\r?\n/, 1)[0] || 'P4 命令执行失败。';
}
