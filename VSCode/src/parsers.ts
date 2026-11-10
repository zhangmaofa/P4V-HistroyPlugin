import { AnnotationLine } from './types';

const taggedPrefix = '... ';

export function parseAnnotations(output: string): AnnotationLine[] {
  const annotations: AnnotationLine[] = [];
  const fields = new Map<string, string>();

  for (const line of splitLines(output)) {
    const field = readTaggedField(line);
    if (!field) {
      continue;
    }

    fields.set(field.key, field.value);
    if (field.key !== 'data') {
      continue;
    }

    const change = Number.parseInt(fields.get('lower') ?? '', 10);
    if (Number.isFinite(change)) {
      annotations.push({
        change,
        user: fields.get('user') ?? '',
        client: fields.get('client') ?? '',
        modifiedAt: fields.get('time') || undefined,
        text: field.value,
        isLocal: false
      });
    }

    fields.clear();
  }

  return annotations;
}

export function parseTaggedRecords(output: string): Array<Map<string, string>> {
  const records: Array<Map<string, string>> = [];
  let current = new Map<string, string>();

  const flush = (): void => {
    if (current.size > 0) {
      records.push(current);
      current = new Map<string, string>();
    }
  };

  for (const line of splitLines(output)) {
    if (line.length === 0) {
      flush();
      continue;
    }

    const field = readTaggedField(line);
    if (!field) {
      continue;
    }

    if (current.has(field.key) && (field.key === 'client' || field.key === 'depotFile')) {
      flush();
    }

    current.set(field.key, field.value);
  }

  flush();
  return records;
}

export function parseSettingValue(output: string, settingName: string): string {
  const prefix = `${settingName}=`;
  for (const line of splitLines(output)) {
    if (!line.toLocaleLowerCase().startsWith(prefix.toLocaleLowerCase())) {
      continue;
    }

    const value = line.slice(prefix.length).trim();
    const sourceSuffix = value.lastIndexOf(' (');
    return sourceSuffix > 0 ? value.slice(0, sourceSuffix).trimEnd() : value;
  }

  return '';
}

function readTaggedField(line: string): { key: string; value: string } | undefined {
  if (!line.startsWith(taggedPrefix)) {
    return undefined;
  }

  const separator = line.indexOf(' ', taggedPrefix.length);
  if (separator < 0) {
    const key = line.slice(taggedPrefix.length);
    return key.length > 0 ? { key, value: '' } : undefined;
  }

  const key = line.slice(taggedPrefix.length, separator);
  return key.length > 0 ? { key, value: line.slice(separator + 1) } : undefined;
}

function splitLines(value: string): string[] {
  return (value ?? '').replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
}
