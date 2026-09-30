import unittest
import json
import tempfile
import hashlib
from pathlib import Path

from loop import paired_lower_bound, promotion_decision, prepare_exclusions, replay_inputs, confirmed_state


def games(scores):
    return [dict(opening=index, color=color, result=result, initialRecord=str(index))
            for index, pair in enumerate(scores) for color, result in zip(('blue', 'white'), pair)]


class LoopTests(unittest.TestCase):
    def test_confirmation_preserves_history_and_cannot_replace_a_different_incumbent(self):
        state = {'incumbent': 'parent', 'iterations': [{'promoted': False}], 'replay': ['experience']}
        failed = {'promoted': False, 'exact_cases_passed': True, 'promotion_overlap': 0}
        retained = confirmed_state(state, failed, 'candidate', 'parent')
        self.assertEqual(retained['incumbent'], 'parent')
        passed = {**failed, 'promoted': True}
        promoted = confirmed_state(state, passed, 'candidate', 'parent')
        self.assertEqual(promoted['incumbent'], 'candidate')
        self.assertEqual(state['incumbent'], 'parent')
        self.assertEqual(promoted['iterations'], state['iterations'])
        self.assertEqual(promoted['replay'], state['replay'])
        with self.assertRaises(ValueError): confirmed_state(state, passed, 'candidate', 'another-parent')
        with self.assertRaises(ValueError): confirmed_state(state, {**passed, 'promotion_overlap': 1}, 'candidate', 'parent')

    def test_replay_carries_completed_experience_and_rejects_modified_data(self):
        with tempfile.TemporaryDirectory() as directory:
            folder = Path(directory)
            data = folder / 'samples.jsonl'
            data.write_text('retained experience\n')
            (folder / 'iteration-1').mkdir()
            (folder / 'iteration-1/data-report.json').write_text(json.dumps({'datasets': [
                {'file': data.name, 'sha256': hashlib.sha256(data.read_bytes()).hexdigest()}]}))
            state = folder / 'state.json'
            state.write_text(json.dumps({'iterations': [{'promoted': False}], 'replay': [str(data)]}))
            self.assertEqual(replay_inputs(state), [str(data)])
            data.write_text('changed\n')
            with self.assertRaises(ValueError): replay_inputs(state)

    def test_unsupported_parent_is_rejected_before_generating_data(self):
        with tempfile.TemporaryDirectory() as directory:
            parent = Path(directory)
            (parent / 'manifest.json').write_text(json.dumps({'format': 'linkx-value-v1', 'architecture': [176, 128, 32, 1]}))
            with self.assertRaisesRegex(ValueError, '512'):
                prepare_exclusions(parent, parent / 'exclusions.json')

    def test_gate_uses_opening_pairs_and_rejects_small_or_inconclusive_improvements(self):
        parent = games([(1, 0)] * 64)
        marginal = games([(1, 1)] * 10 + [(1, 0)] * 54)
        self.assertEqual(paired_lower_bound(parent)['score'], 0.5)
        self.assertFalse(promotion_decision(marginal, parent, parent)['promoted'])
        self.assertFalse(promotion_decision(games([(1, 1)] * 4), games([(1, 0)] * 4), games([(1, 0)] * 4))['promoted'])

    def test_gate_requires_evidence_against_parent_and_no_observed_teacher_regression(self):
        strong = games([(1, 1)] * 32 + [(1, 0)] * 32)
        teacher = games([(1, 0)] * 64)
        self.assertTrue(promotion_decision(strong, teacher, teacher)['promoted'])
        weak_against_teacher = games([(0, 0)] * 64)
        self.assertFalse(promotion_decision(strong, weak_against_teacher, teacher)['promoted'])

    def test_gate_refuses_unpaired_duplicate_or_mismatched_openings(self):
        with self.assertRaises(ValueError):
            paired_lower_bound([dict(opening=1, color='blue', result=1, initialRecord='a')])
        with self.assertRaises(ValueError):
            paired_lower_bound([dict(opening=1, color='blue', result=1, initialRecord='a')] * 2)
        first = games([(1, 1)] * 64)
        other = games([(1, 0)] * 64)
        other[0]['initialRecord'] = 'different-start'
        with self.assertRaises(ValueError):
            promotion_decision(first, other, first)


if __name__ == '__main__':
    unittest.main()
