import assert from 'node:assert/strict';
import test from 'node:test';
import { parseAnnotations, parseSettingValue, parseTaggedRecords } from '../parsers';

test('parses annotate metadata and source text', () => {
  const output = [
    '... lower 123',
    '... upper 123',
    '... user mickzhang',
    '... client p4_trunk_mickzhang_1498',
    '... time 2026/11/10 12:00:00',
    '... data public void Run() {}',
    ''
  ].join('\n');
  const result = parseAnnotations(output);
  assert.equal(result.length, 1);
  assert.equal(result[0].change, 123);
  assert.equal(result[0].user, 'mickzhang');
  assert.equal(result[0].text, 'public void Run() {}');
});

test('parses multiple tagged records', () => {
  const records = parseTaggedRecords([
    '... client first',
    '... Root E:\\first',
    '',
    '... client second',
    '... Root E:\\second',
    ''
  ].join('\n'));
  assert.equal(records.length, 2);
  assert.equal(records[1].get('client'), 'second');
});

test('removes the p4 setting source suffix', () => {
  assert.equal(
    parseSettingValue('P4PORT=ssl:example:1666 (set)\r\n', 'P4PORT'),
    'ssl:example:1666'
  );
});
