using System;

namespace P4LineLens.Core
{
    internal sealed class AnnotationLine
    {
        public AnnotationLine(int change, string user, DateTime? modifiedAt, string client, string text, bool isLocal = false)
        {
            Change = change;
            User = user ?? string.Empty;
            ModifiedAt = modifiedAt;
            Client = client ?? string.Empty;
            Text = text ?? string.Empty;
            IsLocal = isLocal;
        }

        public int Change { get; }

        public string User { get; }

        public DateTime? ModifiedAt { get; }

        public string Client { get; }

        public string Text { get; }

        public bool IsLocal { get; }

        public static AnnotationLine Local(string text)
        {
            return new AnnotationLine(0, string.Empty, null, string.Empty, text, isLocal: true);
        }
    }
}
