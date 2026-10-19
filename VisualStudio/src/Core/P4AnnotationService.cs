using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace P4HistroyPlugin.Core
{
    internal sealed class AnnotationLoadResult
    {
        private AnnotationLoadResult(
            P4WorkspaceContext? workspace,
            IReadOnlyList<AnnotationLine> annotations,
            string error)
        {
            Workspace = workspace;
            Annotations = annotations;
            Error = error;
        }

        public P4WorkspaceContext? Workspace { get; }

        public IReadOnlyList<AnnotationLine> Annotations { get; }

        public string Error { get; }

        public bool Succeeded => Workspace != null && Annotations.Count > 0;

        public static AnnotationLoadResult Success(P4WorkspaceContext workspace, IReadOnlyList<AnnotationLine> annotations)
        {
            return new AnnotationLoadResult(workspace, annotations, string.Empty);
        }

        public static AnnotationLoadResult Failure(string error)
        {
            return new AnnotationLoadResult(null, Array.Empty<AnnotationLine>(), error ?? string.Empty);
        }
    }

    internal sealed class P4AnnotationService
    {
        private readonly P4WorkspaceResolver _workspaceResolver = new P4WorkspaceResolver();

        public async Task<AnnotationLoadResult> LoadAsync(string filePath, CancellationToken cancellationToken)
        {
            try
            {
                if (!File.Exists(filePath))
                {
                    return AnnotationLoadResult.Failure("文件不存在。");
                }

                var workspace = await _workspaceResolver.ResolveAsync(filePath, cancellationToken).ConfigureAwait(false);
                if (workspace == null)
                {
                    return AnnotationLoadResult.Failure("当前文件不在可用的 P4 workspace 中。");
                }

                var result = await P4WorkspaceResolver.RunP4Async(
                    workspace.WorkingDirectory,
                    workspace.Client,
                    new[] { "annotate", "-c", "-u", "-q", filePath + "#have" },
                    cancellationToken).ConfigureAwait(false);

                if (!result.Succeeded)
                {
                    return AnnotationLoadResult.Failure(FirstUsefulError(result));
                }

                var annotations = P4TaggedParser.ParseAnnotations(result.StandardOutput);
                return annotations.Count == 0
                    ? AnnotationLoadResult.Failure("P4 annotate 没有返回文本行。")
                    : AnnotationLoadResult.Success(workspace, annotations);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception exception)
            {
                return AnnotationLoadResult.Failure(exception.Message);
            }
        }

        public void Invalidate(string filePath)
        {
            _workspaceResolver.Invalidate(filePath);
        }

        public bool OpenChange(P4WorkspaceContext workspace, int change)
        {
            if (workspace == null || change <= 0)
            {
                return false;
            }

            var arguments = new List<string>();
            arguments.Add("-p4vc");
            if (!string.IsNullOrWhiteSpace(workspace.ConnectionAddress))
            {
                arguments.Add("-p");
                arguments.Add(workspace.ConnectionAddress);
            }

            if (!string.IsNullOrWhiteSpace(workspace.User))
            {
                arguments.Add("-u");
                arguments.Add(workspace.User);
            }

            arguments.Add("-c");
            arguments.Add(workspace.Client);
            arguments.Add("change");
            arguments.Add(change.ToString(System.Globalization.CultureInfo.InvariantCulture));
            return ProcessRunner.TryStartDetached("p4v.exe", arguments, workspace.WorkingDirectory);
        }

        private static string FirstUsefulError(ProcessResult result)
        {
            var error = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput
                : result.StandardError;
            using (var reader = new StringReader(error ?? string.Empty))
            {
                return reader.ReadLine() ?? "P4 命令执行失败。";
            }
        }
    }
}
