import copy
import struct
import tempfile
import unittest
from pathlib import Path

import numpy as np
import torch

from nnue import Nnue, encode_nnue, export_nnue
from model import fit
from test_data import sample
from data import canonical_key


class NnueTests(unittest.TestCase):
    def test_features_preserve_perspectives_reserves_and_column_heights(self):
        row = sample(index=80)  # White to move, blue mono at bottom right.
        features = np.asarray(encode_nnue(row))
        self.assertEqual(features.shape, (2, 294))
        self.assertEqual(set(np.flatnonzero(features[0, :162])), {161})
        self.assertEqual(set(np.flatnonzero(features[1, :162])), {80})
        self.assertEqual(features[0, 162 + 2], 1)  # White has two monos.
        self.assertEqual(features[0, 183 + 1], 1)  # Blue has one.
        self.assertEqual(features[0, 204 + 80 + 1], 1)  # Last column height 1.
        self.assertEqual(int(features.sum()), 48)  # 1 cell + 14 reserves + 9 heights, twice.
        mirrored = np.asarray(encode_nnue(row, mirror=True))
        self.assertEqual(mirrored[0, 81 + 72], 1)
        self.assertEqual(mirrored[0, 204 + 1], 1)
        swapped = copy.deepcopy(row)
        swapped['board'] = row['board'].replace('B', 'W')
        swapped['active_player'] = 'blue'
        swapped['inventories'] = {'blue': row['inventories']['white'], 'white': row['inventories']['blue']}
        swapped['position_key'] = canonical_key(swapped)
        np.testing.assert_array_equal(features, encode_nnue(swapped))

    def test_shared_transform_has_the_planned_parameter_counts(self):
        for width, count in [(256, 91969), (512, 183873), (1024, 367681)]:
            self.assertEqual(sum(p.numel() for p in Nnue(width).parameters()), count)

    def test_quantized_training_learns_a_signal(self):
        torch.manual_seed(18)
        torch.set_num_threads(2)
        features = torch.zeros(128, 2, 294)
        features[:64, 0, 72] = 1
        features[64:, 1, 72] = 1
        labels = torch.cat((torch.ones(64, 1), -torch.ones(64, 1))) * 0.6
        model = Nnue(8)
        initial = torch.mean((model(features) - labels) ** 2).item()
        fit(model, features, labels, features, labels, epochs=100, batch_size=64)
        self.assertLess(torch.mean((model(features) - labels) ** 2).item(), initial * 0.3)

    def test_export_matches_integer_arithmetic_including_rounding(self):
        torch.manual_seed(83)
        model = Nnue(8).eval()
        features = np.asarray([encode_nnue(sample(index=i)) for i in (72, 76, 80)], dtype=np.float32)
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / 'model.nnue'
            export_nnue(model, path)
            data = path.read_bytes()
        self.assertEqual(struct.unpack_from('<8sIII', data), (b'LXNNU001', 8, 294, 32))
        offset = 20

        def read(dtype, shape):
            nonlocal offset
            count = int(np.prod(shape))
            result = np.frombuffer(data, dtype=dtype, count=count, offset=offset).reshape(shape).astype(np.int64)
            offset += count * np.dtype(dtype).itemsize
            return result

        ft, bias = read('<i2', (294, 8)), read('<i4', (8,))
        head, head_bias = read('<i2', (32, 16)), read('<i4', (32,))
        output, output_bias = read('<i2', (32, 1)), read('<i4', (1,))
        accumulators = np.clip(features.astype(np.int64) @ ft + bias, 0, 256).reshape(-1, 16)
        hidden = np.clip(np.rint((accumulators @ head.T + head_bias) / 64), 0, 256)
        expected = np.tanh((hidden @ output + output_bias) / 16384)
        actual = model(torch.from_numpy(features)).detach().numpy()
        np.testing.assert_allclose(actual, expected, rtol=1e-6, atol=1e-6)
        self.assertEqual(offset, len(data))

    def test_export_rejects_nonfinite_parameters(self):
        model = Nnue(8)
        with torch.no_grad():
            next(model.parameters()).fill_(float('nan'))
        with tempfile.TemporaryDirectory() as directory, self.assertRaises(ValueError):
            export_nnue(model, Path(directory) / 'invalid.nnue')


if __name__ == '__main__':
    unittest.main()
