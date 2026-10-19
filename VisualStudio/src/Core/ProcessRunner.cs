using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace P4HistroyPlugin.Core
{
    internal sealed class ProcessResult
    {
        public ProcessResult(int exitCode, string standardOutput, string standardError)
        {
            ExitCode = exitCode;
            StandardOutput = standardOutput ?? string.Empty;
            StandardError = standardError ?? string.Empty;
        }

        public int ExitCode { get; }

        public string StandardOutput { get; }

        public string StandardError { get; }

        public bool Succeeded => ExitCode == 0;
    }

    internal static class ProcessRunner
    {
        public static Task<ProcessResult> RunAsync(
            string executable,
            IEnumerable<string> arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            return Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();

                var startInfo = new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8,
                };

                using (var process = new Process { StartInfo = startInfo })
                using (cancellationToken.Register(() => TryKill(process)))
                {
                    process.Start();
                    var standardOutputTask = process.StandardOutput.ReadToEndAsync();
                    var standardErrorTask = process.StandardError.ReadToEndAsync();
                    process.WaitForExit();
                    Task.WaitAll(standardOutputTask, standardErrorTask);
                    cancellationToken.ThrowIfCancellationRequested();
                    return new ProcessResult(process.ExitCode, standardOutputTask.Result, standardErrorTask.Result);
                }
            }, cancellationToken);
        }

        public static bool TryStartDetached(string executable, IEnumerable<string> arguments, string workingDirectory)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = executable,
                    Arguments = string.Join(" ", arguments.Select(QuoteArgument)),
                    WorkingDirectory = workingDirectory,
                    UseShellExecute = true,
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        internal static string QuoteArgument(string argument)
        {
            if (argument == null)
            {
                return "\"\"";
            }

            if (argument.Length > 0 && argument.IndexOfAny(new[] { ' ', '\t', '\n', '\v', '"' }) < 0)
            {
                return argument;
            }

            var result = new StringBuilder(argument.Length + 2);
            result.Append('"');
            var backslashes = 0;
            foreach (var character in argument)
            {
                if (character == '\\')
                {
                    backslashes++;
                    continue;
                }

                if (character == '"')
                {
                    result.Append('\\', (backslashes * 2) + 1);
                    result.Append('"');
                    backslashes = 0;
                    continue;
                }

                result.Append('\\', backslashes);
                result.Append(character);
                backslashes = 0;
            }

            result.Append('\\', backslashes * 2);
            result.Append('"');
            return result.ToString();
        }

        private static void TryKill(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // The process either has not started yet or exited between checks.
            }
        }
    }
}
