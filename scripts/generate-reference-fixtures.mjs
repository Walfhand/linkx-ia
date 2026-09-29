// Node >= 22.18. Run: node scripts/generate-reference-fixtures.mjs /path/to/marmelab-linkx [games]
// Uses the reference implementation as an oracle; it never calls the C# implementation.
import { execFileSync } from 'node:child_process';
import assert from 'node:assert/strict';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const root = resolve(process.argv[2] ?? '');
const games = Number(process.argv[3] ?? 20);
if (!process.argv[2] || !Number.isInteger(games) || games < 1)
    throw new Error('Provide a reference checkout path and a positive game count.');

const source = async (file) => import(pathToFileURL(resolve(root, file)).href);
const { parseGameRecord, serializeGameRecord, serializeMove } = await source('src/game/moveNotation.ts');
const { enumerateLegalMoves } = await source('src/game/legalMoves.ts');
const { SHAPE_IDS } = await source('src/game/types.ts');
const { OPENINGS } = await source('supabase/functions/_shared/openings.ts');

let seed = 20260929;
function randomIndex(length) {
    seed = (Math.imul(seed, 1664525) + 1013904223) >>> 0;
    return Math.floor(seed / 4294967296 * length);
}

const cases = new Map();
function token(move) {
    return serializeMove({ shapeId: move.shapeId, column: move.column, ...move.orientation });
}

function snapshot(record) {
    if (cases.has(record)) return cases.get(record);
    const parsed = parseGameRecord(record);
    if (!parsed.ok) {
        const item = { record, error: { reason: parsed.error.reason, moveNumber: parsed.error.index + 1, token: parsed.error.token } };
        cases.set(record, item);
        return item;
    }
    const state = parsed.state;
    const item = {
        record,
        canonical: serializeGameRecord(state),
        board: state.board.flat().map(cell => cell ? (cell.player === 'blue' ? 'B' : 'W') : '.').join(''),
        activePlayer: state.activePlayer,
        firstPlayer: state.firstPlayer,
        inventories: Object.fromEntries(['blue', 'white'].map(player => [player, SHAPE_IDS.map(shape => state.inventories[player][shape])])),
        result: state.result,
        legalMoves: state.result ? [] : enumerateLegalMoves(state.board, state.inventories[state.activePlayer]).map(token).sort(),
    };
    cases.set(record, item);
    return item;
}

for (const initial of ['', 'w', ...OPENINGS.map(opening => opening.notation)]) snapshot(initial);
assert.equal(snapshot('').legalMoves.length, 95);
for (const shape of ['1', '2', '3I', '3L', '4S', '4T', '4L'])
    for (const mirror of ['', 's'])
        for (const rotation of ['', 'r1', 'r2', 'r3', 'l1', 'l2', 'l3'])
            for (const column of [1, 5, 9]) snapshot(`${shape}${mirror}${rotation}${column}`);
for (let game = 0; game < games; game++) {
    let record = game % 2 === 0 ? '' : 'w';
    for (let ply = 0; ply <= 28; ply++) {
        const item = snapshot(record);
        if (item.error) throw new Error('The oracle generated an illegal game.');
        if (item.result) break;
        if (ply === 28 || item.legalMoves.length === 0) throw new Error('The oracle did not resolve the game.');
        record = [item.canonical, item.legalMoves[randomIndex(item.legalMoves.length)]].filter(Boolean).join(' ');
    }
}

// Exercise accepted spelling variants and error handling at every sampled position.
for (const item of [...cases.values()]) {
    snapshot(item.record.toLowerCase().replaceAll(' ', '+'));
    if (item.record.includes('--')) snapshot(item.record.replaceAll(' --', ''));
    if (item.record.startsWith('w ')) snapshot(item.record.replace(/^w /, 'WHITE,'));
    for (const suffix of ['--', '3I9', '4T1', '11', '4Z3'])
        snapshot([item.record, suffix].filter(Boolean).join(' '));
}

const corpus = {
    source: 'https://github.com/marmelab/linkx',
    commit: execFileSync('git', ['-C', root, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim(),
    seed: 20260929,
    games,
    shapes: SHAPE_IDS,
    cases: [...cases.values()],
};
process.stdout.write(JSON.stringify(corpus, null, 2) + '\n');
