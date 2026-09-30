import assert from 'node:assert/strict';
import fs from 'node:fs';
import { resolve, join } from 'node:path';
import { fileURLToPath, pathToFileURL } from 'node:url';
import { createHash } from 'node:crypto';
import { execFileSync } from 'node:child_process';
import { parseArgs } from 'node:util';
import { analysisWorker, analysisAssembly } from '../scripts/analysis-worker.mjs';
import { positionKey } from '../scripts/diagnose-nnue.mjs';

const sha = bytes => createHash('sha256').update(bytes).digest('hex');
const modelHash = folder => sha(fs.readFileSync(join(folder, 'model.nnue')));

export function evaluationProtocol(values, candidates) {
    const pairs = Number(values.pairs ?? 64), confirmation = values.confirmation ?? false;
    assert.ok(Number.isInteger(pairs) && pairs >= 64, 'At least 64 complete opening pairs are required.');
    assert.ok(!confirmation || candidates.length === 1, 'Confirmation must fix exactly one candidate.');
    return { pairs, confirmation };
}

export function readMatchReport(text, candidateHash, opponentHash, expectedContext = {}) {
    const rows = text.trim().split('\n').map(JSON.parse);
    assert.equal(rows[0].type, 'context');
    assert.equal(rows[0].modelSha256, candidateHash);
    assert.equal(rows[0].opponentModelSha256 ?? null, opponentHash);
    for (const [field, expected] of Object.entries(expectedContext)) assert.equal(rows[0][field], expected);
    assert.equal(rows.at(-1).type, 'summary', 'Incomplete evaluation report.');
    const games = rows.filter(row => row.type === 'game');
    assert.equal(rows.at(-1).games, games.length);
    assert.ok(games.length > 0);
    assert.equal(rows.at(-1).score, games.reduce((sum, game) => sum + game.result, 0) / games.length);
    return { summary: rows.at(-1), games };
}

async function main() {
    const { values } = parseArgs({ options: {
        reference: { type: 'string' }, parent: { type: 'string' }, candidates: { type: 'string' },
        output: { type: 'string' }, budget: { type: 'string', default: '100' }, seed: { type: 'string', default: '73201' },
        pairs: { type: 'string', default: '64' }, confirmation: { type: 'boolean', default: false },
        assembly: { type: 'string', default: analysisAssembly },
    } });
    assert.ok(values.reference && values.parent && values.candidates && values.output);
    const candidates = values.candidates.split(',');
    assert.ok(candidates.length > 0);
    const protocol = evaluationProtocol(values, candidates);
    fs.mkdirSync(values.output, { recursive: true });
    const path = name => join(values.output, name);
    const parentHash = modelHash(values.parent);
    function duel(candidate, opponent, openings, output) {
        if (!fs.existsSync(output)) {
            const log = fs.openSync(output + '.log', 'w');
            try {
                execFileSync(process.execPath, ['scripts/diagnose-nnue.mjs', '--mode', 'matches', '--reference', values.reference,
                    '--assembly', values.assembly,
                    '--model', join(candidate, 'model.nnue'), '--openings', openings, '--budget', values.budget, '--output', output,
                    ...(opponent ? ['--opponent-model', join(opponent, 'model.nnue')] : [])], { stdio: ['ignore', log, log] });
            } finally { fs.closeSync(log); }
        }
        const report = readMatchReport(fs.readFileSync(output, 'utf8'), modelHash(candidate), opponent ? modelHash(opponent) : null,
            { budgetMs: Number(values.budget), openingsSha256: sha(fs.readFileSync(openings)), assemblySha256: sha(fs.readFileSync(values.assembly)) });
        assert.equal(report.games.length, JSON.parse(fs.readFileSync(openings)).openings.length * 2);
        return report;
    }
    const unique = [...new Map(candidates.map(folder => [modelHash(folder), folder])).entries()];
    const development = [];
    for (const [hash, candidate] of protocol.confirmation ? [] : unique) {
        if (hash === parentHash) {
            development.push({ candidate, score: 0.5, identical: true, validationMse: JSON.parse(fs.readFileSync(join(candidate, 'metrics.json'))).validation.mse });
            continue;
        }
        console.error(`Development matches: ${candidate}`);
        const result = duel(candidate, values.parent, 'benchmarks/nnue-validation-openings.json', path(`development-${hash.slice(0, 12)}.jsonl`));
        development.push({ candidate, score: result.summary.score, identical: false,
            validationMse: JSON.parse(fs.readFileSync(join(candidate, 'metrics.json'))).validation.mse, result: result.summary });
    }
    development.sort((a, b) => b.score - a.score || a.validationMse - b.validationMse);
    const candidate = protocol.confirmation ? candidates[0] : development[0].candidate;
    const selection = { candidate, development, protocol, identical_to_parent: modelHash(candidate) === parentHash };
    if (selection.identical_to_parent) {
        fs.writeFileSync(path('selection.json'), JSON.stringify(selection, null, 2) + '\n');
        return;
    }
    const panel = path('promotion-openings.json');
    if (!fs.existsSync(panel))
        execFileSync(process.execPath, ['scripts/diagnose-nnue.mjs', '--mode', 'prepare', '--reference', values.reference,
            '--assembly', values.assembly,
            '--model', join(candidate, 'model.nnue'), '--count', String(protocol.pairs), '--seed', values.seed, '--shared-families',
            '--plies', '5', '--exclude-descendants',
            '--exclude', 'benchmarks/nnue-diagnostic-openings.json', '--exclude', 'benchmarks/nnue-validation-openings.json',
            '--output', panel], { stdio: ['ignore', 'inherit', 'inherit'] });
    assert.equal(JSON.parse(fs.readFileSync(panel)).openings.length, protocol.pairs);
    console.error('Promotion matches: candidate vs parent.');
    const parentDuel = duel(candidate, values.parent, panel, path('candidate-parent.jsonl'));
    console.error('Promotion matches: candidate vs Marmelab.');
    const candidateTeacher = duel(candidate, null, panel, path('candidate-teacher.jsonl'));
    console.error('Promotion matches: parent vs Marmelab.');
    const parentTeacher = duel(values.parent, null, panel, path('parent-teacher.jsonl'));

    const source = file => import(pathToFileURL(resolve(values.reference, file)).href);
    const { parseGameRecord } = await source('src/game/moveNotation.ts');
    const { SHAPE_IDS } = await source('src/game/types.ts');
    const key = record => {
        const parsed = parseGameRecord(record); assert.equal(parsed.ok, true); return positionKey(parsed.state, SHAPE_IDS);
    };
    const manifest = JSON.parse(fs.readFileSync(join(candidate, 'manifest.json')));
    const known = new Set();
    for (const data of manifest.datasets) {
        const bytes = fs.readFileSync(join('training/data', data.file)); assert.equal(sha(bytes), data.sha256);
        for (const row of bytes.toString().trim().split('\n').map(JSON.parse)) known.add(row.position_key);
    }
    const games = [...parentDuel.games, ...candidateTeacher.games, ...parentTeacher.games];
    selection.promotion_overlap = games.flatMap(game => game.turns).filter(turn => known.has(key(turn.record))).length;
    const exact = JSON.parse(fs.readFileSync('benchmarks/exact-endgames.json'));
    const worker = analysisWorker(join(candidate, 'model.nnue'), values.assembly);
    const errors = [];
    try {
        for (const item of exact.cases) {
            const decision = await worker.analyze({ record: item.record, maxNodes: 100000, budgetMs: 10000 });
            if (!(decision.exact && Math.sign(decision.score) === item.value && item.optimalMoves.includes(decision.move)))
                errors.push({ record: item.record, decision, expected: item.value });
        }
    } finally { worker.stop(); }
    selection.exact_cases_passed = errors.length === 0;
    selection.exact = { cases: exact.cases.length, correct: exact.cases.length - errors.length, errors };
    fs.writeFileSync(path('selection.json'), JSON.stringify(selection, null, 2) + '\n');

    // Keep all final evaluation trajectories excluded from subsequent collection iterations.
    const holdoutPath = 'training/holdout-records.json';
    const holdout = JSON.parse(fs.readFileSync(holdoutPath));
    const reserved = new Set(holdout.records.map(key));
    for (const game of games) for (const record of [...game.turns.map(turn => turn.record), game.record]) {
        const identity = key(record);
        if (!reserved.has(identity)) { reserved.add(identity); holdout.records.push(record); }
    }
    holdout.loopEvaluationFiles ??= [];
    for (const name of ['candidate-parent.jsonl', 'candidate-teacher.jsonl', 'parent-teacher.jsonl']) {
        const file = path(name);
        const item = { file, sha256: sha(fs.readFileSync(file)) };
        if (!holdout.loopEvaluationFiles.some(previous => previous.file === file)) holdout.loopEvaluationFiles.push(item);
    }
    fs.writeFileSync(holdoutPath, JSON.stringify(holdout, null, 2) + '\n');
}

if (process.argv[1] && resolve(process.argv[1]) === fileURLToPath(import.meta.url)) await main();
