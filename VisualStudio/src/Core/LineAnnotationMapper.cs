using System;
using System.Collections.Generic;

namespace P4HistroyPlugin.Core
{
    internal static class LineAnnotationMapper
    {
        public static AnnotationLine[] Map(IReadOnlyList<AnnotationLine> baseline, IReadOnlyList<string> currentLines)
        {
            if (baseline == null)
            {
                throw new ArgumentNullException(nameof(baseline));
            }

            if (currentLines == null)
            {
                throw new ArgumentNullException(nameof(currentLines));
            }

            var result = new AnnotationLine[currentLines.Count];
            MapRange(baseline, 0, baseline.Count, currentLines, 0, currentLines.Count, result);

            for (var i = 0; i < result.Length; i++)
            {
                if (result[i] == null)
                {
                    result[i] = AnnotationLine.Local(currentLines[i]);
                }
            }

            return result;
        }

        private static void MapRange(
            IReadOnlyList<AnnotationLine> baseline,
            int baselineStart,
            int baselineEnd,
            IReadOnlyList<string> current,
            int currentStart,
            int currentEnd,
            AnnotationLine[] result)
        {
            while (baselineStart < baselineEnd
                   && currentStart < currentEnd
                   && Same(baseline[baselineStart].Text, current[currentStart]))
            {
                result[currentStart] = baseline[baselineStart];
                baselineStart++;
                currentStart++;
            }

            while (baselineStart < baselineEnd
                   && currentStart < currentEnd
                   && Same(baseline[baselineEnd - 1].Text, current[currentEnd - 1]))
            {
                baselineEnd--;
                currentEnd--;
                result[currentEnd] = baseline[baselineEnd];
            }

            if (baselineStart >= baselineEnd || currentStart >= currentEnd)
            {
                return;
            }

            var baselineUnique = FindUniqueLines(baseline, baselineStart, baselineEnd, item => item.Text);
            var currentUnique = FindUniqueLines(current, currentStart, currentEnd, item => item);
            var pairs = new List<LinePair>();

            foreach (var item in baselineUnique)
            {
                if (currentUnique.TryGetValue(item.Key, out var currentIndex))
                {
                    pairs.Add(new LinePair(item.Value, currentIndex));
                }
            }

            pairs.Sort((left, right) => left.Baseline.CompareTo(right.Baseline));
            var anchors = LongestIncreasingSubsequence(pairs);
            if (anchors.Count == 0)
            {
                return;
            }

            var previousBaseline = baselineStart;
            var previousCurrent = currentStart;
            foreach (var anchor in anchors)
            {
                MapRange(baseline, previousBaseline, anchor.Baseline, current, previousCurrent, anchor.Current, result);
                result[anchor.Current] = baseline[anchor.Baseline];
                previousBaseline = anchor.Baseline + 1;
                previousCurrent = anchor.Current + 1;
            }

            MapRange(baseline, previousBaseline, baselineEnd, current, previousCurrent, currentEnd, result);
        }

        private static Dictionary<string, int> FindUniqueLines<T>(
            IReadOnlyList<T> lines,
            int start,
            int end,
            Func<T, string> selectText)
        {
            var counts = new Dictionary<string, Occurrence>(StringComparer.Ordinal);
            for (var i = start; i < end; i++)
            {
                var text = selectText(lines[i]) ?? string.Empty;
                if (counts.TryGetValue(text, out var occurrence))
                {
                    counts[text] = new Occurrence(occurrence.Index, occurrence.Count + 1);
                }
                else
                {
                    counts[text] = new Occurrence(i, 1);
                }
            }

            var unique = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var item in counts)
            {
                if (item.Value.Count == 1)
                {
                    unique[item.Key] = item.Value.Index;
                }
            }

            return unique;
        }

        private static List<LinePair> LongestIncreasingSubsequence(IReadOnlyList<LinePair> pairs)
        {
            if (pairs.Count == 0)
            {
                return new List<LinePair>();
            }

            var tails = new int[pairs.Count];
            var previous = new int[pairs.Count];
            var length = 0;

            for (var i = 0; i < pairs.Count; i++)
            {
                var low = 0;
                var high = length;
                while (low < high)
                {
                    var middle = low + ((high - low) / 2);
                    if (pairs[tails[middle]].Current < pairs[i].Current)
                    {
                        low = middle + 1;
                    }
                    else
                    {
                        high = middle;
                    }
                }

                previous[i] = low > 0 ? tails[low - 1] : -1;
                tails[low] = i;
                if (low == length)
                {
                    length++;
                }
            }

            var result = new LinePair[length];
            var cursor = tails[length - 1];
            for (var i = length - 1; i >= 0; i--)
            {
                result[i] = pairs[cursor];
                cursor = previous[cursor];
            }

            return new List<LinePair>(result);
        }

        private static bool Same(string left, string right)
        {
            return string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.Ordinal);
        }

        private readonly struct Occurrence
        {
            public Occurrence(int index, int count)
            {
                Index = index;
                Count = count;
            }

            public int Index { get; }

            public int Count { get; }
        }

        private readonly struct LinePair
        {
            public LinePair(int baseline, int current)
            {
                Baseline = baseline;
                Current = current;
            }

            public int Baseline { get; }

            public int Current { get; }
        }
    }
}
