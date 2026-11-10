import assert from 'node:assert/strict';
import test from 'node:test';
import { mapAnnotations } from '../lineMapper';
import { AnnotationLine } from '../types';

function annotation(change: number, text: string): AnnotationLine {
  return {
    change,
    user: `user${change}`,
    client: 'client',
    text,
    isLocal: false
  };
}

test('preserves submitted ownership for unchanged lines', () => {
  const mapped = mapAnnotations(
    [annotation(1, 'alpha'), annotation(2, 'beta')],
    ['alpha', 'beta']
  );
  assert.deepEqual(mapped.map(item => item.change), [1, 2]);
  assert.equal(mapped.some(item => item.isLocal), false);
});

test('marks inserted and changed lines as local', () => {
  const mapped = mapAnnotations(
    [annotation(1, 'alpha'), annotation(2, 'beta'), annotation(3, 'gamma')],
    ['alpha', 'inserted', 'changed', 'gamma']
  );
  assert.equal(mapped[0].change, 1);
  assert.equal(mapped[1].isLocal, true);
  assert.equal(mapped[2].isLocal, true);
  assert.equal(mapped[3].change, 3);
});

test('keeps ownership after a deleted line', () => {
  const mapped = mapAnnotations(
    [annotation(1, 'alpha'), annotation(2, 'deleted'), annotation(3, 'gamma')],
    ['alpha', 'gamma']
  );
  assert.deepEqual(mapped.map(item => item.change), [1, 3]);
});
