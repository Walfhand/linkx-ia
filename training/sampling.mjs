import assert from 'node:assert/strict';

// Preserve broad coverage alongside hard examples. This RNG is independent of the actors' move exploration.
export function selectDisagreements(pool, count, seed) {
    assert.ok(Number.isInteger(count) && count >= 0 && count <= 32);
    assert.equal(new Set(pool.map(item => item.record)).size, pool.length);
    for (const item of pool)
        assert.ok(Number.isFinite(item.learnerValue) && Math.abs(item.learnerValue) <= 1.00001
            && Number.isFinite(item.screenValue) && Math.abs(item.screenValue) <= 1.00001);
    if (count === 0 || pool.length === 0) return [];
    const ranked = pool.map(item => ({ ...item, disagreement: Math.abs(item.learnerValue - item.screenValue) }))
        .sort((a, b) => b.disagreement - a.disagreement || a.record.localeCompare(b.record));
    const selected = [];
    if (count > 1) {
        // Modulo after unsigned conversion, including negative seeds.
        const index = ((Math.imul(seed, 1664525) + 1013904223) >>> 0) % pool.length;
        selected.push({ ...pool[index], disagreement: Math.abs(pool[index].learnerValue - pool[index].screenValue), selection: 'uniform' });
    }
    for (const item of ranked) {
        if (selected.length >= count) break;
        if (!selected.some(earlier => earlier.record === item.record)) selected.push({ ...item, selection: 'disagreement' });
    }
    return selected;
}
