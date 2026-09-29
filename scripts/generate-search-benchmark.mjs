// Exact endgames: exhaustive minimax on Marmelab rules, without any heuristic evaluation.
// Node >=22.18: node scripts/generate-search-benchmark.mjs REF_CHECKOUT CORPUS_JSON > endgames.json
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import assert from 'node:assert/strict';

const root = process.argv[2];
const corpusPath = process.argv[3];
if (!root || !corpusPath) throw new Error('Provide the reference checkout and generated rules corpus.');
const source = file => import(pathToFileURL(resolve(root, file)).href);
const { parseGameRecord, serializeMove } = await source('src/game/moveNotation.ts');
const { enumerateLegalMoves } = await source('src/game/legalMoves.ts');
const { simulateLegalMove } = await source('src/game/simulation.ts');
const { searchMasterTopMoves } = await source('src/game/engineSearch.ts');
const corpus = JSON.parse(fs.readFileSync(corpusPath));
const referenceCommit = execFileSync('git', ['-C', root, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
assert.equal(corpus.commit, referenceCommit, 'Rules corpus and reference checkout must use the same commit.');
const token = move => serializeMove({ shapeId: move.shapeId, column: move.column, ...move.orientation });
const key = position => JSON.stringify([
    position.board.flat().map(cell => cell?.player ?? ''), position.inventories, position.activePlayer,
]);
const remaining = item => [...item.inventories.blue, ...item.inventories.white].reduce((a, b) => a + b, 0);
const candidates = [...new Map(corpus.cases.filter(item => !item.error && !item.result && remaining(item) <= 10)
    .map(item => [item.canonical, item])).values()].sort((a, b) => remaining(a) - remaining(b));
const cases = [];
let skipped = 0;
for (const candidate of candidates) {
    const parsed = parseGameRecord(candidate.canonical);
    assert.equal(parsed.ok, true);
    const rootPlayer = parsed.state.activePlayer;
    let nodes = 0;
    const cache = new Map();
    const terminal = result => result.winner === null ? 0 : result.winner === rootPlayer ? 1 : -1;
    function solve(position) {
        if (++nodes > 100_000) throw new Error('node-limit');
        const identity = key(position);
        if (cache.has(identity)) return cache.get(identity);
        const scores = enumerateLegalMoves(position.board, position.inventories[position.activePlayer]).map(move => {
            const next = simulateLegalMove(position, move);
            return next.result ? terminal(next.result) : solve(next.position);
        });
        assert.ok(scores.length > 0);
        const value = position.activePlayer === rootPlayer ? Math.max(...scores) : Math.min(...scores);
        cache.set(identity, value);
        return value;
    }
    try {
        const moves = enumerateLegalMoves(parsed.state.board, parsed.state.inventories[rootPlayer]);
        const outcomes = moves.map(move => {
            const next = simulateLegalMove(parsed.state, move);
            return { move: token(move), value: next.result ? terminal(next.result) : solve(next.position) };
        });
        const value = Math.max(...outcomes.map(item => item.value));
        const optimalMoves = outcomes.filter(item => item.value === value).map(item => item.move);
        const teacher = searchMasterTopMoves(parsed.state, { maxNodes: 30_000 });
        assert.ok(teacher);
        cases.push({
            record: candidate.canonical, value, optimalMoves, outcomes, nodes,
            reference: { move: token(teacher.moves[0]), score: teacher.score, depth: teacher.depth, exact: teacher.exact,
                correct: optimalMoves.includes(token(teacher.moves[0])), correctValue: Math.sign(teacher.score) === value },
        });
    } catch (error) {
        if (error.message !== 'node-limit') throw error;
        skipped++;
    }
}
assert.ok(cases.length >= 10, 'Need enough fully solved positions to form a benchmark.');
process.stdout.write(JSON.stringify({
    referenceCommit,
    method: 'exhaustive minimax; terminal outcomes only; incomplete cases discarded',
    skipped, cases,
}, null, 2) + '\n');
console.error(`Solved ${cases.length} positions; discarded ${skipped} incomplete positions.`);
