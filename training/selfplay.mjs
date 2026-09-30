// One actor per process; hypothetical search states receive independent teacher labels, never the actual game's result.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { dirname, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { parseArgs } from 'node:util';
import { execFileSync } from 'node:child_process';
import { createHash } from 'node:crypto';
import { analysisWorker, analysisAssembly } from '../scripts/analysis-worker.mjs';
import { positionKey } from '../scripts/diagnose-nnue.mjs';

const { values } = parseArgs({ options: {
    reference: { type: 'string' }, model: { type: 'string' }, exclusions: { type: 'string' }, output: { type: 'string' },
    games: { type: 'string', default: '250' }, seed: { type: 'string', default: '64101' },
    'actor-nodes': { type: 'string', default: '5000' }, 'teacher-nodes': { type: 'string', default: '200000' },
    samples: { type: 'string', default: '2' },
} });
assert.ok(values.reference && values.model && values.exclusions && values.output);
const games = Number(values.games), seed = Number(values.seed), actorNodes = Number(values['actor-nodes']);
const teacherNodes = Number(values['teacher-nodes']), sampleCount = Number(values.samples);
assert.ok(Number.isInteger(games) && games > 0 && Number.isInteger(seed) && seed >= 0 && seed <= 0xffffffff);
assert.ok(Number.isInteger(actorNodes) && actorNodes >= 500 && Number.isInteger(teacherNodes) && teacherNodes >= 500);
assert.ok(Number.isInteger(sampleCount) && sampleCount >= 0 && sampleCount <= 32);
const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const modelSha = sha(fs.readFileSync(values.model));
const assemblySha = sha(fs.readFileSync(analysisAssembly));
const sourceSha = sha(fs.readFileSync(new URL('./selfplay.mjs', import.meta.url)));
const parent = JSON.parse(fs.readFileSync(resolve(dirname(values.model), 'manifest.json')));
const holdout = JSON.parse(fs.readFileSync(new URL('./holdout-records.json', import.meta.url)));
const exclusions = JSON.parse(fs.readFileSync(values.exclusions));
const referenceCommit = execFileSync('git', ['-C', values.reference, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
assert.equal(referenceCommit, parent.teacher_commit); assert.equal(referenceCommit, holdout.referenceCommit);
execFileSync('git', ['-C', values.reference, 'diff', '--exit-code', 'HEAD', '--', 'src/game']);
const source = path => import(pathToFileURL(resolve(values.reference, path)).href);
const { parseGameRecord, serializeMove } = await source('src/game/moveNotation.ts');
const { enumerateLegalMoves } = await source('src/game/legalMoves.ts');
const { simulateLegalMove } = await source('src/game/simulation.ts');
const { searchMasterTopMoves } = await source('src/game/engineSearch.ts');
const { SHAPE_IDS } = await source('src/game/types.ts');
const { OPENINGS } = await source('supabase/functions/_shared/openings.ts');
const token = move => serializeMove({ shapeId: move.shapeId, column: move.column, ...move.orientation });
const key = position => positionKey(position, SHAPE_IDS);
const reserved = new Set(holdout.records.map(record => {
    const parsed = parseGameRecord(record); assert.equal(parsed.ok, true); return key(parsed.state);
}));
const excluded = new Set([...reserved, ...exclusions.excludedKeys]);
const allowed = new Set(exclusions.trainingOpeningKeys);
assert.ok([...allowed].every(item => parent.partitions.train.opening_keys.includes(item)));
const openings = ['', ...OPENINGS.map(item => item.notation)].filter(record => allowed.has(key(parseGameRecord(record).state)));
assert.ok(openings.length > 0);
function snapshot(position) {
    return { board: position.board.flat().map(cell => cell ? cell.player === 'blue' ? 'B' : 'W' : '.').join(''),
        inventories: Object.fromEntries(['blue', 'white'].map(player => [player, SHAPE_IDS.map(shape => position.inventories[player][shape])])),
        active_player: position.activePlayer };
}
const cache = new Map();
function analyse(position) {
    const identity = JSON.stringify(snapshot(position)); // Keep absolute orientation for cached move tokens.
    if (!cache.has(identity)) {
        const decision = searchMasterTopMoves(position, { maxNodes: teacherNodes });
        assert.ok(decision && decision.depth >= 1);
        cache.set(identity, { score: decision.score, exact: decision.exact, depth: decision.depth,
            nodes: decision.nodes, moves: decision.moves.map(token) });
    }
    return cache.get(identity);
}
let randomState = seed >>> 0;
const random = () => ((randomState = (Math.imul(randomState, 1664525) + 1013904223) >>> 0) / 4294967296);
fs.mkdirSync(dirname(values.output), { recursive: true });
const output = fs.openSync(values.output, 'wx');
const learner = analysisWorker(values.model), classical = analysisWorker();
const started = performance.now();
let samples = 0, roots = 0, tree = 0, completedGames = 0, excludedSamples = 0;
try {
    for (let game = 0; game < games; game++) {
        let record = [random() < 0.5 ? 'w' : '', openings[Math.floor(random() * openings.length)]].filter(Boolean).join(' ');
        let parsed = parseGameRecord(record); assert.equal(parsed.ok, true);
        const openingKey = key(parsed.state);
        // Vary the actual start while retaining the frozen family's train/validation assignment.
        const prefix = Math.floor(random() * 4);
        for (let i = 0; i < prefix; i++) {
            const legal = enumerateLegalMoves(parsed.state.board, parsed.state.inventories[parsed.state.activePlayer]);
            record += (record ? ' ' : '') + token(legal[Math.floor(random() * legal.length)]);
            parsed = parseGameRecord(record); assert.equal(parsed.ok, true);
        }
        let position = parsed.state, result = null;
        const learnerColor = random() < 0.5 ? 'blue' : 'white';
        const opponent = game % 4 === 0 ? 'teacher' : game % 4 === 1 ? 'classical' : 'self';
        const rows = new Map();
        function collect(state, sampleRecord, kind, actor) {
            const position_key = key(state);
            if (excluded.has(position_key)) { excludedSamples++; return; }
            if (kind === 'tree' && rows.has(position_key)) return;
            const decision = analyse(state);
            rows.set(position_key, { ...snapshot(state), record: sampleRecord,
                game_id: `loop-${modelSha.slice(0, 12)}-${seed}-${game}`, opening_key: openingKey, position_key,
                score: decision.score, exact: decision.exact, depth: decision.depth, nodes: decision.nodes,
                source: kind, actor, opponent, winner: null, outcome: null });
        }
        for (let ply = 0; ply < 28 && !result; ply++) {
            const actor = opponent === 'self' || position.activePlayer === learnerColor ? 'learner' : opponent;
            collect(position, record, 'root', actor);
            const legal = enumerateLegalMoves(position.board, position.inventories[position.activePlayer]);
            let move;
            if (actor === 'teacher') move = analyse(position).moves[0];
            else {
                const worker = actor === 'classical' ? classical : learner;
                const decision = await worker.analyze({ record, maxNodes: actorNodes, budgetMs: 30000,
                    sampleCount, sampleSeed: (seed ^ Math.imul(game + 1, 7919) ^ ply) | 0 });
                for (const sample of decision.samples ?? []) {
                    const parsedSample = parseGameRecord(sample); assert.equal(parsedSample.ok, true);
                    assert.equal(parsedSample.state.result, null);
                    collect(parsedSample.state, sample, 'tree', actor);
                }
                move = decision.move;
                if (actor === 'learner' && random() < 0.1) move = token(legal[Math.floor(random() * legal.length)]);
            }
            const chosen = legal.find(item => token(item) === move); assert.ok(chosen, move);
            const mover = position.activePlayer;
            const next = simulateLegalMove(position, chosen);
            record = [record, move].filter(Boolean).join(' ');
            if (!next.result && next.position.activePlayer === mover) record += ' --';
            position = next.position; result = next.result;
        }
        assert.ok(result, 'A generated game must finish.');
        for (const row of rows.values()) {
            if (row.source === 'root') {
                row.winner = result.winner;
                row.outcome = result.winner === null ? 0 : result.winner === row.active_player ? 1 : -1;
                roots++;
            } else tree++;
            fs.writeSync(output, JSON.stringify(row) + '\n'); samples++;
        }
        completedGames++;
        if (completedGames % 10 === 0 || completedGames === games)
            console.error(JSON.stringify({ seed, completedGames, samples, roots, tree, seconds: Math.round((performance.now() - started) / 1000) }));
    }
} finally { learner.stop(); classical.stop(); fs.closeSync(output); }
fs.writeFileSync(values.output + '.meta.json', JSON.stringify({ schema: 2, referenceCommit, seed, maxNodes: teacherNodes,
    actorNodes, sampleCount, completedGames, samples, roots, tree, excludedSamples, shapes: SHAPE_IDS,
    reservedKeys: [...reserved], trainingOnly: true, excludedTrainingKeysSha256: sha(fs.readFileSync(values.exclusions)),
    parentModelSha256: modelSha, assemblySha256: assemblySha, sourceSha256: sourceSha,
    elapsedSeconds: (performance.now() - started) / 1000, nodeVersion: process.version,
    collection: '50% learner self-play, 25% vs teacher, 25% vs classical; 10% learner exploration; 0..3 random prefix moves; separately relabelled reservoir samples; training families only',
}, null, 2) + '\n', { flag: 'wx' });
