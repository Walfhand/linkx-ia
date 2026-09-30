import assert from 'node:assert/strict';
import { test } from 'node:test';
import { readMatchReport, evaluationProtocol } from './evaluate-loop.mjs';

test('confirmation fixes one candidate and supports 1024 games without changing the default gate', () => {
    assert.deepEqual(evaluationProtocol({ pairs: '512', confirmation: true }, ['candidate']), { pairs: 512, confirmation: true });
    assert.deepEqual(evaluationProtocol({}, ['a', 'b']), { pairs: 64, confirmation: false });
    assert.throws(() => evaluationProtocol({ pairs: '63' }, ['a']));
    assert.throws(() => evaluationProtocol({ pairs: '64.5' }, ['a']));
    assert.throws(() => evaluationProtocol({ confirmation: true }, ['a', 'b']));
});

test('evaluation requires a completed report with matching model identities', () => {
    const header = { type: 'context', modelSha256: 'candidate', opponentModelSha256: 'parent' };
    const game = { type: 'game', opening: 1, color: 'blue', result: 1, turns: [] };
    const finish = { type: 'summary', games: 1, score: 1 };
    const lines = rows => rows.map(JSON.stringify).join('\n') + '\n';
    assert.equal(readMatchReport(lines([header, game, finish]), 'candidate', 'parent').summary.score, 1);
    assert.throws(() => readMatchReport(lines([header, game]), 'candidate', 'parent'));
    assert.throws(() => readMatchReport(lines([header, game, finish]), 'different', 'parent'));
    assert.throws(() => readMatchReport(lines([header, game, finish]), 'candidate', null));
    assert.throws(() => readMatchReport(lines([header, game, { ...finish, score: 0.5 }]), 'candidate', 'parent'));
    assert.throws(() => readMatchReport(lines([{ ...header, budgetMs: 100 }, game, finish]), 'candidate', 'parent', { budgetMs: 1000 }));
});
