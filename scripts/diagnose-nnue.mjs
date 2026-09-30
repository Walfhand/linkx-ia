// Frozen, recorded diagnostic. Run --mode prepare before any matches; JSONL preserves completed games.
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { resolve, dirname } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { cpus } from 'node:os';
import { parseArgs } from 'node:util';
import { analysisWorker, analysisAssembly } from './analysis-worker.mjs';

const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const token = (api, move) => api.serializeMove({ shapeId: move.shapeId, column: move.column, ...move.orientation });

export function positionKey(position, shapes) {
    const own = position.activePlayer;
    const board = position.board.flat().map(cell => !cell ? '.' : cell.player === own ? '1' : '2').join('');
    const mirror = Array.from({ length: 9 }, (_, y) => [...board.slice(y * 9, y * 9 + 9)].reverse().join('')).join('');
    const other = own === 'blue' ? 'white' : 'blue';
    return [board, mirror].sort()[0] + '|' + [own, other].flatMap(player => shapes.map(shape => position.inventories[player][shape])).join('');
}

export function descendantExclusion(keys) {
    const bits = Array.from({ length: 81 }, (_, index) => 1n << BigInt(index));
    function packed(key, mirror = false) {
        let own = 0n, other = 0n;
        for (let index = 0; index < 81; index++) {
            const at = mirror ? Math.floor(index / 9) * 9 + 8 - index % 9 : index;
            if (key[index] === '1') own |= bits[at];
            if (key[index] === '2') other |= bits[at];
        }
        return { own, other, reserves: [...key.slice(82)].map(Number) };
    }
    const known = [...keys].map(key => packed(key));
    return key => {
        for (const root of [packed(key), packed(key, true)]) {
            for (const swap of [false, true]) {
                const own = swap ? root.other : root.own, other = swap ? root.own : root.other;
                for (const state of known) {
                    if ((state.own & own) !== own || (state.other & other) !== other) continue;
                    if (state.reserves.every((count, index) => count <= root.reserves[swap ? (index + 7) % 14 : index])) return true;
                }
            }
        }
        return false;
    };
}

export function playerValue(decision, movingPlayer, player) {
    const value = decision.exact ? Math.sign(decision.score) : Math.tanh(decision.score / 4000);
    return value === 0 ? 0 : movingPlayer === player ? value : -value;
}

export function assessMove(before, after, player, nextPlayer) {
    const beforeValue = playerValue(before, player, player);
    const afterValue = playerValue(after, nextPlayer, player);
    return { beforeValue, afterValue, estimatedDrop: beforeValue - afterValue,
        provenBlunder: before.exact && after.exact && beforeValue >= 0 && afterValue < 0 };
}

export function summarize(games) {
    const search = {};
    for (const engine of ['nnue', 'teacher']) {
        const turns = games.flatMap(game => game.turns).filter(turn => turn.engine === engine);
        const ms = turns.map(turn => turn.elapsedMs).sort((a, b) => a - b);
        const nodes = turns.reduce((sum, turn) => sum + turn.nodes, 0);
        const elapsed = ms.reduce((sum, value) => sum + value, 0);
        search[engine] = { turns: turns.length, meanDepth: turns.length ? turns.reduce((sum, turn) => sum + turn.depth, 0) / turns.length : 0,
            nodes, nodesPerSecond: elapsed ? nodes * 1000 / elapsed : 0, proven: turns.filter(turn => turn.exact).length,
            maximumMs: ms.at(-1) ?? 0, p95Ms: ms[Math.floor((ms.length - 1) * 0.95)] ?? 0 };
    }
    return { games: games.length, wins: games.filter(game => game.result === 1).length,
        draws: games.filter(game => game.result === 0.5).length, losses: games.filter(game => game.result === 0).length,
        score: games.length ? games.reduce((sum, game) => sum + game.result, 0) / games.length : 0, search };
}

export function selectOpenings(api, families, known, count, seed, maxNodes = 50000, allowSharedFamilies = false,
    plies = 3, forbiddenDescendant = () => false) {
    let randomState = seed >>> 0;
    const random = () => ((randomState = (Math.imul(randomState, 1664525) + 1013904223) >>> 0) / 4294967296);
    const empty = api.parseGameRecord('').state;
    const firstMoves = api.enumerateLegalMoves(empty.board, empty.inventories.blue).map(move => token(api, move));
    for (let i = firstMoves.length - 1; i > 0; i--) {
        const j = Math.floor(random() * (i + 1));
        [firstMoves[i], firstMoves[j]] = [firstMoves[j], firstMoves[i]];
    }
    const selected = [], usedFamilies = new Set(families), usedKeys = new Set(known);
    for (let round = 0; round < (allowSharedFamilies ? count : 1); round++) {
    for (const first of firstMoves) {
        const initial = api.parseGameRecord(first).state;
        const familyKey = positionKey(initial, api.shapes);
        if (usedFamilies.has(familyKey)) continue;
        for (let attempt = 0; attempt < 50; attempt++) {
            let position = initial;
            for (let ply = 1; ply < plies && !position.result; ply++) {
                const legal = api.enumerateLegalMoves(position.board, position.inventories[position.activePlayer]);
                position = api.parseGameRecord(api.serializeGameRecord(position) + ' ' + token(api, legal[Math.floor(random() * legal.length)])).state;
            }
            const key = positionKey(position, api.shapes);
            if (position.result || usedKeys.has(key) || forbiddenDescendant(key)) continue;
            const teacher = api.searchMasterTopMoves(position, { maxNodes });
            if (teacher.exact) continue; // Avoid starts already resolved by the frozen teacher at generation budget.
            selected.push({ id: selected.length + 1, record: api.serializeGameRecord(position), familyKey, positionKey: key,
                screening: { score: teacher.score, depth: teacher.depth, nodes: teacher.nodes, exact: teacher.exact } });
            if (!allowSharedFamilies) usedFamilies.add(familyKey);
            usedKeys.add(key);
            break;
        }
        if (selected.length === count) return selected;
    }
    }
    throw new Error(`Only found ${selected.length}/${count} independent opening starts.`);
}

export async function playGame(api, opening, color, budgetMs, candidate, teacher) {
    let record = opening.record;
    const turns = [];
    for (let ply = 0; ply <= 28; ply++) {
        const parsed = api.parseGameRecord(record);
        assert.equal(parsed.ok, true, record);
        const state = parsed.state;
        if (state.result) return { type: 'game', opening: opening.id, initialRecord: opening.record, color, budgetMs, record,
            result: state.result.winner === null ? 0.5 : state.result.winner === color ? 1 : 0, ending: state.result, turns };
        assert.ok(ply < 28, 'The game must finish within 28 placements.');
        const engine = state.activePlayer === color ? 'nnue' : 'teacher';
        const decision = await (engine === 'nnue' ? candidate : teacher)(record, state);
        assert.equal(typeof decision.move, 'string');
        assert.ok(Number.isFinite(decision.score) && Number.isFinite(decision.elapsedMs));
        const next = api.parseGameRecord(record + ' ' + decision.move);
        assert.equal(next.ok, true, `${engine} played ${decision.move} on ${record}`);
        turns.push({ ply, record, player: state.activePlayer, engine, ...decision });
        record = api.serializeGameRecord(next.state);
    }
}

async function loadReference(root) {
    const source = file => import(pathToFileURL(resolve(root, file)).href);
    const notation = await source('src/game/moveNotation.ts');
    const { enumerateLegalMoves } = await source('src/game/legalMoves.ts');
    const { searchMasterTopMoves } = await source('src/game/engineSearch.ts');
    const { SHAPE_IDS } = await source('src/game/types.ts');
    return { ...notation, enumerateLegalMoves, searchMasterTopMoves, shapes: SHAPE_IDS };
}

function corpusKeys(manifest) {
    const keys = new Set();
    for (const dataset of manifest.datasets) {
        const data = fs.readFileSync(resolve('training/data', dataset.file));
        assert.equal(sha(data), dataset.sha256, 'Dataset does not match the trained model.');
        for (const line of data.toString().trim().split('\n')) keys.add(JSON.parse(line).position_key);
    }
    return keys;
}

async function analyseGames(api, values, context, write) {
    const games = values.log.flatMap(path => {
        const rows = fs.readFileSync(path, 'utf8').trim().split('\n').map(JSON.parse);
        assert.equal(rows.at(-1).type, 'summary', 'Only analyse completed match campaigns.');
        assert.equal(rows[0].modelSha256, context.modelSha256);
        assert.equal(rows[0].assemblySha256, context.assemblySha256);
        return rows.filter(row => row.type === 'game');
    });
    const cache = new Map();
    function teacher(record, maxNodes = 200000, maxDepth) {
        const state = api.parseGameRecord(record).state;
        const key = api.serializeGameRecord(state) + `|${maxNodes}|${maxDepth}`;
        if (cache.has(key)) return cache.get(key);
        const started = performance.now();
        const result = state.result;
        const raw = result ? { score: result.winner === null ? 0 : result.winner === state.activePlayer ? 1000000 : -1000000,
            depth: 0, exact: true, nodes: 0, moves: [], pv: [] } : api.searchMasterTopMoves(state, { maxNodes, ...(maxDepth ? { maxDepth } : {}) });
        const decision = { score: raw.score, depth: raw.depth, exact: raw.exact, nodes: raw.nodes,
            elapsedMs: performance.now() - started, player: state.activePlayer, terminal: Boolean(result),
            move: raw.moves.length ? token(api, raw.moves[0]) : null, pv: raw.pv.map(move => token(api, move)) };
        cache.set(key, decision);
        return decision;
    }
    const screened = [];
    for (const game of games.filter(game => game.result === 0)) {
        for (const turn of game.turns.filter(turn => turn.engine === 'nnue')) {
            const before = teacher(turn.record);
            const after = teacher(turn.record + ' ' + turn.move);
            const item = { type: 'screening', opening: game.opening, color: game.color, budgetMs: game.budgetMs,
                record: turn.record, move: turn.move, played: turn, before, after,
                ...assessMove(before, after, turn.player, after.player) };
            screened.push(item); write(item);
        }
        console.error(`Screened losing game ${game.budgetMs}ms / opening ${game.opening} / ${game.color}`);
    }
    // Two distinct failures per time budget: bounded counterfactual analysis, selected after exhaustive screening.
    const selected = [], seen = new Set();
    for (const budget of [...new Set(games.map(game => game.budgetMs))].sort((a, b) => a - b)) {
        const ranked = screened.filter(item => item.budgetMs === budget).sort((a, b) =>
            Number(b.provenBlunder) - Number(a.provenBlunder) || b.estimatedDrop - a.estimatedDrop || a.played.ply - b.played.ply);
        let count = 0;
        for (const item of ranked) {
            const key = positionKey(api.parseGameRecord(item.record).state, api.shapes);
            if (seen.has(key)) continue;
            selected.push(item); seen.add(key);
            if (++count === 2) break;
        }
    }
    const worker = analysisWorker(values.model, values.assembly);
    const classical = analysisWorker(null, values.assembly);
    try {
        // Warm both JITs before comparing short timed searches. Recorded node limits reproduce the original failure independently of timing.
        for (const client of [worker, classical])
            await client.analyze({ record: selected[0].record, maxNodes: 200000, budgetMs: 2000 });
        for (const [index, item] of selected.entries()) {
            const reference = teacher(item.record, 2000000);
            const referenceChoice = teacher(item.record + ' ' + reference.move, 2000000);
            const choices = [];
            for (const [name, client, options] of [
                ['recorded-nodes', worker, { maxNodes: item.played.nodes, budgetMs: 30000 }],
                ['nnue-100ms', worker, { budgetMs: 100 }], ['nnue-1000ms', worker, { budgetMs: 1000 }],
                ['nnue-4000ms', worker, { budgetMs: 4000 }], ['nnue-api-cap', worker, { budgetMs: 4000, maxNodes: 100000 }],
                ['classical-4000ms', classical, { budgetMs: 4000 }],
                ['nnue-depth3', worker, { budgetMs: 10000, maxDepth: 3 }],
                ['nnue-depth4', worker, { budgetMs: 10000, maxDepth: 4 }],
                ['classical-depth3', classical, { budgetMs: 10000, maxDepth: 3 }],
                ['classical-depth4', classical, { budgetMs: 10000, maxDepth: 4 }],
            ]) {
                const decision = await client.analyze({ record: item.record, ...options });
                if (name === 'recorded-nodes') {
                    assert.equal(decision.move, item.move, 'The recorded move must reproduce at its original node budget.');
                    assert.equal(decision.score, item.played.score);
                    assert.equal(decision.depth, item.played.depth);
                }
                const after = teacher(item.record + ' ' + decision.move, 2000000);
                const evaluation = after.terminal ? null : (await worker.analyze({ record: item.record + ' ' + decision.move, evaluateOnly: true })).value;
                choices.push({ name, ...decision, teacherAfter: after,
                    staticValueForMover: evaluation === null ? null : after.player === item.played.player ? evaluation : -evaluation,
                    ...assessMove(reference, after, item.played.player, after.player) });
            }
            const referenceStatic = referenceChoice.terminal ? null : (await worker.analyze({ record: item.record + ' ' + reference.move, evaluateOnly: true })).value;
            write({ type: 'case', index: index + 1, source: item, reference, referenceChoice,
                referenceStaticValueForMover: referenceStatic === null ? null : referenceChoice.player === item.played.player ? referenceStatic : -referenceStatic,
                teacherDepth3: teacher(item.record, 2000000, 3), choices });
            console.error(`Counterfactual case ${index + 1}/${selected.length} complete.`);
        }
        write({ type: 'summary', screenedMoves: screened.length, losingGames: games.filter(game => game.result === 0).length,
            provenBlunders: screened.filter(item => item.provenBlunder).length, cases: selected.length,
            note: 'Screening at 200000 teacher nodes; counterfactuals at 2000000. Heuristic drops are estimates; only proven non-losing -> proven losing transitions count as proven blunders.' });
    } finally { worker.stop(); classical.stop(); }
}

async function main() {
    const { values } = parseArgs({ options: {
        mode: { type: 'string' }, reference: { type: 'string' }, output: { type: 'string' }, openings: { type: 'string' },
        model: { type: 'string', default: 'models/nnue-v1/h512/model.nnue' },
        budget: { type: 'string', default: '100' }, count: { type: 'string', default: '12' },
        seed: { type: 'string', default: '20260930' }, assembly: { type: 'string', default: analysisAssembly },
        nodes: { type: 'string' },
        log: { type: 'string', multiple: true, default: [] }, exclude: { type: 'string', multiple: true, default: [] },
        'shared-families': { type: 'boolean', default: false }, 'opponent-model': { type: 'string' },
        plies: { type: 'string', default: '3' }, 'exclude-descendants': { type: 'boolean', default: false },
    } });
    assert.ok(values.reference && values.output);
    assert.ok(['prepare', 'matches', 'analyse'].includes(values.mode));
    const manifest = JSON.parse(fs.readFileSync(resolve(dirname(values.model), 'manifest.json')));
    const referenceCommit = execFileSync('git', ['-C', values.reference, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim();
    assert.equal(referenceCommit, manifest.teacher_commit);
    execFileSync('git', ['-C', values.reference, 'diff', '--exit-code', 'HEAD', '--', 'src/game']);
    const api = await loadReference(values.reference);
    const known = corpusKeys(manifest);
    const context = { referenceCommit, model: values.model, modelSha256: sha(fs.readFileSync(values.model)),
        assemblySha256: sha(fs.readFileSync(values.assembly)), datasets: manifest.datasets,
        environment: { cpu: cpus()[0].model, node: process.version, dotnet: execFileSync('dotnet', ['--version'], { encoding: 'utf8' }).trim() } };
    fs.mkdirSync(dirname(values.output), { recursive: true });
    if (values.mode === 'prepare') {
        const count = Number(values.count), seed = Number(values.seed), plies = Number(values.plies);
        assert.ok(Number.isInteger(count) && count > 0 && Number.isInteger(seed) && Number.isInteger(plies) && plies >= 1 && plies <= 10);
        const families = new Set(Object.values(manifest.partitions).flatMap(partition => partition.opening_keys));
        const reserved = JSON.parse(fs.readFileSync('training/holdout-records.json'));
        for (const record of reserved.records) known.add(positionKey(api.parseGameRecord(record).state, api.shapes));
        for (const path of values.exclude) {
            const previous = JSON.parse(fs.readFileSync(path));
            for (const opening of previous.openings) { families.add(opening.familyKey); known.add(opening.positionKey); }
        }
        const openings = selectOpenings(api, families, known, count, seed, 50000, values['shared-families'], plies,
            values['exclude-descendants'] ? descendantExclusion(known) : undefined);
        fs.writeFileSync(values.output, JSON.stringify({ ...context, seed, openings,
            plies, excludedKnownDescendants: values['exclude-descendants'],
            method: 'Starts absent from the model datasets up to horizontal mirror and color swap; outside excluded first-placement families; exclude starts proven at 50000 teacher nodes. If enabled, monotone board/reserve checks exclude every possible known descendant.',
            sharedFirstPlacementFamilies: values['shared-families'] }, null, 2) + '\n', { flag: 'wx' });
        console.log(`Reserved ${openings.length} starts against ${known.size} corpus keys.`);
        return;
    }
    if (values.mode === 'analyse') {
        assert.ok(values.log.length > 0);
        const output = fs.openSync(values.output, 'wx');
        const write = object => fs.writeSync(output, JSON.stringify(object) + '\n');
        try {
            write({ type: 'context', ...context, matches: values.log.map(path => ({ path, sha256: sha(fs.readFileSync(path)) })) });
            await analyseGames(api, values, context, write);
        } finally { fs.closeSync(output); }
        return;
    }
    const reservation = JSON.parse(fs.readFileSync(values.openings));
    assert.equal(reservation.referenceCommit, referenceCommit);
    // A frozen panel can compare multiple checkpoints; every checkpoint must still exclude its starting states.
    const budgetMs = Number(values.budget), maxNodes = values.nodes ? Number(values.nodes) : 2147483647;
    assert.ok(Number.isInteger(budgetMs) && budgetMs > 0 && Number.isInteger(maxNodes) && maxNodes > 0);
    const out = fs.openSync(values.output, 'wx');
    const write = object => fs.writeSync(out, JSON.stringify(object) + '\n');
    const worker = analysisWorker(values.model, values.assembly);
    const opponent = values['opponent-model'] ? analysisWorker(values['opponent-model'], values.assembly) : null;
    try {
        write({ type: 'context', ...context, budgetMs, maxNodes, openingsSha256: sha(fs.readFileSync(values.openings)),
            opponentModel: values['opponent-model'] ?? null,
            opponentModelSha256: opponent ? sha(fs.readFileSync(values['opponent-model'])) : null });
        await worker.analyze({ record: reservation.openings[0].record, maxNodes: 200000, budgetMs: 2000 });
        if (opponent) await opponent.analyze({ record: reservation.openings[0].record, maxNodes: 200000, budgetMs: 2000 });
        else api.searchMasterTopMoves(api.parseGameRecord(reservation.openings[0].record).state, { maxNodes: 200000 });
        const candidate = record => worker.analyze({ record, budgetMs, maxNodes });
        const teacher = opponent ? record => opponent.analyze({ record, budgetMs, maxNodes }) : (_, state) => {
            const started = performance.now();
            const decision = api.searchMasterTopMoves(state, { budgetMs });
            return { move: token(api, decision.moves[0]), score: decision.score, depth: decision.depth,
                nodes: decision.nodes, exact: decision.exact, elapsedMs: performance.now() - started, pv: decision.pv.map(move => token(api, move)) };
        };
        const games = [];
        for (const opening of reservation.openings) {
            assert.ok(!known.has(opening.positionKey));
            assert.equal(positionKey(api.parseGameRecord(opening.record).state, api.shapes), opening.positionKey);
            for (const color of ['blue', 'white']) {
                const game = await playGame(api, opening, color, budgetMs, candidate, teacher);
                game.trainingOverlap = game.turns.filter(turn => known.has(positionKey(api.parseGameRecord(turn.record).state, api.shapes))).length;
                games.push(game); write(game);
                console.error(JSON.stringify({ budgetMs, games: games.length, total: reservation.openings.length * 2, result: game.result, ...summarize(games) }));
            }
        }
        write({ type: 'summary', ...summarize(games) });
    } finally { worker.stop(); opponent?.stop(); fs.closeSync(out); }
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
