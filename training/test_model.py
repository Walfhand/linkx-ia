import tempfile
import unittest
from pathlib import Path

import numpy as np
import onnxruntime as ort
import torch

from model import ValueNet, fit
from export import export_model


class ModelTests(unittest.TestCase):
    def test_warm_start_can_keep_the_parent_when_all_updates_hurt_validation(self):
        torch.manual_seed(4)
        torch.set_num_threads(2)
        features = torch.ones(32, 176)
        model = ValueNet()
        weights = {key: value.clone() for key, value in model.state_dict().items()}
        result = fit(model, features, torch.ones(32, 1), features, -torch.ones(32, 1), epochs=25, keep_initial=True)
        self.assertEqual(result['best_epoch'], 0)
        for key, value in model.state_dict().items():
            torch.testing.assert_close(value, weights[key], rtol=0, atol=0)

    def test_fit_restores_early_weights_when_later_epochs_hurt_validation(self):
        torch.manual_seed(4)
        torch.set_num_threads(2)
        features = torch.ones(32, 176)
        model = ValueNet()
        result = fit(model, features, torch.ones(32, 1), features, -torch.ones(32, 1), epochs=40, seed=4)
        self.assertLess(result["best_epoch"], len(result["history"]))
        actual = torch.mean((model(features) + 1) ** 2).item()
        self.assertAlmostEqual(actual, min(item["validation_mse"] for item in result["history"]), places=6)

    def test_fit_learns_a_signal_and_restores_the_best_validation_weights(self):
        torch.manual_seed(7)
        torch.set_num_threads(2)
        features = torch.zeros(128, 176)
        features[:, 0] = torch.linspace(0, 1, 128)
        labels = features[:, :1] * 2 - 1
        validation = features[::5].clone()
        expected = labels[::5].clone()
        model = ValueNet()
        initial = torch.mean((model(validation) - expected) ** 2).item()

        result = fit(model, features, labels, validation, expected, epochs=80, batch_size=64, seed=7)

        final = torch.mean((model(validation) - expected) ** 2).item()
        self.assertLess(final, initial * 0.5)
        self.assertAlmostEqual(final, min(epoch["validation_mse"] for epoch in result["history"]), places=6)
        self.assertTrue(torch.isfinite(model(validation)).all())

    def test_export_preserves_predictions_for_single_and_multiple_positions(self):
        torch.manual_seed(9)
        model = ValueNet().eval()
        features = torch.rand(8, 176)
        expected = model(features).detach().numpy()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "model.onnx"
            export_model(model, path, features.numpy(), expected)
            session = ort.InferenceSession(str(path), providers=["CPUExecutionProvider"])
            for count in (1, 8):
                actual = session.run(None, {"position": features[:count].numpy()})[0]
                np.testing.assert_allclose(actual, expected[:count], rtol=1e-5, atol=1e-5)


if __name__ == "__main__":
    unittest.main()
