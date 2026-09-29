"""Export learned weights and verify GPU/PyTorch/ONNX equivalence on held-out examples."""
import argparse
import hashlib
import json
import time
from pathlib import Path

import numpy as np
import onnx
import onnxruntime as ort
import torch

from model import ValueNet


def export_model(model, path, features, expected=None):
    model = model.cpu().eval()
    values = np.asarray(features, dtype=np.float32)
    with torch.no_grad():
        predicted = model(torch.from_numpy(values)).numpy()
    if expected is not None:
        np.testing.assert_allclose(predicted, expected, rtol=1e-4, atol=1e-4)
    device_error = float(np.max(np.abs(predicted - expected))) if expected is not None else None
    torch.onnx.export(model, (torch.from_numpy(values),), str(path), dynamo=True, opset_version=18,
                      input_names=["position"], output_names=["value"], external_data=False,
                      dynamic_shapes=({0: torch.export.Dim("batch")},))
    onnx.checker.check_model(str(path))
    options = ort.SessionOptions()
    options.intra_op_num_threads = 1
    options.inter_op_num_threads = 1
    session = ort.InferenceSession(str(path), options, providers=["CPUExecutionProvider"])
    actual = session.run(None, {"position": values})[0]
    np.testing.assert_allclose(actual, predicted, rtol=1e-5, atol=1e-5)
    for _ in range(30):
        session.run(None, {"position": values[:1]})
    times = []
    for _ in range(500):
        started = time.perf_counter_ns()
        session.run(None, {"position": values[:1]})
        times.append((time.perf_counter_ns() - started) / 1000)
    return {"maximum_absolute_error": float(np.max(np.abs(actual - predicted))),
            "gpu_to_cpu_maximum_absolute_error": device_error,
            "cpu_microseconds_p50": float(np.median(times)), "cpu_microseconds_p95": float(np.percentile(times, 95)),
            "onnx_bytes": Path(path).stat().st_size, "onnx_sha256": hashlib.sha256(Path(path).read_bytes()).hexdigest(),
            "export_torch": torch.__version__, "onnxruntime": ort.__version__}


if __name__ == "__main__":
    parser = argparse.ArgumentParser()
    parser.add_argument("run", type=Path)
    args = parser.parse_args()
    model = ValueNet()
    model.load_state_dict(torch.load(args.run / "weights.pt", map_location="cpu", weights_only=True))
    examples = np.load(args.run / "export-check.npz")
    report = export_model(model, args.run / "model.onnx", examples["features"], examples["predictions"])
    (args.run / "export.json").write_text(json.dumps(report, indent=2) + "\n")
    print(json.dumps(report, indent=2))
