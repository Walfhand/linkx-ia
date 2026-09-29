"""Check GPU checkpoint -> CPU PyTorch -> native C# on validation and independent rules fixtures."""
import argparse
import hashlib
import json
import subprocess
from pathlib import Path

import numpy as np
import torch

from data import canonical_key
from nnue import Nnue, encode_nnue

parser = argparse.ArgumentParser()
parser.add_argument('run', type=Path)
args = parser.parse_args()
manifest = json.loads((args.run / 'manifest.json').read_text())
model = Nnue(manifest['architecture'][1]).eval()
model.load_state_dict(torch.load(args.run / 'weights.pt', weights_only=True, map_location='cpu'))
examples = np.load(args.run / 'export-check.npz')
with torch.no_grad():
    cpu = model(torch.from_numpy(examples['features'])).numpy()
np.testing.assert_allclose(cpu, examples['predictions'], rtol=1e-6, atol=1e-6)
checks = json.loads((args.run / 'dotnet-checks.json').read_text())[:len(examples['features'])]
fixtures = json.loads(Path('tests/LinkxAi.Tests.Unit/ReferenceFixtures/rules.json').read_text())
for item in fixtures['cases']:
    if 'error' in item:
        continue
    row = dict(board=item['board'], inventories=item['inventories'], active_player=item['activePlayer'],
               score=0, exact=False, outcome=0, depth=1, game_id='fixture', opening_key='fixture')
    row['position_key'] = canonical_key(row)
    with torch.no_grad():
        value = model(torch.from_numpy(encode_nnue(row)[None])).item()
    checks.append({'record': item['record'], 'value': value})
request = '\n'.join(json.dumps(dict(record=item['record'], evaluateOnly=True)) for item in checks) + '\n'
result = subprocess.run(['dotnet', 'tools/LinkxAi.Analysis/bin/Release/net10.0/LinkxAi.Analysis.dll',
                         str(args.run / 'model.nnue')], input=request, text=True, capture_output=True, check=True)
actual = [json.loads(line)['value'] for line in result.stdout.splitlines()]
expected = [item['value'] for item in checks]
np.testing.assert_allclose(actual, expected, rtol=1e-6, atol=1e-6)
report = dict(positions=len(checks), gpu_to_cpu_maximum_absolute_error=float(np.abs(cpu - examples['predictions']).max()),
              native_maximum_absolute_error=float(np.abs(np.asarray(actual) - expected).max()),
              model_bytes=(args.run / 'model.nnue').stat().st_size,
              model_sha256=hashlib.sha256((args.run / 'model.nnue').read_bytes()).hexdigest(),
              export_torch=torch.__version__)
(args.run / 'export.json').write_text(json.dumps(report, indent=2) + '\n')
(args.run / 'dotnet-checks.json').write_text(json.dumps(checks, indent=2) + '\n')
print(json.dumps(report, indent=2))
