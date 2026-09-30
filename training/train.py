"""Train a value evaluator. The model is experimental until game-strength validation."""
import argparse
import hashlib
import json
import os
import time
from pathlib import Path

import numpy as np
import torch

from data import encode, load_corpus, split_samples, target
from model import ValueNet, fit
from nnue import Nnue, encode_nnue, export_nnue


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--data", type=Path, nargs="+", required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--device", choices=["cpu", "cuda"], default="cpu", help="cuda also selects AMD ROCm")
    parser.add_argument("--epochs", type=int, default=200)
    parser.add_argument("--seed", type=int, default=42)
    parser.add_argument("--split-seed", type=int, default=42)
    parser.add_argument("--resume-weights", type=Path)
    parser.add_argument("--prefer-stronger-labels", action="store_true")
    parser.add_argument("--nnue-width", type=int, choices=[256, 512, 1024])
    args = parser.parse_args()
    if args.device == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("GPU requested but PyTorch cannot access it")
    rows, metadata = load_corpus(args.data)
    splits = split_samples(rows, seed=args.split_seed, prefer_stronger=args.prefer_stronger_labels)
    torch.manual_seed(args.seed)
    torch.set_num_threads(4)
    arrays = {}
    encoder = encode_nnue if args.nnue_width else encode
    for name, items in splits.items():
        mirrors = (False, True) if name == "train" else (False,)
        features = np.asarray([encoder(row, mirror=mirror) for row in items for mirror in mirrors], dtype=np.float32)
        labels = np.asarray([[target(row)] for row in items for _ in mirrors], dtype=np.float32)
        arrays[name] = (torch.from_numpy(features).to(args.device), torch.from_numpy(labels).to(args.device))
    args.output.mkdir(parents=True, exist_ok=False)
    model = (Nnue(args.nnue_width) if args.nnue_width else ValueNet()).to(args.device)
    if args.resume_weights:
        model.load_state_dict(torch.load(args.resume_weights, map_location=args.device, weights_only=True))
    started = time.perf_counter()
    result = fit(model, *arrays["train"], *arrays["validation"], epochs=args.epochs, seed=args.seed, keep_initial=args.resume_weights is not None)
    if args.device == "cuda":
        torch.cuda.synchronize()
    seconds = time.perf_counter() - started
    metrics = {"best_epoch": result["best_epoch"], "epochs_completed": len(result["history"]), "training_seconds": seconds,
               "initial_validation_mse": result['initial_validation_mse']}
    with torch.no_grad():
        constant = arrays["train"][1].mean()
        for name, (features, labels) in arrays.items():
            prediction = model(features)
            metrics[name] = {
                "positions": len(splits[name]), "opening_groups": len({row["opening_key"] for row in splits[name]}),
                "games": len({row["game_id"] for row in splits[name]}),
                "mse": torch.mean((prediction - labels) ** 2).item(),
                "mae": torch.mean(torch.abs(prediction - labels)).item(),
                "constant_baseline_mse": torch.mean((constant - labels) ** 2).item(),
            }
        features = arrays["validation"][0][:32]
        np.savez_compressed(args.output / "export-check.npz", features=features.cpu().numpy(), predictions=model(features).cpu().numpy())
    weights = {name: value.detach().cpu() for name, value in model.state_dict().items()}
    torch.save(weights, args.output / "weights.pt")
    if args.nnue_width:
        export_nnue(model, args.output / "model.nnue")
        with torch.no_grad():
            checks = [{"record": row['record'], "value": float(model(arrays['validation'][0][i:i + 1]).item())}
                      for i, row in enumerate(splits['validation'][:32])]
        (args.output / 'dotnet-checks.json').write_text(json.dumps(checks, indent=2) + '\n')
    manifest = {
        "format": "linkx-nnue-v1" if args.nnue_width else "linkx-value-v1", "status": "experimental", "teacher_commit": metadata[0]["referenceCommit"],
        "architecture": [294, args.nnue_width, args.nnue_width * 2, 32, 1] if args.nnue_width else [176, 128, 32, 1],
        "activations": ["clipped_relu", "clipped_relu", "tanh"] if args.nnue_width else ["relu", "relu", "tanh"],
        "parameters": sum(parameter.numel() for parameter in model.parameters()),
        "feature_order": "two perspectives, side-to-move first, shared transform: own cells (81), opponent cells (81), own then opponent categorical reserves (42), categorical column heights (90)" if args.nnue_width else "own cells row-major (81), opponent cells (81), own reserves /2 (7), opponent reserves /2 (7)",
        "shape_order": metadata[0]["shapes"], "perspective": "side to move", "seed": args.seed, "split_seed": args.split_seed,
        "parent_weights_sha256": hashlib.sha256(args.resume_weights.read_bytes()).hexdigest() if args.resume_weights else None,
        "target": "sign(score) if proven; otherwise tanh(score/4000) for hypothetical tree states, or 0.9*tanh(score/4000)+0.1*observed_game_outcome for played states",
        "prefer_stronger_training_labels": args.prefer_stronger_labels,
        "split": "opening groups; horizontal-mirror and color-swap equivalent positions deduplicated across all partitions",
        "raw_samples": len(rows), "unique_samples": sum(len(items) for items in splits.values()),
        "generated_games": sum(item["completedGames"] for item in metadata),
        "reserved_positions": len(set(key for item in metadata for key in item["reservedKeys"])),
        "device": torch.cuda.get_device_name(0) if args.device == "cuda" else "CPU", "torch": torch.__version__,
        "hip": torch.version.hip, "training_seconds": seconds,
        "training_image": os.environ.get("LINKX_TRAINING_IMAGE"),
        "weights_sha256": hashlib.sha256((args.output / "weights.pt").read_bytes()).hexdigest(),
        "partitions": {name: {"opening_keys": sorted({row["opening_key"] for row in items}),
                              "positions_sha256": hashlib.sha256("\n".join(sorted(row["position_key"] for row in items)).encode()).hexdigest()}
                       for name, items in splits.items()},
        "datasets": [{"file": path.name, "sha256": hashlib.sha256(path.read_bytes()).hexdigest()} for path in sorted(args.data)],
        "trainer_sources": {name: hashlib.sha256(Path(__file__).with_name(name).read_bytes()).hexdigest()
                            for name in ("train.py", "model.py", "data.py", "nnue.py")},
        "generation": [{key: item[key] for key in ("seed", "maxNodes", "completedGames", "samples", "elapsedSeconds")} for item in metadata],
        "promotion": "Not approved for tournament deployment; validation error is not an Elo measurement.",
    }
    if args.nnue_width:
        manifest['quantization'] = {
            'format': 'LXNNU001, little-endian, feature-major int16 transform weights, int32 biases',
            'transform_scale': 256, 'activation_scale': 256, 'dense_weight_scale': 64,
            'dense_bias_scale': 16384, 'rounding': 'nearest, ties to even',
            'training': 'clipping and straight-through fake quantization at every integer rounding boundary',
        }
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n")
    (args.output / "metrics.json").write_text(json.dumps(metrics, indent=2) + "\n")
    (args.output / "history.json").write_text(json.dumps(result["history"], indent=2) + "\n")
    print(json.dumps(metrics, indent=2), flush=True)


if __name__ == "__main__":
    main()
