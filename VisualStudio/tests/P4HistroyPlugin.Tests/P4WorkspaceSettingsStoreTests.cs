using System;
using System.IO;
using P4HistroyPlugin.Core;
using Xunit;

namespace P4HistroyPlugin.Tests;

public sealed class P4WorkspaceSettingsStoreTests
{
    [Fact]
    public void PersistsAutomaticallyDiscoveredWorkspace()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var settingsPath = Path.Combine(temporaryRoot, "workspaces.json");
            var workspaceRoot = Path.Combine(temporaryRoot, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            var store = new P4WorkspaceSettingsStore(settingsPath);

            store.SaveAuto(new P4WorkspaceContext(
                "auto-client",
                "alice",
                "ssl:auto:1666",
                workspaceRoot,
                workspaceRoot,
                isManual: false));

            var saved = new P4WorkspaceSettingsStore(settingsPath)
                .FindForFile(Path.Combine(workspaceRoot, "Assets", "file.cs"));
            Assert.NotNull(saved);
            Assert.False(saved!.Manual);
            Assert.Equal("ssl:auto:1666", saved.Port);
            Assert.Equal("auto-client", saved.Client);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void SavesAndLoadsManualWorkspaceByLocalRoot()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var settingsPath = Path.Combine(temporaryRoot, "settings", "workspaces.json");
            var workspaceRoot = Path.Combine(temporaryRoot, "workspace");
            var filePath = Path.Combine(workspaceRoot, "Assets", "file.cs");
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            var store = new P4WorkspaceSettingsStore(settingsPath);

            store.SaveManual(workspaceRoot, "ssl:p4.example:1666", "alice", "alice-main");

            var saved = new P4WorkspaceSettingsStore(settingsPath).FindForFile(filePath);
            Assert.NotNull(saved);
            Assert.True(saved!.Manual);
            Assert.Equal("ssl:p4.example:1666", saved.Port);
            Assert.Equal("alice-main", saved.Client);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    [Fact]
    public void AutomaticCaptureDoesNotOverwriteManualWorkspace()
    {
        var temporaryRoot = CreateTemporaryRoot();
        try
        {
            var settingsPath = Path.Combine(temporaryRoot, "workspaces.json");
            var workspaceRoot = Path.Combine(temporaryRoot, "workspace");
            Directory.CreateDirectory(workspaceRoot);
            var store = new P4WorkspaceSettingsStore(settingsPath);
            store.SaveManual(workspaceRoot, "ssl:manual:1666", "alice", "manual-client");

            store.SaveAuto(new P4WorkspaceContext(
                "auto-client",
                "alice",
                "ssl:auto:1666",
                workspaceRoot,
                workspaceRoot,
                isManual: false));

            var saved = store.FindForFile(Path.Combine(workspaceRoot, "file.cs"));
            Assert.NotNull(saved);
            Assert.True(saved!.Manual);
            Assert.Equal("manual-client", saved.Client);
        }
        finally
        {
            Directory.Delete(temporaryRoot, recursive: true);
        }
    }

    private static string CreateTemporaryRoot()
    {
        var path = Path.Combine(Path.GetTempPath(), "P4HistroyPlugin.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
