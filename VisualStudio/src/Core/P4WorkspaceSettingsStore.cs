using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace P4HistroyPlugin.Core
{
    [DataContract]
    internal sealed class P4WorkspaceSetting
    {
        [DataMember(Name = "root")]
        public string Root { get; set; } = string.Empty;

        [DataMember(Name = "port")]
        public string Port { get; set; } = string.Empty;

        [DataMember(Name = "user")]
        public string User { get; set; } = string.Empty;

        [DataMember(Name = "client")]
        public string Client { get; set; } = string.Empty;

        [DataMember(Name = "manual")]
        public bool Manual { get; set; }

        [DataMember(Name = "updatedAt")]
        public string UpdatedAt { get; set; } = string.Empty;
    }

    [DataContract]
    internal sealed class P4WorkspaceSettingsDocument
    {
        [DataMember(Name = "version")]
        public int Version { get; set; } = 1;

        [DataMember(Name = "workspaces")]
        public List<P4WorkspaceSetting> Workspaces { get; set; } = new List<P4WorkspaceSetting>();
    }

    internal sealed class P4WorkspaceSettingsStore
    {
        private readonly object _gate = new object();
        private readonly string _settingsPath;

        public P4WorkspaceSettingsStore()
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "P4HistroyPlugin",
                "workspaces.json"))
        {
        }

        internal P4WorkspaceSettingsStore(string settingsPath)
        {
            _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
        }

        public string SettingsPath => _settingsPath;

        public P4WorkspaceSetting? FindForFile(string filePath)
        {
            lock (_gate)
            {
                return Load().Workspaces
                    .Where(setting => IsComplete(setting) && IsPathWithinRoot(filePath, setting.Root))
                    .OrderByDescending(setting => NormalizePath(setting.Root).Length)
                    .FirstOrDefault();
            }
        }

        public void SaveAuto(P4WorkspaceContext workspace)
        {
            Save(new P4WorkspaceSetting
            {
                Root = workspace.Root,
                Port = workspace.ConnectionAddress,
                User = workspace.User,
                Client = workspace.Client,
                Manual = false,
                UpdatedAt = DateTime.UtcNow.ToString("O"),
            });
        }

        public void SaveManual(string root, string port, string user, string client)
        {
            Save(new P4WorkspaceSetting
            {
                Root = root,
                Port = port,
                User = user,
                Client = client,
                Manual = true,
                UpdatedAt = DateTime.UtcNow.ToString("O"),
            });
        }

        public bool RemoveForFile(string filePath)
        {
            lock (_gate)
            {
                var document = Load();
                var match = document.Workspaces
                    .Where(setting => IsPathWithinRoot(filePath, setting.Root))
                    .OrderByDescending(setting => NormalizePath(setting.Root).Length)
                    .FirstOrDefault();
                if (match == null)
                {
                    return false;
                }

                document.Workspaces.Remove(match);
                Write(document);
                return true;
            }
        }

        private void Save(P4WorkspaceSetting setting)
        {
            if (!IsComplete(setting))
            {
                throw new ArgumentException("P4 workspace 设置必须包含 Root、P4PORT、P4USER 和 P4CLIENT。", nameof(setting));
            }

            setting.Root = NormalizePath(setting.Root);
            lock (_gate)
            {
                var document = Load();
                var existing = document.Workspaces.FirstOrDefault(item =>
                    string.Equals(NormalizePath(item.Root), setting.Root, StringComparison.OrdinalIgnoreCase));
                if (existing != null && existing.Manual && !setting.Manual)
                {
                    return;
                }

                if (existing != null)
                {
                    document.Workspaces.Remove(existing);
                }
                document.Workspaces.Add(setting);
                Write(document);
            }
        }

        private P4WorkspaceSettingsDocument Load()
        {
            try
            {
                if (!File.Exists(_settingsPath))
                {
                    return new P4WorkspaceSettingsDocument();
                }

                using (var stream = File.OpenRead(_settingsPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(P4WorkspaceSettingsDocument));
                    return serializer.ReadObject(stream) as P4WorkspaceSettingsDocument
                           ?? new P4WorkspaceSettingsDocument();
                }
            }
            catch
            {
                return new P4WorkspaceSettingsDocument();
            }
        }

        private void Write(P4WorkspaceSettingsDocument document)
        {
            var directory = Path.GetDirectoryName(_settingsPath);
            if (!string.IsNullOrWhiteSpace(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var temporaryPath = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = File.Create(temporaryPath))
                {
                    var serializer = new DataContractJsonSerializer(typeof(P4WorkspaceSettingsDocument));
                    serializer.WriteObject(stream, document);
                }

                if (File.Exists(_settingsPath))
                {
                    File.Replace(temporaryPath, _settingsPath, null, true);
                }
                else
                {
                    File.Move(temporaryPath, _settingsPath);
                }
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }

        private static bool IsComplete(P4WorkspaceSetting setting)
        {
            return setting != null
                   && !string.IsNullOrWhiteSpace(setting.Root)
                   && !string.IsNullOrWhiteSpace(setting.Port)
                   && !string.IsNullOrWhiteSpace(setting.User)
                   && !string.IsNullOrWhiteSpace(setting.Client);
        }

        private static bool IsPathWithinRoot(string filePath, string root)
        {
            try
            {
                var fullFile = NormalizePath(filePath);
                var fullRoot = NormalizePath(root);
                return string.Equals(fullFile, fullRoot, StringComparison.OrdinalIgnoreCase)
                       || fullFile.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string NormalizePath(string value)
        {
            return Path.GetFullPath(value)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }
    }
}
