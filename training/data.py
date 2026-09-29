"""Version 1 features: own cells, opponent cells, then both reserves, side-to-move first."""
import hashlib
import json
import math
from pathlib import Path


def load_corpus(paths):
    rows, metadata = [], []
    for path in sorted({Path(path) for path in paths}):
        info = json.loads(Path(str(path) + ".meta.json").read_text())
        if info.get("schema") != 1 or info.get("shapes") != ["mono", "domino", "bar3", "smallL", "s", "t", "largeL"]:
            raise ValueError("Unsupported corpus schema or shape order")
        if metadata and info["referenceCommit"] != metadata[0]["referenceCommit"]:
            raise ValueError("Corpora from different teacher versions cannot be mixed")
        reserved = set(info["reservedKeys"])
        start = len(rows)
        with path.open() as stream:
            for line in stream:
                row = json.loads(line)
                validate_sample(row)
                if row["position_key"] in reserved:
                    raise ValueError("Reserved benchmark position leaked into the training corpus")
                rows.append(row)
        if len(rows) - start != info["samples"]:
            raise ValueError("Corpus row count differs from its completion metadata")
        metadata.append(info)
    if not rows:
        raise ValueError("No training data found")
    return rows, metadata


def canonical_key(row):
    own = "B" if row["active_player"] == "blue" else "W"
    board = "".join("." if cell == "." else "1" if cell == own else "2" for cell in row["board"])
    reflected = "".join(board[index:index + 9][::-1] for index in range(0, 81, 9))
    other = "white" if row["active_player"] == "blue" else "blue"
    reserves = row["inventories"][row["active_player"]] + row["inventories"][other]
    return min(board, reflected) + "|" + "".join(str(count) for count in reserves)


def validate_sample(row):
    if not isinstance(row.get("board"), str) or len(row["board"]) != 81 or set(row["board"]) - set("BW."):
        raise ValueError("Expected 81 board cells containing B, W or .")
    if row.get("active_player") not in ("blue", "white"):
        raise ValueError("Unknown moving player")
    for player in ("blue", "white"):
        counts = row.get("inventories", {}).get(player)
        if not isinstance(counts, list) or len(counts) != 7 or any(type(value) is not int or value not in (0, 1, 2) for value in counts):
            raise ValueError("Expected seven reserve counts in 0..2 for each player")
    if type(row.get("score")) not in (int, float) or not math.isfinite(row["score"]):
        raise ValueError("Expected a finite teacher score")
    if type(row.get("exact")) is not bool or type(row.get("outcome")) is not int or row["outcome"] not in (-1, 0, 1):
        raise ValueError("Invalid outcome or proof marker")
    if type(row.get("depth")) is not int or row["depth"] < 1:
        raise ValueError("Teacher must complete at least one search iteration")
    if any(not isinstance(row.get(field), str) or not row[field] for field in ("game_id", "opening_key")):
        raise ValueError("A game and an opening group are required")
    if row.get("position_key") != canonical_key(row):
        raise ValueError("Position key disagrees with board, reserves or side to move")


def encode(row, mirror=False):
    validate_sample(row)
    board = row["board"]
    if mirror:
        board = "".join(board[index:index + 9][::-1] for index in range(0, 81, 9))
    own = row["active_player"]
    other = "white" if own == "blue" else "blue"
    symbols = ("B", "W") if own == "blue" else ("W", "B")
    return [float(cell == symbol) for symbol in symbols for cell in board] + [
        count / 2 for player in (own, other) for count in row["inventories"][player]
    ]


def target(row):
    if row["exact"]:
        return float((row["score"] > 0) - (row["score"] < 0))
    # ponytail: heuristic score calibration for the pilot; fit calibration on separate games before promotion.
    return 0.9 * math.tanh(row["score"] / 4000) + 0.1 * row["outcome"]


def split_samples(rows, seed=42):
    game_openings = {}
    for row in rows:
        validate_sample(row)
        previous = game_openings.setdefault(row["game_id"], row["opening_key"])
        if previous != row["opening_key"]:
            raise ValueError("A game cannot belong to two opening groups")
    groups = sorted(set(game_openings.values()), key=lambda group: hashlib.sha256(f"{seed}:{group}".encode()).digest())
    if len(groups) < 3:
        raise ValueError("At least three distinct opening groups are needed")
    held_out = max(1, len(groups) // 10)
    assignments = {group: "test" if index < held_out else "validation" if index < 2 * held_out else "train"
                   for index, group in enumerate(groups)}
    result = {"train": [], "validation": [], "test": []}
    seen = set()
    ordered = sorted(rows, key=lambda row: (row["opening_key"], row["game_id"], row["position_key"]))
    for partition in result:
        for row in ordered:
            if assignments[row["opening_key"]] != partition or row["position_key"] in seen:
                continue
            seen.add(row["position_key"])
            result[partition].append(row)
        if not result[partition]:
            raise ValueError(f"The {partition} partition is empty after duplicate removal")
    return result
