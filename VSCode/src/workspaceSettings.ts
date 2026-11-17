import { promises as fs } from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import { P4WorkspaceContext } from './types';

export interface StoredWorkspaceSetting {
  root: string;
  port: string;
  user: string;
  client: string;
  manual: boolean;
  updatedAt: string;
}

interface WorkspaceSettingsDocument {
  version: number;
  workspaces: StoredWorkspaceSetting[];
}

export class WorkspaceSettingsStore {
  private queue: Promise<void> = Promise.resolve();

  constructor(
    readonly settingsPath: string = defaultSettingsPath()
  ) {}

  async findForFile(filePath: string): Promise<StoredWorkspaceSetting | undefined> {
    await this.queue;
    const document = await this.load();
    return document.workspaces
      .filter(setting => isComplete(setting) && pathWithinRoot(filePath, setting.root))
      .sort((left, right) => comparablePath(right.root).length - comparablePath(left.root).length)[0];
  }

  async saveAuto(workspace: P4WorkspaceContext): Promise<void> {
    await this.save({
      root: workspace.root,
      port: workspace.connectionAddress,
      user: workspace.user,
      client: workspace.client,
      manual: false,
      updatedAt: new Date().toISOString()
    });
  }

  async saveManual(root: string, port: string, user: string, client: string): Promise<void> {
    await this.save({
      root,
      port,
      user,
      client,
      manual: true,
      updatedAt: new Date().toISOString()
    });
  }

  async removeForFile(filePath: string): Promise<boolean> {
    return this.exclusive(async () => {
      const document = await this.load();
      const matches = document.workspaces
        .map((setting, index) => ({ setting, index }))
        .filter(item => pathWithinRoot(filePath, item.setting.root))
        .sort((left, right) => comparablePath(right.setting.root).length
          - comparablePath(left.setting.root).length);
      const match = matches[0];
      if (!match) {
        return false;
      }

      document.workspaces.splice(match.index, 1);
      await this.write(document);
      return true;
    });
  }

  private async save(setting: StoredWorkspaceSetting): Promise<void> {
    if (!isComplete(setting)) {
      throw new Error('P4 workspace 设置必须包含 Root、P4PORT、P4USER 和 P4CLIENT。');
    }

    setting.root = path.resolve(setting.root);
    await this.exclusive(async () => {
      const document = await this.load();
      const existingIndex = document.workspaces.findIndex(item =>
        comparablePath(item.root) === comparablePath(setting.root));
      const existing = existingIndex >= 0 ? document.workspaces[existingIndex] : undefined;
      if (existing?.manual && !setting.manual) {
        return;
      }

      if (existingIndex >= 0) {
        document.workspaces.splice(existingIndex, 1);
      }
      document.workspaces.push(setting);
      await this.write(document);
    });
  }

  private exclusive<T>(operation: () => Promise<T>): Promise<T> {
    const result = this.queue.then(operation, operation);
    this.queue = result.then(() => undefined, () => undefined);
    return result;
  }

  private async load(): Promise<WorkspaceSettingsDocument> {
    try {
      const content = await fs.readFile(this.settingsPath, 'utf8');
      const parsed = JSON.parse(content) as Partial<WorkspaceSettingsDocument>;
      return {
        version: typeof parsed.version === 'number' ? parsed.version : 1,
        workspaces: Array.isArray(parsed.workspaces) ? parsed.workspaces : []
      };
    } catch (error) {
      const code = error instanceof Error && 'code' in error
        ? String((error as NodeJS.ErrnoException).code)
        : '';
      if (code !== 'ENOENT') {
        // A malformed file should not prevent automatic workspace discovery.
      }
      return { version: 1, workspaces: [] };
    }
  }

  private async write(document: WorkspaceSettingsDocument): Promise<void> {
    await fs.mkdir(path.dirname(this.settingsPath), { recursive: true });
    const temporaryPath = `${this.settingsPath}.${process.pid}.${Date.now()}.tmp`;
    try {
      await fs.writeFile(temporaryPath, JSON.stringify(document, null, 2), 'utf8');
      await fs.copyFile(temporaryPath, this.settingsPath);
    } finally {
      await fs.unlink(temporaryPath).catch(() => undefined);
    }
  }
}

function defaultSettingsPath(): string {
  const localAppData = process.env.LOCALAPPDATA;
  return localAppData
    ? path.join(localAppData, 'P4HistroyPlugin', 'workspaces.json')
    : path.join(os.homedir(), '.p4-histroy-plugin', 'workspaces.json');
}

function isComplete(setting: StoredWorkspaceSetting): boolean {
  return !!setting
    && !!setting.root?.trim()
    && !!setting.port?.trim()
    && !!setting.user?.trim()
    && !!setting.client?.trim();
}

function pathWithinRoot(filePath: string, root: string): boolean {
  try {
    const fullFile = comparablePath(filePath);
    const fullRoot = comparablePath(root);
    return fullFile === fullRoot || fullFile.startsWith(`${fullRoot}${path.sep}`);
  } catch {
    return false;
  }
}

function comparablePath(value: string): string {
  const resolved = path.resolve(value).replace(/[\\/]$/, '');
  return process.platform === 'win32' ? resolved.toLocaleLowerCase() : resolved;
}
