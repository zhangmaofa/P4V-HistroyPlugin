using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace P4HistroyPlugin.Core
{
    internal static class P4TaggedParser
    {
        private const string Prefix = "... ";

        public static IReadOnlyList<AnnotationLine> ParseAnnotations(string output)
        {
            var annotations = new List<AnnotationLine>();
            var fields = new Dictionary<string, string>(StringComparer.Ordinal);

            using (var reader = new StringReader(output ?? string.Empty))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!TryReadField(line, out var key, out var value))
                    {
                        continue;
                    }

                    fields[key] = value;
                    if (!string.Equals(key, "data", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    if (fields.TryGetValue("lower", out var changeText)
                        && int.TryParse(changeText, NumberStyles.None, CultureInfo.InvariantCulture, out var change))
                    {
                        fields.TryGetValue("user", out var user);
                        fields.TryGetValue("client", out var client);
                        fields.TryGetValue("time", out var timeText);

                        DateTime? modifiedAt = null;
                        if (DateTime.TryParseExact(
                            timeText,
                            "yyyy/MM/dd HH:mm:ss",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.AssumeLocal,
                            out var parsedTime))
                        {
                            modifiedAt = parsedTime;
                        }

                        annotations.Add(new AnnotationLine(change, user ?? string.Empty, modifiedAt, client ?? string.Empty, value));
                    }

                    fields.Clear();
                }
            }

            return annotations;
        }

        public static IReadOnlyList<IReadOnlyDictionary<string, string>> ParseRecords(string output)
        {
            var records = new List<IReadOnlyDictionary<string, string>>();
            var current = new Dictionary<string, string>(StringComparer.Ordinal);

            using (var reader = new StringReader(output ?? string.Empty))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (line.Length == 0)
                    {
                        FlushRecord(records, current);
                        continue;
                    }

                    if (TryReadField(line, out var key, out var value))
                    {
                        current[key] = value;
                    }
                }
            }

            FlushRecord(records, current);
            return records;
        }

        public static string ParseSettingValue(string output, string settingName)
        {
            if (string.IsNullOrWhiteSpace(settingName))
            {
                return string.Empty;
            }

            var prefix = settingName + "=";
            using (var reader = new StringReader(output ?? string.Empty))
            {
                string? line;
                while ((line = reader.ReadLine()) != null)
                {
                    if (!line.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var value = line.Substring(prefix.Length).Trim();
                    var sourceSuffix = value.LastIndexOf(" (", StringComparison.Ordinal);
                    return sourceSuffix > 0 ? value.Substring(0, sourceSuffix).TrimEnd() : value;
                }
            }

            return string.Empty;
        }

        private static bool TryReadField(string line, out string key, out string value)
        {
            key = string.Empty;
            value = string.Empty;
            if (!line.StartsWith(Prefix, StringComparison.Ordinal))
            {
                return false;
            }

            var separator = line.IndexOf(' ', Prefix.Length);
            if (separator < 0)
            {
                key = line.Substring(Prefix.Length);
                return key.Length > 0;
            }

            key = line.Substring(Prefix.Length, separator - Prefix.Length);
            value = line.Substring(separator + 1);
            return key.Length > 0;
        }

        private static void FlushRecord(
            ICollection<IReadOnlyDictionary<string, string>> records,
            Dictionary<string, string> current)
        {
            if (current.Count == 0)
            {
                return;
            }

            records.Add(new Dictionary<string, string>(current, StringComparer.Ordinal));
            current.Clear();
        }
    }
}
