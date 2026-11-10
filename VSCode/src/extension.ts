import * as vscode from 'vscode';
import { mapAnnotations } from './lineMapper';
import { P4Service } from './p4';
import { AnnotationLine, P4WorkspaceContext } from './types';

interface DocumentState {
  generation: number;
  loading: boolean;
  baseline: readonly AnnotationLine[];
  mapped: readonly AnnotationLine[];
  workspace?: P4WorkspaceContext;
  error?: string;
}

interface OpenChangeArgument {
  readonly uri?: string | vscode.Uri;
  readonly change?: number;
  readonly lineNumber?: number;
}

const states = new Map<string, DocumentState>();
const editTimers = new Map<string, NodeJS.Timeout>();
const service = new P4Service();

let enabled = true;
let historyDecoration: vscode.TextEditorDecorationType;
let localDecoration: vscode.TextEditorDecorationType;
let errorDecoration: vscode.TextEditorDecorationType;
let statusBar: vscode.StatusBarItem;

export function activate(context: vscode.ExtensionContext): void {
  enabled = context.globalState.get<boolean>('p4HistroyPlugin.enabled', true);

  historyDecoration = vscode.window.createTextEditorDecorationType({
    rangeBehavior: vscode.DecorationRangeBehavior.ClosedClosed
  });
  localDecoration = vscode.window.createTextEditorDecorationType({
    rangeBehavior: vscode.DecorationRangeBehavior.ClosedClosed
  });
  errorDecoration = vscode.window.createTextEditorDecorationType({
    rangeBehavior: vscode.DecorationRangeBehavior.ClosedClosed
  });
  statusBar = vscode.window.createStatusBarItem(vscode.StatusBarAlignment.Left, 20);
  statusBar.command = 'p4HistroyPlugin.toggle';
  statusBar.show();

  context.subscriptions.push(
    historyDecoration,
    localDecoration,
    errorDecoration,
    statusBar,
    vscode.commands.registerCommand('p4HistroyPlugin.toggle', async () => {
      enabled = !enabled;
      await context.globalState.update('p4HistroyPlugin.enabled', enabled);
      for (const editor of vscode.window.visibleTextEditors) {
        renderEditor(editor);
      }
      updateStatusBar();
    }),
    vscode.commands.registerCommand('p4HistroyPlugin.refresh', async (resource?: vscode.Uri) => {
      const document = findDocument(resource) ?? vscode.window.activeTextEditor?.document;
      if (!document || document.uri.scheme !== 'file') {
        return;
      }
      service.invalidate(document.fileName);
      await loadDocument(document);
    }),
    vscode.commands.registerCommand('p4HistroyPlugin.openChange', (argument?: OpenChangeArgument) => {
      openChange(argument);
    }),
    vscode.window.onDidChangeVisibleTextEditors(editors => {
      void loadEditors(editors);
    }),
    vscode.window.onDidChangeActiveTextEditor(editor => {
      if (editor?.document.uri.scheme === 'file') {
        void ensureLoaded(editor.document);
      }
      updateStatusBar();
    }),
    vscode.window.onDidChangeTextEditorVisibleRanges(event => {
      renderEditor(event.textEditor);
    }),
    vscode.workspace.onDidChangeTextDocument(event => {
      scheduleRemap(event.document);
    }),
    vscode.workspace.onDidCloseTextDocument(document => {
      const key = document.uri.toString();
      const timer = editTimers.get(key);
      if (timer) {
        clearTimeout(timer);
      }
      editTimers.delete(key);
      states.delete(key);
    }),
    vscode.workspace.onDidChangeConfiguration(event => {
      if (!event.affectsConfiguration('p4HistroyPlugin')) {
        return;
      }
      service.clearCache();
      void refreshVisibleEditors();
    })
  );

  updateStatusBar();
  void loadEditors(vscode.window.visibleTextEditors);
}

export function deactivate(): void {
  for (const timer of editTimers.values()) {
    clearTimeout(timer);
  }
  editTimers.clear();
  states.clear();
}

async function loadEditors(editors: readonly vscode.TextEditor[]): Promise<void> {
  const documents = new Map<string, vscode.TextDocument>();
  for (const editor of editors) {
    if (editor.document.uri.scheme === 'file') {
      documents.set(editor.document.uri.toString(), editor.document);
    }
  }
  await Promise.all([...documents.values()].map(ensureLoaded));
}

async function ensureLoaded(document: vscode.TextDocument): Promise<void> {
  if (!states.has(document.uri.toString())) {
    await loadDocument(document);
  } else {
    renderDocument(document);
  }
}

async function loadDocument(document: vscode.TextDocument): Promise<void> {
  if (document.uri.scheme !== 'file') {
    return;
  }

  const key = document.uri.toString();
  const generation = (states.get(key)?.generation ?? 0) + 1;
  states.set(key, {
    generation,
    loading: true,
    baseline: [],
    mapped: []
  });
  renderDocument(document);
  updateStatusBar();

  try {
    const result = await service.load(document.fileName);
    const current = states.get(key);
    if (!current || current.generation !== generation || document.isClosed) {
      return;
    }

    if (!result.workspace || result.annotations.length === 0) {
      states.set(key, {
        generation,
        loading: false,
        baseline: [],
        mapped: [],
        error: result.error ?? 'P4 查询没有返回数据。'
      });
    } else {
      const baseline = result.annotations;
      states.set(key, {
        generation,
        loading: false,
        baseline,
        mapped: mapAnnotations(baseline, documentLines(document)),
        workspace: result.workspace
      });
    }
  } catch (error) {
    const current = states.get(key);
    if (current?.generation === generation) {
      states.set(key, {
        generation,
        loading: false,
        baseline: [],
        mapped: [],
        error: error instanceof Error ? error.message : String(error)
      });
    }
  }

  renderDocument(document);
  updateStatusBar();
}

function scheduleRemap(document: vscode.TextDocument): void {
  const key = document.uri.toString();
  const state = states.get(key);
  if (!state || state.baseline.length === 0) {
    return;
  }

  const existing = editTimers.get(key);
  if (existing) {
    clearTimeout(existing);
  }
  editTimers.set(key, setTimeout(() => {
    editTimers.delete(key);
    const latest = states.get(key);
    if (!latest || latest.baseline.length === 0 || document.isClosed) {
      return;
    }
    latest.mapped = mapAnnotations(latest.baseline, documentLines(document));
    renderDocument(document);
  }, 180));
}

function renderDocument(document: vscode.TextDocument): void {
  for (const editor of vscode.window.visibleTextEditors) {
    if (editor.document === document) {
      renderEditor(editor);
    }
  }
}

function renderEditor(editor: vscode.TextEditor): void {
  if (!enabled || editor.document.uri.scheme !== 'file') {
    clearDecorations(editor);
    return;
  }

  const state = states.get(editor.document.uri.toString());
  if (!state) {
    clearDecorations(editor);
    return;
  }

  const historyOptions: vscode.DecorationOptions[] = [];
  const localOptions: vscode.DecorationOptions[] = [];
  const errorOptions: vscode.DecorationOptions[] = [];

  if (state.error && editor.document.lineCount > 0) {
    errorOptions.push({
      range: editor.document.lineAt(0).range,
      hoverMessage: state.error,
      renderOptions: {
        before: {
          contentText: 'P4 !  ',
          color: new vscode.ThemeColor('errorForeground'),
          fontWeight: 'bold',
          margin: '0 1.5em 0 0'
        }
      }
    });
  }

  for (const lineNumber of visibleLineNumbers(editor)) {
    const annotation = state.mapped[lineNumber];
    if (!annotation) {
      continue;
    }

    const range = editor.document.lineAt(lineNumber).range;
    if (annotation.isLocal) {
      localOptions.push({
        range,
        hoverMessage: '这一行相对 workspace 的 #have 版本有本地改动，尚无已提交归属。',
        renderOptions: {
          before: {
            contentText: '本地修改  ',
            color: new vscode.ThemeColor('editorWarning.foreground'),
            margin: '0 1.5em 0 0'
          }
        }
      });
      continue;
    }

    historyOptions.push({
      range,
      hoverMessage: historyHover(editor.document.uri, annotation, state.workspace),
      renderOptions: {
        before: {
          contentText: `${annotation.user} · ${annotation.change}  `,
          color: new vscode.ThemeColor('editorCodeLens.foreground'),
          margin: '0 1.5em 0 0'
        }
      }
    });
  }

  editor.setDecorations(historyDecoration, historyOptions);
  editor.setDecorations(localDecoration, localOptions);
  editor.setDecorations(errorDecoration, errorOptions);
}

function historyHover(
  uri: vscode.Uri,
  annotation: AnnotationLine,
  workspace: P4WorkspaceContext | undefined
): vscode.MarkdownString {
  const hover = new vscode.MarkdownString();
  hover.appendMarkdown(`**提交人：** ${escapeMarkdown(annotation.user)}  \n`);
  hover.appendMarkdown(`**Changelist：** ${annotation.change}  \n`);
  if (annotation.modifiedAt) {
    hover.appendMarkdown(`**时间：** ${escapeMarkdown(annotation.modifiedAt)}  \n`);
  }
  if (annotation.client) {
    hover.appendMarkdown(`**提交 workspace：** ${escapeMarkdown(annotation.client)}  \n`);
  }
  if (workspace) {
    hover.appendMarkdown(`**当前 server：** ${escapeMarkdown(workspace.connectionAddress)}  \n`);
    hover.appendMarkdown(`**当前 client：** ${escapeMarkdown(workspace.client)}  \n\n`);
  }
  const commandArgs = encodeURIComponent(JSON.stringify([{
    uri: uri.toString(),
    change: annotation.change
  }]));
  hover.appendMarkdown(`[在 P4V 中打开 CL ${annotation.change}](command:p4HistroyPlugin.openChange?${commandArgs})`);
  hover.isTrusted = true;
  return hover;
}

function openChange(argument?: OpenChangeArgument): void {
  const uri = toUri(argument?.uri) ?? vscode.window.activeTextEditor?.document.uri;
  if (!uri) {
    return;
  }

  const state = states.get(uri.toString());
  if (!state?.workspace) {
    void vscode.window.showWarningMessage('当前文件还没有可用的 P4 行级记录。');
    return;
  }

  let change = argument?.change;
  if (!change) {
    const activeEditor = vscode.window.activeTextEditor;
    const line = argument?.lineNumber
      ? argument.lineNumber - 1
      : activeEditor?.document.uri.toString() === uri.toString()
        ? activeEditor.selection.active.line
        : undefined;
    const annotation = line === undefined ? undefined : state.mapped[line];
    if (annotation && !annotation.isLocal) {
      change = annotation.change;
    }
  }

  if (!change) {
    void vscode.window.showInformationMessage('当前行是本地修改，或没有可打开的已提交 changelist。');
    return;
  }
  service.openChange(state.workspace, change);
}

function updateStatusBar(): void {
  const editor = vscode.window.activeTextEditor;
  if (!enabled) {
    statusBar.text = '$(eye-closed) P4 History';
    statusBar.tooltip = '行级记录已隐藏；点击重新显示';
    return;
  }

  const state = editor ? states.get(editor.document.uri.toString()) : undefined;
  if (state?.loading) {
    statusBar.text = '$(sync~spin) P4 History';
    statusBar.tooltip = '正在查询当前文件的 P4 annotate 数据';
  } else if (state?.error) {
    statusBar.text = '$(warning) P4 History';
    statusBar.tooltip = state.error;
  } else if (state?.workspace) {
    statusBar.text = `$(git-commit) P4 ${state.workspace.client}`;
    statusBar.tooltip = `${state.workspace.connectionAddress}\n${state.workspace.client}\n点击隐藏行级记录`;
  } else {
    statusBar.text = '$(git-commit) P4 History';
    statusBar.tooltip = '点击隐藏行级记录';
  }
}

async function refreshVisibleEditors(): Promise<void> {
  const documents = new Map<string, vscode.TextDocument>();
  for (const editor of vscode.window.visibleTextEditors) {
    if (editor.document.uri.scheme === 'file') {
      service.invalidate(editor.document.fileName);
      documents.set(editor.document.uri.toString(), editor.document);
    }
  }
  await Promise.all([...documents.values()].map(loadDocument));
}

function visibleLineNumbers(editor: vscode.TextEditor): number[] {
  const numbers = new Set<number>();
  for (const visibleRange of editor.visibleRanges) {
    const end = Math.min(visibleRange.end.line, editor.document.lineCount - 1);
    for (let line = visibleRange.start.line; line <= end; line++) {
      numbers.add(line);
    }
  }
  return [...numbers].sort((left, right) => left - right);
}

function documentLines(document: vscode.TextDocument): string[] {
  const lines = new Array<string>(document.lineCount);
  for (let index = 0; index < lines.length; index++) {
    lines[index] = document.lineAt(index).text;
  }
  return lines;
}

function clearDecorations(editor: vscode.TextEditor): void {
  editor.setDecorations(historyDecoration, []);
  editor.setDecorations(localDecoration, []);
  editor.setDecorations(errorDecoration, []);
}

function findDocument(resource?: vscode.Uri): vscode.TextDocument | undefined {
  return resource
    ? vscode.workspace.textDocuments.find(document => document.uri.toString() === resource.toString())
    : undefined;
}

function toUri(value: string | vscode.Uri | undefined): vscode.Uri | undefined {
  if (!value) {
    return undefined;
  }
  return typeof value === 'string' ? vscode.Uri.parse(value) : value;
}

function escapeMarkdown(value: string): string {
  return value.replace(/[\\`*_{}\[\]()#+\-.!]/g, '\\$&');
}
