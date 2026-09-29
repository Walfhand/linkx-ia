import copy
import json
import tempfile
import unittest
from pathlib import Path

from data import canonical_key, encode, load_corpus, split_samples, target, validate_sample


def sample(game="game-1", opening="opening-1", index=80):
    row = {
        "game_id": game, "opening_key": opening,
        "board": "." * index + "B" + "." * (80 - index),
        "active_player": "white",
        "inventories": {"blue": [1, 2, 2, 2, 2, 2, 2], "white": [2] * 7},
        "score": 1000, "exact": False, "outcome": -1, "depth": 3, "nodes": 1000,
    }
    row["position_key"] = canonical_key(row)
    return row


class DataTests(unittest.TestCase):
    def test_loading_rejects_reserved_validation_positions(self):
        row = sample()
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "data.jsonl"
            path.write_text(json.dumps(row) + "\n")
            metadata = {"schema": 1, "referenceCommit": "a" * 40, "samples": 1,
                        "shapes": ["mono", "domino", "bar3", "smallL", "s", "t", "largeL"],
                        "reservedKeys": [row["position_key"]]}
            Path(str(path) + ".meta.json").write_text(json.dumps(metadata))
            with self.assertRaises(ValueError):
                load_corpus([path])

    def test_encoding_uses_side_to_move_and_both_reserves(self):
        features = encode(sample())
        self.assertEqual(len(features), 176)
        self.assertEqual(sum(features[:81]), 0)
        self.assertEqual(features[81 + 80], 1)
        self.assertEqual(features[162:169], [1.0] * 7)
        self.assertEqual(features[169:], [0.5] + [1.0] * 6)

    def test_color_swap_preserves_encoding_and_key(self):
        first = sample()
        second = copy.deepcopy(first)
        second["board"] = second["board"].translate(str.maketrans("BW", "WB"))
        second["active_player"] = "blue"
        second["inventories"] = {"blue": first["inventories"]["white"], "white": first["inventories"]["blue"]}
        self.assertEqual(encode(first), encode(second))
        self.assertEqual(canonical_key(first), canonical_key(second))

    def test_horizontal_mirrors_share_a_key_but_vertical_flips_do_not(self):
        self.assertEqual(canonical_key(sample(index=72)), canonical_key(sample(index=80)))
        self.assertNotEqual(canonical_key(sample(index=0)), canonical_key(sample(index=72)))
        self.assertEqual(encode(sample(index=72), mirror=True), encode(sample(index=80)))

    def test_proven_targets_ignore_a_later_self_play_blunder(self):
        row = sample()
        row.update(exact=True, score=1_000_000, outcome=-1)
        self.assertEqual(target(row), 1.0)
        row.update(score=0, outcome=1)
        self.assertEqual(target(row), 0.0)

    def test_validation_rejects_corrupt_features_and_labels(self):
        for field, value in [("board", "..."), ("score", float("nan")), ("active_player", "red"), ("depth", 0), ("outcome", 2)]:
            row = sample()
            row[field] = value
            with self.subTest(field=field), self.assertRaises(ValueError):
                validate_sample(row)
        row = sample()
        row["inventories"]["blue"][0] = 3
        with self.assertRaises(ValueError):
            validate_sample(row)

    def test_split_keeps_openings_games_and_mirrored_positions_disjoint(self):
        rows = [sample(f"game-{i}", f"opening-{i // 3}", index=(i * 7) % 81) for i in range(30)]
        rows.append(copy.deepcopy(rows[0]))
        splits = split_samples(rows, seed=42)
        self.assertEqual(splits, split_samples(list(reversed(rows)), seed=42))
        for name, items in splits.items():
            self.assertTrue(items, name)
            for other, other_items in splits.items():
                if name == other:
                    continue
                for field in ("opening_key", "game_id", "position_key"):
                    self.assertTrue({row[field] for row in items}.isdisjoint(row[field] for row in other_items), field)

    def test_split_rejects_a_game_assigned_to_two_openings(self):
        rows = [sample("same-game", "one"), sample("same-game", "two", 71)]
        with self.assertRaises(ValueError):
            split_samples(rows)


if __name__ == "__main__":
    unittest.main()
