import assert from 'node:assert/strict';
import { promises as fs } from 'node:fs';
import * as os from 'node:os';
import * as path from 'node:path';
import test from 'node:test';
import { WorkspaceSettingsStore } from '../workspaceSettings';

test('persists an automatically discovered workspace', async () => {
  const temporaryRoot = await fs.mkdtemp(path.join(os.tmpdir(), 'p4histroy-settings-'));
  try {
    const workspaceRoot = path.join(temporaryRoot, 'workspace');
    const settingsPath = path.join(temporaryRoot, 'workspaces.json');
    const store = new WorkspaceSettingsStore(settingsPath);

    await store.saveAuto({
      root: workspaceRoot,
      connectionAddress: 'ssl:auto:1666',
      user: 'alice',
      client: 'auto-client',
      workingDirectory: workspaceRoot,
      isManual: false
    });

    const reloaded = new WorkspaceSettingsStore(settingsPath);
    const saved = await reloaded.findForFile(path.join(workspaceRoot, 'Assets', 'file.cs'));
    assert.ok(saved);
    assert.equal(saved.manual, false);
    assert.equal(saved.port, 'ssl:auto:1666');
    assert.equal(saved.client, 'auto-client');
  } finally {
    await fs.rm(temporaryRoot, { recursive: true, force: true });
  }
});

test('persists a manual workspace and matches files below its root', async () => {
  const temporaryRoot = await fs.mkdtemp(path.join(os.tmpdir(), 'p4histroy-settings-'));
  try {
    const workspaceRoot = path.join(temporaryRoot, 'workspace');
    const settingsPath = path.join(temporaryRoot, 'settings', 'workspaces.json');
    const store = new WorkspaceSettingsStore(settingsPath);

    await store.saveManual(workspaceRoot, 'ssl:p4.example:1666', 'alice', 'alice-main');

    const reloaded = new WorkspaceSettingsStore(settingsPath);
    const saved = await reloaded.findForFile(path.join(workspaceRoot, 'Assets', 'file.cs'));
    assert.ok(saved);
    assert.equal(saved.manual, true);
    assert.equal(saved.port, 'ssl:p4.example:1666');
    assert.equal(saved.client, 'alice-main');
  } finally {
    await fs.rm(temporaryRoot, { recursive: true, force: true });
  }
});

test('automatic capture does not overwrite a manual workspace', async () => {
  const temporaryRoot = await fs.mkdtemp(path.join(os.tmpdir(), 'p4histroy-settings-'));
  try {
    const workspaceRoot = path.join(temporaryRoot, 'workspace');
    const settingsPath = path.join(temporaryRoot, 'workspaces.json');
    const store = new WorkspaceSettingsStore(settingsPath);
    await store.saveManual(workspaceRoot, 'ssl:manual:1666', 'alice', 'manual-client');

    await store.saveAuto({
      root: workspaceRoot,
      connectionAddress: 'ssl:auto:1666',
      user: 'alice',
      client: 'auto-client',
      workingDirectory: workspaceRoot,
      isManual: false
    });

    const saved = await store.findForFile(path.join(workspaceRoot, 'file.cs'));
    assert.ok(saved);
    assert.equal(saved.manual, true);
    assert.equal(saved.client, 'manual-client');
  } finally {
    await fs.rm(temporaryRoot, { recursive: true, force: true });
  }
});
