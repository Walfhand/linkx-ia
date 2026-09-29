// Independent validation of the C# candidate. Build tools/LinkxAi.Analysis first.
// Usage: node scripts/validate-teacher.mjs REFERENCE_CHECKOUT EXACT_ENDGAMES_JSON [budgetMs] [model.onnx]
import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync, spawn } from 'node:child_process';
import { cpus } from 'node:os';
import { createHash } from 'node:crypto';
import { createInterface } from 'node:readline';
import { resolve } from 'node:path';
import { pathToFileURL } from 'node:url';

const [reference, exactPath, budgetText = '100', modelPath] = process.argv.slice(2);
if (!reference || !exactPath) throw new Error('Provide reference checkout and exact endgames.');
const budgetMs = Number(budgetText);
assert.ok(Number.isInteger(budgetMs) && budgetMs > 0);
const source = file => import(pathToFileURL(resolve(reference, file)).href);
const { parseGameRecord, serializeGameRecord, serializeMove } = await source('src/game/moveNotation.ts');
const { enumerateLegalMoves } = await source('src/game/legalMoves.ts');
const { searchMasterTopMoves } = await source('src/game/engineSearch.ts');
const { OPENINGS } = await source('supabase/functions/_shared/openings.ts');
const exact = JSON.parse(fs.readFileSync(exactPath));
assert.equal(exact.referenceCommit, execFileSync('git', ['-C', reference, 'rev-parse', 'HEAD'], { encoding: 'utf8' }).trim());
const token = move => serializeMove({ shapeId: move.shapeId, column: move.column, ...move.orientation });

const assembly = 'tools/LinkxAi.Analysis/bin/Release/net10.0/LinkxAi.Analysis.dll';
assert.ok(fs.existsSync(assembly), 'Build tools/LinkxAi.Analysis in Release first.');
function worker(model) {
    const child = spawn('dotnet', [assembly, ...(model ? [model] : [])], { stdio: ['pipe', 'pipe', 'inherit'] });
    const pending = [];
    let stopped = false;
    createInterface({ input: child.stdout }).on('line', line => {
        const request = pending.shift();
        if (!request) throw new Error('Unexpected analysis response.');
        try { request.resolve(JSON.parse(line)); } catch (error) { request.reject(error); }
    });
    child.on('exit', code => {
        stopped = true;
        for (const request of pending.splice(0)) request.reject(new Error(`Analysis worker exited: ${code}`));
    });
    return {
        analyze(request) {
            if (stopped) return Promise.reject(new Error('Analysis worker has stopped.'));
            return new Promise((resolve, reject) => {
                pending.push({ resolve, reject });
                child.stdin.write(JSON.stringify(request) + '\n');
            });
        },
        stop() { child.stdin.end(); child.kill(); },
    };
}
const candidate = worker(modelPath);
const classical = modelPath ? worker() : null;

try {
    let correct = 0;
    const errors = [];
    for (const position of exact.cases) {
        const decision = await candidate.analyze({ record: position.record, maxNodes: 100_000, budgetMs: 10_000 });
        if (decision.exact && Math.sign(decision.score) === position.value && position.optimalMoves.includes(decision.move)) correct++;
        else errors.push({ record: position.record, expected: position.value, decision });
    }
    assert.equal(errors.length, 0, JSON.stringify(errors.slice(0, 3)));

    const openings = ['', ...OPENINGS.map(opening => opening.notation)];
    async function duel(opponent) {
        const games = [];
        let maximumMs = 0;
        for (const opening of openings) {
            for (const color of ['blue', 'white']) {
                let record = opening;
                while (true) {
                    const parsed = parseGameRecord(record);
                    assert.equal(parsed.ok, true, record);
                    const state = parsed.state;
                    if (state.result) {
                        games.push({ opening, color, result: state.result.winner === null ? 0.5 : state.result.winner === color ? 1 : 0 });
                        break;
                    }
                    let move;
                    if (state.activePlayer === color) {
                        const decision = await candidate.analyze({ record, budgetMs });
                        maximumMs = Math.max(maximumMs, decision.elapsedMs);
                        move = decision.move;
                    } else if (opponent === 'first-legal') {
                        move = token(enumerateLegalMoves(state.board, state.inventories[state.activePlayer])[0]);
                    } else if (opponent === 'classical') {
                        move = (await classical.analyze({ record, budgetMs })).move;
                    } else {
                        move = token(searchMasterTopMoves(state, { budgetMs }).moves[0]);
                    }
                    assert.equal(typeof move, 'string');
                    assert.notEqual(move, '--');
                    record = [serializeGameRecord(state), move].filter(Boolean).join(' ');
                }
            }
            console.error(`${opponent}: ${games.length}/${openings.length * 2} games`);
        }
        return { budgetMs, games, wins: games.filter(game => game.result === 1).length,
            draws: games.filter(game => game.result === 0.5).length, losses: games.filter(game => game.result === 0).length,
            score: games.reduce((sum, game) => sum + game.result, 0) / games.length, maximumMs };
    }
    const baseline = await duel('first-legal');
    const referenceDuel = await duel('marmelab');
    const classicalDuel = classical ? await duel('classical') : undefined;
    console.log(JSON.stringify({ referenceCommit: exact.referenceCommit,
        environment: { cpu: cpus()[0].model, node: process.version, dotnet: execFileSync('dotnet', ['--version'], {encoding:'utf8'}).trim() },
        exactCases: exact.cases.length,
        exactCorrect: correct, referenceCorrectMoves: exact.cases.filter(item => item.reference.correct).length,
        referenceProvenResults: exact.cases.filter(item => item.reference.exact && item.reference.correctValue).length,
        model: modelPath ? { path: modelPath, sha256: createHash('sha256').update(fs.readFileSync(modelPath)).digest('hex') } : null,
        baseline, referenceDuel, classicalDuel,
        note: 'Diagnostic paired games, not an Elo estimate or proof of opening/middlegame strength.' }, null, 2));
} finally {
    candidate.stop();
    classical?.stop();
}
