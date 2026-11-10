import { AnnotationLine, localAnnotation } from './types';

interface LinePair {
  readonly baseline: number;
  readonly current: number;
}

export function mapAnnotations(
  baseline: readonly AnnotationLine[],
  currentLines: readonly string[]
): AnnotationLine[] {
  const result = new Array<AnnotationLine | undefined>(currentLines.length);
  mapRange(baseline, 0, baseline.length, currentLines, 0, currentLines.length, result);
  return Array.from(
    { length: currentLines.length },
    (_, index) => result[index] ?? localAnnotation(currentLines[index])
  );
}

function mapRange(
  baseline: readonly AnnotationLine[],
  baselineStartValue: number,
  baselineEndValue: number,
  current: readonly string[],
  currentStartValue: number,
  currentEndValue: number,
  result: Array<AnnotationLine | undefined>
): void {
  let baselineStart = baselineStartValue;
  let baselineEnd = baselineEndValue;
  let currentStart = currentStartValue;
  let currentEnd = currentEndValue;

  while (baselineStart < baselineEnd && currentStart < currentEnd
    && baseline[baselineStart].text === current[currentStart]) {
    result[currentStart] = baseline[baselineStart];
    baselineStart++;
    currentStart++;
  }

  while (baselineStart < baselineEnd && currentStart < currentEnd
    && baseline[baselineEnd - 1].text === current[currentEnd - 1]) {
    baselineEnd--;
    currentEnd--;
    result[currentEnd] = baseline[baselineEnd];
  }

  if (baselineStart >= baselineEnd || currentStart >= currentEnd) {
    return;
  }

  const baselineUnique = findUniqueLines(
    baseline,
    baselineStart,
    baselineEnd,
    item => item.text
  );
  const currentUnique = findUniqueLines(current, currentStart, currentEnd, item => item);
  const pairs: LinePair[] = [];

  for (const [text, baselineIndex] of baselineUnique) {
    const currentIndex = currentUnique.get(text);
    if (currentIndex !== undefined) {
      pairs.push({ baseline: baselineIndex, current: currentIndex });
    }
  }

  pairs.sort((left, right) => left.baseline - right.baseline);
  const anchors = longestIncreasingSubsequence(pairs);
  if (anchors.length === 0) {
    return;
  }

  let previousBaseline = baselineStart;
  let previousCurrent = currentStart;
  for (const anchor of anchors) {
    mapRange(
      baseline,
      previousBaseline,
      anchor.baseline,
      current,
      previousCurrent,
      anchor.current,
      result
    );
    result[anchor.current] = baseline[anchor.baseline];
    previousBaseline = anchor.baseline + 1;
    previousCurrent = anchor.current + 1;
  }

  mapRange(
    baseline,
    previousBaseline,
    baselineEnd,
    current,
    previousCurrent,
    currentEnd,
    result
  );
}

function findUniqueLines<T>(
  lines: readonly T[],
  start: number,
  end: number,
  selectText: (item: T) => string
): Map<string, number> {
  const occurrences = new Map<string, { index: number; count: number }>();
  for (let index = start; index < end; index++) {
    const text = selectText(lines[index]) ?? '';
    const existing = occurrences.get(text);
    occurrences.set(text, existing
      ? { index: existing.index, count: existing.count + 1 }
      : { index, count: 1 });
  }

  const unique = new Map<string, number>();
  for (const [text, occurrence] of occurrences) {
    if (occurrence.count === 1) {
      unique.set(text, occurrence.index);
    }
  }
  return unique;
}

function longestIncreasingSubsequence(pairs: readonly LinePair[]): LinePair[] {
  if (pairs.length === 0) {
    return [];
  }

  const tails = new Array<number>(pairs.length);
  const previous = new Array<number>(pairs.length);
  let length = 0;

  for (let index = 0; index < pairs.length; index++) {
    let low = 0;
    let high = length;
    while (low < high) {
      const middle = low + Math.floor((high - low) / 2);
      if (pairs[tails[middle]].current < pairs[index].current) {
        low = middle + 1;
      } else {
        high = middle;
      }
    }

    previous[index] = low > 0 ? tails[low - 1] : -1;
    tails[low] = index;
    if (low === length) {
      length++;
    }
  }

  const result = new Array<LinePair>(length);
  let cursor = tails[length - 1];
  for (let index = length - 1; index >= 0; index--) {
    result[index] = pairs[cursor];
    cursor = previous[cursor];
  }
  return result;
}
