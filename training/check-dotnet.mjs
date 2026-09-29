import assert from 'node:assert/strict';
import fs from 'node:fs';
import { execFileSync } from 'node:child_process';

const checks = JSON.parse(fs.readFileSync('models/pilot-v1/dotnet-checks.json'));
const output = execFileSync('dotnet', ['tools/LinkxAi.Analysis/bin/Release/net10.0/LinkxAi.Analysis.dll',
    'models/pilot-v1/model.onnx'], {
    input: checks.map(item => JSON.stringify({ record: item.record, evaluateOnly: true, budgetMs: 0 })).join('\n') + '\n',
    encoding: 'utf8',
});
const rows = output.trim().split('\n').map(JSON.parse);
assert.equal(rows.length, checks.length);
for (let i = 0; i < rows.length; i++)
    assert.ok(Number.isFinite(rows[i].value) && Math.abs(rows[i].value - checks[i].value) < 1e-5,
        `C# feature encoding or inference differs on ${checks[i].record}: ${JSON.stringify(rows[i])}`);
console.log(`C# and PyTorch agree on ${checks.length} positions.`);
