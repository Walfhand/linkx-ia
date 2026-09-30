import assert from 'node:assert/strict';
import { test } from 'node:test';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import fs from 'node:fs';
import { positionKey, playerValue, assessMove, summarize, playGame, selectOpenings, descendantExclusion } from './diagnose-nnue.mjs';
import { analysisWorker, analysisAssembly } from './analysis-worker.mjs';

test('native worker preserves queued response order and rejects protocol errors', { skip: !fs.existsSync(analysisAssembly) }, async () => {
    const worker = analysisWorker('models/nnue-v1/h512/model.nnue');
    try {
        const requests = [{ record: '15', evaluateOnly: true }, { record: 'w 15', evaluateOnly: true }];
        const values = await Promise.all(requests.map(request => worker.analyze(request)));
        assert.equal(values[0].value, values[1].value);
        await assert.rejects(worker.analyze({ record: 'invalid', evaluateOnly: true }));
        assert.ok(Number.isFinite((await worker.analyze(requests[0])).value));
    } finally { worker.stop(); }
    await assert.rejects(worker.analyze({ record: '' }));
});

test('diagnostic values preserve forced passes and distinguish terminal outcomes from estimates', () => {
    assert.equal(playerValue({ score: 4000, exact: false }, 'blue', 'blue'), Math.tanh(1));
    assert.equal(playerValue({ score: 4000, exact: false }, 'white', 'blue'), -Math.tanh(1));
    assert.equal(playerValue({ score: -999990, exact: true }, 'white', 'blue'), 1);
    assert.equal(playerValue({ score: 0, exact: true }, 'blue', 'white'), 0);
});

test('paired summaries count both colors and report the two engines separately', () => {
    const games = [
        { result: 1, turns: [{ engine: 'nnue', nodes: 100, depth: 2, elapsedMs: 10, exact: false }] },
        { result: 0, turns: [{ engine: 'teacher', nodes: 2000, depth: 5, elapsedMs: 20, exact: true }] },
        { result: 0.5, turns: [] },
    ];
    const summary = summarize(games);
    assert.equal(summary.wins, 1); assert.equal(summary.losses, 1); assert.equal(summary.draws, 1);
    assert.equal(summary.score, 0.5);
    assert.equal(summary.search.nnue.meanDepth, 2);
    assert.equal(summary.search.teacher.nodesPerSecond, 100000);
    assert.equal(summary.search.teacher.proven, 1);
});

test('a proven blunder requires a proven non-losing alternative, including after a forced pass', () => {
    const root = { score: 999999, exact: true };
    assert.equal(assessMove(root, { score: 999995, exact: true }, 'blue', 'white').provenBlunder, true);
    assert.equal(assessMove(root, { score: -999995, exact: true }, 'blue', 'blue').provenBlunder, true);
    assert.equal(assessMove({ score: 4000, exact: false }, { score: 999995, exact: true }, 'blue', 'white').provenBlunder, false);
    assert.equal(assessMove({ score: -999999, exact: true }, { score: 999995, exact: true }, 'blue', 'white').provenBlunder, false);
});

test('promotion starts exclude any possible known descendant, including mirrors and swapped colors', () => {
    const stateKey = (cells, reserves = '22222222222222') => {
        const board = Array(81).fill('.'); for (const [index, color] of cells) board[index] = color;
        return board.join('') + '|' + reserves;
    };
    const known = stateKey([[72, '1'], [73, '1'], [80, '2']], '02222221222222');
    const excluded = descendantExclusion(new Set([known]));
    assert.equal(excluded(stateKey([[72, '1']], '12222222222222')), true);
    assert.equal(excluded(stateKey([[80, '1']], '12222222222222')), true);
    assert.equal(excluded(stateKey([[72, '2']], '22222221222222')), true);
    assert.equal(excluded(stateKey([[75, '1']], '12222222222222')), false);
    assert.equal(excluded(stateKey([[72, '1']], '10222222222222')), false); // A used domino cannot reappear.
});

test('new openings exclude the training positions and families, and game traces replay legally', { skip: !process.env.LINKX_REFERENCE }, async () => {
    assert.ok(process.env.LINKX_REFERENCE);
    const source = file => import(pathToFileURL(resolve(process.env.LINKX_REFERENCE, file)).href);
    const { parseGameRecord, serializeMove, serializeGameRecord } = await source('src/game/moveNotation.ts');
    const { enumerateLegalMoves } = await source('src/game/legalMoves.ts');
    const { searchMasterTopMoves } = await source('src/game/engineSearch.ts');
    const { SHAPE_IDS } = await source('src/game/types.ts');
    const api = { parseGameRecord, serializeMove, serializeGameRecord, enumerateLegalMoves, searchMasterTopMoves, shapes: SHAPE_IDS };
    const snapshot = JSON.parse(fs.readFileSync('tests/LinkxAi.Tests.Unit/ReferenceFixtures/rules.json')).cases.find(item => item.record === '');
    const empty = parseGameRecord('').state;
    assert.equal(positionKey(empty, SHAPE_IDS), '.'.repeat(81) + '|22222222222222');
    const token = move => serializeMove({ shapeId: move.shapeId, column: move.column, ...move.orientation });
    const first = token(enumerateLegalMoves(empty.board, empty.inventories.blue)[0]);
    const families = new Set([positionKey(parseGameRecord(first).state, SHAPE_IDS)]);
    const known = new Set([positionKey(empty, SHAPE_IDS)]);
    const openings = selectOpenings(api, families, known, 3, 813, 2000);
    assert.deepEqual(openings, selectOpenings(api, families, known, 3, 813, 2000));
    assert.equal(new Set(openings.map(item => item.familyKey)).size, 3);
    for (const opening of openings) {
        assert.ok(!families.has(opening.familyKey)); assert.ok(!known.has(opening.positionKey));
        const swapped = parseGameRecord('w ' + opening.record).state;
        assert.equal(positionKey(swapped, SHAPE_IDS), opening.positionKey);
    }
    const allFamilies = new Set(enumerateLegalMoves(empty.board, empty.inventories.blue)
        .map(move => positionKey(parseGameRecord(token(move)).state, SHAPE_IDS)));
    const onlyFamily = [...allFamilies][0]; allFamilies.delete(onlyFamily);
    const shared = selectOpenings(api, allFamilies, known, 3, 912, 2000, true);
    assert.equal(shared.length, 3);
    assert.equal(new Set(shared.map(item => item.familyKey)).size, 1);
    assert.equal(new Set(shared.map(item => item.positionKey)).size, 3);
    assert.equal(snapshot.legalMoves.length, 95);
    const firstLegal = async (record, state) => ({ move: token(enumerateLegalMoves(state.board, state.inventories[state.activePlayer])[0]), score: 0, depth: 0, exact: false, nodes: 0, elapsedMs: 0 });
    const game = await playGame(api, openings[0], 'blue', 100, firstLegal, firstLegal);
    assert.ok(game.turns.length > 0 && game.turns.length <= 28);
    assert.ok(parseGameRecord(game.record).state.result);
    const excludesAncestors = descendantExclusion(new Set([positionKey(parseGameRecord(game.record).state, SHAPE_IDS)]));
    for (const turn of game.turns) {
        assert.ok(parseGameRecord(turn.record).ok);
        assert.ok(parseGameRecord(turn.record + ' ' + turn.move).ok);
        assert.equal(excludesAncestors(positionKey(parseGameRecord(turn.record).state, SHAPE_IDS)), true);
    }
});
