using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace P4HistroyPlugin.Core
{
    internal sealed class P4WorkspaceContext
    {
        public P4WorkspaceContext(
            string client,
            string user,
            string connectionAddress,
            string workingDirectory,
            string root,
            bool isManual)
        {
            Client = client;
            User = user;
            ConnectionAddress = connectionAddress;
            WorkingDirectory = workingDirectory;
            Root = root;
            IsManual = isManual;
        }

        public string Client { get; }

        public string User { get; }

        public string ConnectionAddress { get; }

        public string WorkingDirectory { get; }

        public string Root { get; }

        public bool IsManual { get; }
    }

    internal sealed class P4WorkspaceResolver
    {
        private readonly ConcurrentDictionary<string, Task<P4WorkspaceContext?>> _cache
            = new ConcurrentDictionary<string, Task<P4WorkspaceContext?>>(StringComparer.OrdinalIgnoreCase);
        private readonly P4WorkspaceSettingsStore _settings = new P4WorkspaceSettingsStore();

        public string SettingsPath => _settings.SettingsPath;

        public async Task<P4WorkspaceContext?> ResolveAsync(string filePath, CancellationToken cancellationToken)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Environment.CurrentDirectory;
            var resolution = _cache.GetOrAdd(directory, _ => ResolveCoreAsync(filePath, directory, cancellationToken));
            try
            {
                return await resolution.ConfigureAwait(false);
            }
            catch
            {
                if (_cache.TryGetValue(directory, out var cached) && ReferenceEquals(cached, resolution))
                {
                    _cache.TryRemove(directory, out _);
                }

                throw;
            }
        }

        public void Invalidate(string filePath)
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(filePath)) ?? Environment.CurrentDirectory;
            _cache.TryRemove(directory, out _);
        }

        public void SaveManual(string root, string port, string user, string client)
        {
            _settings.SaveManual(root, port, user, client);
            _cache.Clear();
        }

        public bool ClearSaved(string filePath)
        {
            var removed = _settings.RemoveForFile(filePath);
            _cache.Clear();
            return removed;
        }

        private async Task<P4WorkspaceContext?> ResolveCoreAsync(
            string filePath,
            string directory,
            CancellationToken cancellationToken)
        {
            var saved = _settings.FindForFile(filePath);
            if (saved != null)
            {
                return new P4WorkspaceContext(
                    saved.Client,
                    saved.User,
                    saved.Port,
                    directory,
                    saved.Root,
                    saved.Manual);
            }

            var infoResult = await RunP4Async(
                directory,
                client: null,
                user: null,
                connectionAddress: null,
                new[] { "info", "-s" },
                cancellationToken).ConfigureAwait(false);
            var info = FirstRecord(infoResult.StandardOutput);
            if (!TryGet(info, "userName", out var user))
            {
                return null;
            }

            TryGet(info, "clientName", out var configuredClient);
            TryGet(info, "clientHost", out var clientHost);
            TryGet(info, "serverAddress", out var reportedServerAddress);
            var connectionAddress = await ReadP4SettingAsync(directory, "P4PORT", cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(connectionAddress))
            {
                connectionAddress = reportedServerAddress;
            }

            if (!string.IsNullOrWhiteSpace(configuredClient)
                && await MapsFileAsync(directory, configuredClient, user, connectionAddress, filePath, cancellationToken).ConfigureAwait(false))
            {
                var root = await ReadClientRootAsync(
                    directory,
                    configuredClient,
                    user,
                    connectionAddress,
                    cancellationToken).ConfigureAwait(false);
                var workspace = new P4WorkspaceContext(
                    configuredClient,
                    user,
                    connectionAddress,
                    directory,
                    string.IsNullOrWhiteSpace(root) ? directory : root,
                    isManual: false);
                _settings.SaveAuto(workspace);
                return workspace;
            }

            var clientsResult = await RunP4Async(
                directory,
                client: null,
                user,
                connectionAddress,
                new[] { "clients", "-u", user },
                cancellationToken).ConfigureAwait(false);
            if (!clientsResult.Succeeded)
            {
                return null;
            }

            var fullFilePath = Path.GetFullPath(filePath);
            var candidates = P4TaggedParser.ParseRecords(clientsResult.StandardOutput)
                .Select(record => new
                {
                    Client = Get(record, "client"),
                    Root = Get(record, "Root"),
                    Host = Get(record, "Host"),
                })
                .Where(candidate => !string.IsNullOrWhiteSpace(candidate.Client)
                                    && !string.IsNullOrWhiteSpace(candidate.Root)
                                    && IsHostMatch(candidate.Host, clientHost)
                                    && IsPathWithinRoot(fullFilePath, candidate.Root))
                .OrderByDescending(candidate => Path.GetFullPath(candidate.Root).Length)
                .ToArray();

            foreach (var candidate in candidates)
            {
                if (await MapsFileAsync(
                        directory,
                        candidate.Client,
                        user,
                        connectionAddress,
                        fullFilePath,
                        cancellationToken).ConfigureAwait(false))
                {
                    var workspace = new P4WorkspaceContext(
                        candidate.Client,
                        user,
                        connectionAddress,
                        directory,
                        candidate.Root,
                        isManual: false);
                    _settings.SaveAuto(workspace);
                    return workspace;
                }
            }

            return null;
        }

        internal static Task<ProcessResult> RunP4Async(
            string workingDirectory,
            string? client,
            string? user,
            string? connectionAddress,
            IEnumerable<string> commandArguments,
            CancellationToken cancellationToken)
        {
            var arguments = new List<string>();
            if (!string.IsNullOrWhiteSpace(connectionAddress))
            {
                arguments.Add("-p");
                arguments.Add(connectionAddress!);
            }

            if (!string.IsNullOrWhiteSpace(user))
            {
                arguments.Add("-u");
                arguments.Add(user!);
            }

            if (!string.IsNullOrWhiteSpace(client))
            {
                arguments.Add("-c");
                arguments.Add(client!);
            }

            arguments.Add("-d");
            arguments.Add(workingDirectory);
            arguments.Add("-ztag");
            arguments.AddRange(commandArguments);
            return ProcessRunner.RunAsync("p4.exe", arguments, workingDirectory, cancellationToken);
        }

        private static async Task<string> ReadP4SettingAsync(
            string workingDirectory,
            string settingName,
            CancellationToken cancellationToken)
        {
            var result = await ProcessRunner.RunAsync(
                "p4.exe",
                new[] { "-d", workingDirectory, "set", "-q", settingName },
                workingDirectory,
                cancellationToken).ConfigureAwait(false);

            return result.Succeeded
                ? P4TaggedParser.ParseSettingValue(result.StandardOutput, settingName)
                : string.Empty;
        }

        private static async Task<bool> MapsFileAsync(
            string directory,
            string client,
            string user,
            string connectionAddress,
            string filePath,
            CancellationToken cancellationToken)
        {
            var result = await RunP4Async(
                directory,
                client,
                user,
                connectionAddress,
                new[] { "where", filePath },
                cancellationToken).ConfigureAwait(false);
            if (!result.Succeeded)
            {
                return false;
            }

            return P4TaggedParser.ParseRecords(result.StandardOutput)
                .Any(record => TryGet(record, "path", out var mappedPath)
                               && PathsEqual(mappedPath, filePath));
        }

        private static async Task<string> ReadClientRootAsync(
            string directory,
            string client,
            string user,
            string connectionAddress,
            CancellationToken cancellationToken)
        {
            var result = await RunP4Async(
                directory,
                client,
                user,
                connectionAddress,
                new[] { "client", "-o", client },
                cancellationToken).ConfigureAwait(false);
            var record = FirstRecord(result.StandardOutput);
            return Get(record, "Root");
        }

        private static IReadOnlyDictionary<string, string> FirstRecord(string output)
        {
            return P4TaggedParser.ParseRecords(output).FirstOrDefault()
                   ?? new Dictionary<string, string>(StringComparer.Ordinal);
        }

        private static bool IsHostMatch(string candidateHost, string configuredHost)
        {
            if (string.IsNullOrWhiteSpace(candidateHost))
            {
                return true;
            }

            return string.Equals(candidateHost, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
                   || (!string.IsNullOrWhiteSpace(configuredHost)
                       && string.Equals(candidateHost, configuredHost, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsPathWithinRoot(string filePath, string root)
        {
            try
            {
                var fullFile = Path.GetFullPath(filePath).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return string.Equals(fullFile, fullRoot, StringComparison.OrdinalIgnoreCase)
                       || fullFile.StartsWith(fullRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static bool PathsEqual(string left, string right)
        {
            try
            {
                return string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
            }
            catch
            {
                return false;
            }
        }

        private static string Get(IReadOnlyDictionary<string, string> record, string key)
        {
            return record.TryGetValue(key, out var value) ? value : string.Empty;
        }

        private static bool TryGet(IReadOnlyDictionary<string, string> record, string key, out string value)
        {
            if (record.TryGetValue(key, out var result))
            {
                value = result;
                return true;
            }

            value = string.Empty;
            return false;
        }
    }
}
