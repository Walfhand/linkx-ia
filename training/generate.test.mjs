import assert from 'node:assert/strict';
import { execFileSync } from 'node:child_process';
import fs from 'node:fs';
import os from 'node:os';
import { join } from 'node:path';
import { pathToFileURL } from 'node:url';
import { test } from 'node:test';

test('teacher corpus is reproducible, labeled from the moving player and excludes reserved positions', async () => {
    assert.ok(process.env.LINKX_REFERENCE, 'Set LINKX_REFERENCE to the pinned reference checkout.');
    const { parseGameRecord } = await import(pathToFileURL(join(process.env.LINKX_REFERENCE, 'src/game/moveNotation.ts')).href);
    const directory = fs.mkdtempSync(join(os.tmpdir(), 'linkx-generation-'));
    try {
        const outputs = [];
        for (const name of ['a', 'b']) {
            const file = join(directory, name + '.jsonl');
            execFileSync(process.execPath, ['training/generate-data.mjs', '--reference', process.env.LINKX_REFERENCE,
                '--games', '3', '--nodes', '2000', '--seed', '53', '--output', file], { stdio: ['ignore', 'ignore', 'pipe'] });
            outputs.push(fs.readFileSync(file, 'utf8'));
            const rows = outputs.at(-1).trim().split('\n').map(JSON.parse);
            assert.ok(rows.length >= 20);
            const metadata = JSON.parse(fs.readFileSync(file + '.meta.json'));
            assert.equal(metadata.completedGames, 3);
            for (const row of rows) {
                const parsed = parseGameRecord(row.record);
                assert.equal(parsed.ok, true);
                assert.equal(row.active_player, parsed.state.activePlayer);
                assert.equal(row.board, parsed.state.board.flat().map(cell => cell ? cell.player === 'blue' ? 'B' : 'W' : '.').join(''));
                for (const color of ['blue', 'white'])
                    assert.deepEqual(row.inventories[color], metadata.shapes.map(shape => parsed.state.inventories[color][shape]));
                assert.equal(row.board.length, 81);
                assert.ok([-1, 0, 1].includes(row.outcome));
                assert.ok(row.depth >= 1);
                assert.ok(!metadata.reservedKeys.includes(row.position_key));
                assert.equal(row.outcome, row.winner === null ? 0 : row.winner === row.active_player ? 1 : -1);
            }
        }
        assert.equal(outputs[0], outputs[1]);
    } finally { fs.rmSync(directory, { recursive: true, force: true }); }
});
