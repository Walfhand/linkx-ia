import copy
import json
import sys

import torch
from torch import nn


class ValueNet(nn.Module):
    def __init__(self):
        super().__init__()
        self.layers = nn.Sequential(nn.Linear(176, 128), nn.ReLU(), nn.Linear(128, 32), nn.ReLU(), nn.Linear(32, 1), nn.Tanh())

    def forward(self, features):
        return self.layers(features)


def fit(model, features, labels, validation, expected, epochs=200, batch_size=256, seed=42, keep_initial=False):
    if min(len(features), len(validation), epochs, batch_size) <= 0:
        raise ValueError("Training and validation data, epochs and batch size must be positive")
    optimizer = torch.optim.AdamW(model.parameters(), lr=0.001, weight_decay=0.0001)
    random = torch.Generator(device=features.device).manual_seed(seed)
    model.eval()
    with torch.no_grad():
        initial_loss = nn.functional.mse_loss(model(validation), expected).item()
    best_loss, best_epoch = (initial_loss if keep_initial else float("inf")), 0
    best_weights = copy.deepcopy(model.state_dict()) if keep_initial else None
    history = []
    for epoch in range(1, epochs + 1):
        model.train()
        order = torch.randperm(len(features), generator=random, device=features.device)
        total = 0.0
        for indices in order.split(batch_size):
            optimizer.zero_grad(set_to_none=True)
            loss = nn.functional.mse_loss(model(features[indices]), labels[indices])
            if not torch.isfinite(loss):
                raise ValueError("Training diverged to a non-finite loss")
            loss.backward()
            optimizer.step()
            total += loss.item() * len(indices)
        model.eval()
        with torch.no_grad():
            validation_loss = nn.functional.mse_loss(model(validation), expected).item()
        history.append({"epoch": epoch, "train_mse": total / len(features), "validation_mse": validation_loss})
        if validation_loss < best_loss:
            best_loss, best_epoch = validation_loss, epoch
            best_weights = copy.deepcopy(model.state_dict())
        if epoch % 10 == 0:
            print(json.dumps(history[-1]), file=sys.stderr, flush=True)
        if epoch - best_epoch >= 20:
            break
    model.load_state_dict(best_weights)
    model.eval()
    return {"best_epoch": best_epoch, "history": history, "initial_validation_mse": initial_loss}
