import assert from 'node:assert/strict';
import { test } from 'node:test';
import fs from 'node:fs';
import os from 'node:os';
import { join, resolve } from 'node:path';
import { pathToFileURL } from 'node:url';
import { execFileSync } from 'node:child_process';
import { analysisAssembly } from '../scripts/analysis-worker.mjs';

test('learner self-play is reproducible, collects hypothetical states and respects frozen exclusions', async () => {
    assert.ok(process.env.LINKX_REFERENCE && fs.existsSync(analysisAssembly));
    const { parseGameRecord } = await import(pathToFileURL(resolve(process.env.LINKX_REFERENCE, 'src/game/moveNotation.ts')).href);
    const manifest = JSON.parse(fs.readFileSync('models/nnue-v1/h512/manifest.json'));
    const directory = fs.mkdtempSync(join(os.tmpdir(), 'linkx-selfplay-'));
    try {
        const exclusionFile = join(directory, 'exclusions.json');
        fs.writeFileSync(exclusionFile, JSON.stringify({ trainingOpeningKeys: manifest.partitions.train.opening_keys,
            excludedKeys: ['.'.repeat(81) + '|22222222222222'], splitSeed: 42 }));
        const outputs = [];
        for (const name of ['first', 'second']) {
            const output = join(directory, name + '.jsonl');
            execFileSync(process.execPath, ['training/selfplay.mjs', '--reference', process.env.LINKX_REFERENCE,
                '--model', 'models/nnue-v1/h512/model.nnue', '--exclusions', exclusionFile,
                '--output', output, '--games', '4', '--seed', '19', '--actor-nodes', '500', '--teacher-nodes', '2000', '--samples', '2'],
                { stdio: ['ignore', 'ignore', 'pipe'] });
            const text = fs.readFileSync(output, 'utf8'); outputs.push(text);
            const rows = text.trim().split('\n').map(JSON.parse);
            assert.ok(rows.some(row => row.source === 'root'));
            assert.ok(rows.some(row => row.source === 'tree'));
            const info = JSON.parse(fs.readFileSync(output + '.meta.json'));
            assert.equal(info.schema, 2); assert.equal(info.completedGames, 4);
            assert.equal(info.samples, rows.length);
            for (const row of rows) {
                assert.ok(manifest.partitions.train.opening_keys.includes(row.opening_key));
                assert.notEqual(row.position_key, '.'.repeat(81) + '|22222222222222');
                assert.ok(!info.reservedKeys.includes(row.position_key));
                const parsed = parseGameRecord(row.record);
                assert.equal(parsed.ok, true); assert.equal(parsed.state.result, null);
                assert.equal(parsed.state.activePlayer, row.active_player);
                assert.equal(row.board, parsed.state.board.flat().map(cell => cell ? cell.player === 'blue' ? 'B' : 'W' : '.').join(''));
                assert.ok(row.depth >= 1);
                if (row.source === 'tree') { assert.equal(row.outcome, null); assert.equal(row.winner, null); }
                else assert.equal(row.outcome, row.winner === null ? 0 : row.winner === row.active_player ? 1 : -1);
            }
        }
        assert.equal(outputs[0], outputs[1]);
    } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});
