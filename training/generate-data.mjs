// Teacher self-play generation. Each process owns the reference engine's mutable search buffers.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { dirname, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';
import { parseArgs } from 'node:util';

const { values } = parseArgs({ options: {
    reference: { type: 'string' }, output: { type: 'string' },
    games: { type: 'string', default: '128' }, nodes: { type: 'string', default: '50000' },
    seed: { type: 'string', default: '43101' },
} });
assert.ok(values.reference && values.output, 'Provide --reference and --output.');
const games = Number(values.games), maxNodes = Number(values.nodes), seed = Number(values.seed);
assert.ok(Number.isInteger(games) && games > 0);
assert.ok(Number.isInteger(maxNodes) && maxNodes >= 500);
assert.ok(Number.isInteger(seed) && seed >= 0 && seed <= 0xffffffff);
const source = file => import(pathToFileURL(resolve(values.reference, file)).href);
const { parseGameRecord, serializeMove } = await source('src/game/moveNotation.ts');
const { enumerateLegalMoves } = await source('src/game/legalMoves.ts');
const { simulateLegalMove } = await source('src/game/simulation.ts');
const { searchMasterTopMoves } = await source('src/game/engineSearch.ts');
const { SHAPE_IDS } = await source('src/game/types.ts');
const { OPENINGS } = await source('supabase/functions/_shared/openings.ts');
const holdout = JSON.parse(fs.readFileSync(new URL('./holdout-records.json', import.meta.url)));
const referenceCommit = execFileSync('git', ['-C', values.reference, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
assert.equal(referenceCommit, holdout.referenceCommit, 'The teacher must match the validated commit.');
execFileSync('git', ['-C', values.reference, 'diff', '--exit-code', 'HEAD', '--', 'src/game'], { stdio: 'pipe' });

let state = seed >>> 0;
const random = () => ((state = (Math.imul(state, 1664525) + 1013904223) >>> 0) / 4294967296);
const token = move => serializeMove({ shapeId: move.shapeId, column: move.column, ...move.orientation });
function snapshot(position) {
    return {
        board: position.board.flat().map(cell => cell ? (cell.player === 'blue' ? 'B' : 'W') : '.').join(''),
        inventories: Object.fromEntries(['blue', 'white'].map(player => [player, SHAPE_IDS.map(shape => position.inventories[player][shape])])),
        active_player: position.activePlayer,
    };
}
function canonicalKey(row) {
    const own = row.active_player === 'blue' ? 'B' : 'W';
    const board = [...row.board].map(cell => cell === '.' ? '.' : cell === own ? '1' : '2').join('');
    const mirror = Array.from({ length: 9 }, (_, y) => [...board.slice(y * 9, y * 9 + 9)].reverse().join('')).join('');
    const other = row.active_player === 'blue' ? 'white' : 'blue';
    return [board, mirror].sort()[0] + '|' + [...row.inventories[row.active_player], ...row.inventories[other]].join('');
}
const reserved = new Set(holdout.records.map(record => {
    const parsed = parseGameRecord(record);
    assert.equal(parsed.ok, true);
    return canonicalKey(snapshot(parsed.state));
}));
const output = resolve(values.output);
fs.mkdirSync(dirname(output), { recursive: true });
const file = fs.openSync(output, 'wx');
const cache = new Map();
const started = performance.now();
let samples = 0, skippedReserved = 0, completedGames = 0;
try {
    const openings = ['', ...OPENINGS.map(opening => opening.notation)];
    for (let game = 0; game < games; game++) {
        let record = [game % 2 ? 'w' : '', openings[game % openings.length]].filter(Boolean).join(' ');
        const parsed = parseGameRecord(record);
        assert.equal(parsed.ok, true);
        let position = parsed.state;
        let result = null;
        const openingKey = canonicalKey(snapshot(position));
        const rows = [];
        for (let ply = 0; ply < 28 && !result; ply++) {
            const row = snapshot(position);
            const positionKey = canonicalKey(row);
            const cacheKey = JSON.stringify(row);
            let decision = cache.get(cacheKey);
            if (!decision) {
                const search = searchMasterTopMoves(position, { maxNodes });
                assert.ok(search && search.depth >= 1);
                decision = { score: search.score, depth: search.depth, nodes: search.nodes, exact: search.exact, moves: search.moves.map(token) };
                cache.set(cacheKey, decision);
            }
            if (!reserved.has(positionKey)) rows.push({
                ...row, game_id: `${seed}-${game}`, opening_key: openingKey, position_key: positionKey, record,
                score: decision.score, exact: decision.exact, depth: decision.depth, nodes: decision.nodes,
            });
            else skippedReserved++;
            const legal = enumerateLegalMoves(position.board, position.inventories[position.activePlayer]);
            // Deliberate exploration broadens the corpus; every sampled position is still labeled by the teacher.
            const explore = random() < 0.2;
            const chosenToken = decision.moves[Math.floor(random() * decision.moves.length)];
            const chosen = explore ? legal[Math.floor(random() * legal.length)]
                : legal.find(move => token(move) === chosenToken);
            assert.ok(chosen);
            const mover = position.activePlayer;
            const next = simulateLegalMove(position, chosen);
            record = [record, token(chosen)].filter(Boolean).join(' ');
            if (!next.result && next.position.activePlayer === mover) record += ' --';
            position = next.position;
            result = next.result;
        }
        assert.ok(result, 'A generated game must finish within 28 placements.');
        for (const row of rows) {
            row.winner = result.winner;
            row.outcome = result.winner === null ? 0 : result.winner === row.active_player ? 1 : -1;
            fs.writeSync(file, JSON.stringify(row) + '\n');
        }
        samples += rows.length;
        completedGames++;
        if (completedGames % 16 === 0 || completedGames === games)
            console.error(JSON.stringify({ seed, completedGames, samples, seconds: Math.round((performance.now() - started) / 1000) }));
    }
} finally { fs.closeSync(file); }
fs.writeFileSync(output + '.meta.json', JSON.stringify({
    schema: 1, referenceCommit, seed, maxNodes, completedGames, samples, skippedReserved,
    shapes: SHAPE_IDS, reservedKeys: [...reserved], exploration: 0.2,
    elapsedSeconds: (performance.now() - started) / 1000, nodeVersion: process.version,
}, null, 2) + '\n', { flag: 'wx' });
