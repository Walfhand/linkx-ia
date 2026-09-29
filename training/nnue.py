"""Shared two-perspective NNUE, with the CPU integer arithmetic simulated during training."""
import struct
from pathlib import Path

import numpy as np
import torch
from torch import nn
from torch.nn import functional as F

from data import validate_sample


def encode_nnue(row, mirror=False):
    validate_sample(row)
    board = row['board']
    if mirror:
        board = ''.join(board[y:y + 9][::-1] for y in range(0, 81, 9))
    own = row['active_player']
    other = 'white' if own == 'blue' else 'blue'
    heights = [next((9 - y for y in range(9) if board[y * 9 + x] != '.'), 0) for x in range(9)]
    features = np.zeros((2, 294), dtype=np.float32)
    for perspective, players in enumerate(((own, other), (other, own))):
        for side, player in enumerate(players):
            symbol = 'B' if player == 'blue' else 'W'
            for index, cell in enumerate(board):
                features[perspective, side * 81 + index] = cell == symbol
            for shape, count in enumerate(row['inventories'][player]):
                features[perspective, 162 + side * 21 + shape * 3 + count] = 1
        for column, height in enumerate(heights):
            features[perspective, 204 + column * 10 + height] = 1
    return features


def quantize(value, scale, bound=None):
    if bound is not None:
        value = value.clamp(-bound, bound)
    rounded = (value * scale).round() / scale
    # Straight-through gradient; the forward pass uses exactly representable integer scales.
    return value + (rounded - value).detach()


class Nnue(nn.Module):
    def __init__(self, width=256):
        super().__init__()
        if width < 8 or width > 1024 or width % 8:
            raise ValueError('NNUE width must be a multiple of eight in 8..1024')
        self.width = width
        self.transform = nn.Linear(294, width)
        self.hidden = nn.Linear(width * 2, 32)
        self.output = nn.Linear(32, 1)

    def forward(self, features):
        accumulators = F.linear(features, quantize(self.transform.weight, 256, 4),
                                quantize(self.transform.bias, 256, 8)).clamp(0, 1)
        hidden = F.linear(accumulators.flatten(1), quantize(self.hidden.weight, 64, 2),
                          quantize(self.hidden.bias, 16384, 8))
        hidden = quantize(hidden.clamp(0, 1), 256)
        return torch.tanh(F.linear(hidden, quantize(self.output.weight, 64, 2),
                                  quantize(self.output.bias, 16384, 8)))


def export_nnue(model, path):
    if any(not torch.isfinite(parameter).all() for parameter in model.parameters()):
        raise ValueError('Cannot export non-finite NNUE parameters')
    data = [struct.pack('<8sIII', b'LXNNU001', model.width, 294, 32)]
    for layer, scale, bound in [(model.transform, 256, 4), (model.hidden, 64, 2), (model.output, 64, 2)]:
        weights = layer.weight.detach().cpu().clamp(-bound, bound).mul(scale).round().numpy()
        if layer is model.transform:
            weights = weights.T  # Feature-major rows for incremental CPU updates.
        bias_scale = 256 if layer is model.transform else 16384
        biases = layer.bias.detach().cpu().clamp(-8, 8).mul(bias_scale).round().numpy()
        data.extend((weights.astype('<i2').tobytes(), biases.astype('<i4').tobytes()))
    Path(path).write_bytes(b''.join(data))
