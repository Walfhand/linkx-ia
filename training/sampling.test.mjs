import assert from 'node:assert/strict';
import { test } from 'node:test';
import { selectDisagreements } from './sampling.mjs';

test('sampling combines diverse positions and strong disagreements without changing the input', () => {
    const pool = [
        { record: 'a', learnerValue: 0.9, screenValue: -0.9 },
        { record: 'b', learnerValue: 0.2, screenValue: 0.3 },
        { record: 'c', learnerValue: -0.3, screenValue: -0.31 },
        { record: 'd', learnerValue: 0, screenValue: 0.8 },
    ];
    const original = structuredClone(pool), seen = new Set();
    for (let seed = 0; seed < 30; seed++) {
        const selected = selectDisagreements(pool, 2, seed);
        assert.equal(selected.length, 2);
        assert.equal(new Set(selected.map(item => item.record)).size, 2);
        assert.ok(selected.some(item => item.record === 'a'));
        assert.deepEqual(selected, selectDisagreements(pool, 2, seed));
        assert.equal(selected.filter(item => item.selection === 'uniform').length, 1);
        assert.equal(selected.filter(item => item.selection === 'disagreement').length, 1);
        for (const item of selected) { assert.equal(item.disagreement, Math.abs(item.learnerValue - item.screenValue)); seen.add(item.record); }
    }
    assert.equal(seen.size, pool.length);
    assert.deepEqual(pool, original);
    assert.deepEqual(selectDisagreements(pool, 0, 42), []);
    assert.throws(() => selectDisagreements([{ record: 'a', learnerValue: NaN, screenValue: 0 }], 1, 42));
});
