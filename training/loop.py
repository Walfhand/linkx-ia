"""Bounded collect -> relabel -> train -> play -> promote iterations. Run from the repository root."""
import argparse
import hashlib
import json
import math
import os
from pathlib import Path
import subprocess
import sys

from data import load_corpus, split_samples

IMAGE = 'rocm/pytorch@sha256:96a2fb24dec9896e2f8238178f0c49d0dcc4c7dcc597be09e4564316bd86d191'


def digest(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def write_json(path, value):
    path = Path(path)
    temporary = path.with_suffix(path.suffix + '.tmp')
    temporary.write_text(json.dumps(value, indent=2) + '\n')
    temporary.replace(path)


def paired_lower_bound(games):
    pairs = {}
    for game in games:
        key = (game['opening'], game['initialRecord'])
        colors = pairs.setdefault(key, {})
        if game['color'] not in ('blue', 'white') or game['color'] in colors or game['result'] not in (0, 0.5, 1):
            raise ValueError('Invalid or duplicate paired game')
        colors[game['color']] = game['result']
    if not pairs or any(set(colors) != {'blue', 'white'} for colors in pairs.values()):
        raise ValueError('Every opening needs both colors')
    score = sum(sum(colors.values()) / 2 for colors in pairs.values()) / len(pairs)
    # One-sided Hoeffding bound for independent opening-pair scores bounded in [0, 1].
    margin = math.sqrt(math.log(20) / (2 * len(pairs)))
    return {'games': len(games), 'pairs': len(pairs), 'score': score, 'lower95': max(0, score - margin),
            'method': 'one-sided Hoeffding bound over opening pairs; assumes independent sampled starts'}


def promotion_decision(parent_duel, candidate_teacher, parent_teacher):
    signatures = [{(g['opening'], g['initialRecord'], g['color']) for g in games}
                  for games in (parent_duel, candidate_teacher, parent_teacher)]
    if signatures[0] != signatures[1] or signatures[0] != signatures[2]:
        raise ValueError('Promotion comparisons must use identical paired openings')
    parent = paired_lower_bound(parent_duel)
    candidate_ref = paired_lower_bound(candidate_teacher)
    parent_ref = paired_lower_bound(parent_teacher)
    evidence = parent['pairs'] >= 64 and parent['lower95'] > 0.5
    no_observed_regression = candidate_ref['score'] >= parent_ref['score']
    return {'promoted': evidence and no_observed_regression, 'vs_parent': parent,
            'vs_teacher': candidate_ref, 'parent_vs_teacher': parent_ref,
            'sufficient_parent_evidence': evidence, 'no_observed_teacher_regression': no_observed_regression}


def confirmed_state(state, decision, candidate, expected_parent):
    if state['incumbent'] != expected_parent:
        raise ValueError('The incumbent changed during confirmation')
    if decision['promoted'] and (not decision.get('exact_cases_passed') or decision.get('promotion_overlap') != 0):
        raise ValueError('Promotion requires exact-case and leakage checks')
    return {**state, 'incumbent': candidate if decision['promoted'] else state['incumbent'],
            'confirmations': [*state.get('confirmations', []), decision]}


def read_matches(path):
    rows = [json.loads(line) for line in Path(path).read_text().splitlines()]
    if not rows or rows[-1].get('type') != 'summary':
        raise ValueError(f'Incomplete match report: {path}')
    games = [row for row in rows if row.get('type') == 'game']
    paired_lower_bound(games)
    return games


def run(command, log):
    print('Running:', ' '.join(map(str, command)), flush=True)
    with Path(log).open('w') as stream:
        subprocess.run(list(map(str, command)), stdout=stream, stderr=subprocess.STDOUT, check=True)


def replay_inputs(state_path):
    state_path = Path(state_path)
    previous = json.loads(state_path.read_text())
    if not previous.get('iterations'):
        raise ValueError('Replay requires a completed iteration')
    report = json.loads((state_path.parent / f"iteration-{len(previous['iterations'])}" / 'data-report.json').read_text())
    expected = {item['file']: item['sha256'] for item in report['datasets']}
    paths = previous.get('replay', [])
    if not paths or any(expected.get(Path(path).name) != digest(path) for path in paths):
        raise ValueError('Previous replay data is missing or modified')
    return paths


def prepare_exclusions(parent, output):
    manifest = json.loads((parent / 'manifest.json').read_text())
    if manifest.get('format') != 'linkx-nnue-v1' or manifest.get('architecture') != [294, 512, 1024, 32, 1]:
        raise ValueError('This loop requires a two-perspective NNUE with width 512')
    paths = [Path('training/data', item['file']) for item in manifest['datasets']]
    for path, info in zip(paths, manifest['datasets']):
        if path.name != info['file'] or digest(path) != info['sha256']:
            raise ValueError('Parent replay data is missing or modified')
    rows, _ = load_corpus(paths)
    split_seed = manifest.get('split_seed', manifest['seed'])
    splits = split_samples(rows, seed=split_seed)
    for name, items in splits.items():
        checksum = hashlib.sha256('\n'.join(sorted(row['position_key'] for row in items)).encode()).hexdigest()
        if checksum != manifest['partitions'][name]['positions_sha256']:
            raise ValueError(f'The frozen {name} partition cannot be reconstructed')
    config = {'trainingOpeningKeys': manifest['partitions']['train']['opening_keys'],
              'excludedKeys': sorted(row['position_key'] for name in ('validation', 'test') for row in splits[name]),
              'splitSeed': split_seed, 'parentPartitionHashes': manifest['partitions']}
    if output.exists():
        if json.loads(output.read_text()) != config:
            raise ValueError('Existing exclusions disagree with the parent model')
    else:
        write_json(output, config)
    return paths, config


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--reference', type=Path, required=True)
    parser.add_argument('--parent', type=Path, default=Path('models/nnue-v1/h512'))
    parser.add_argument('--output', type=Path, default=Path('training/runs/loop-v1'))
    parser.add_argument('--iterations', type=int, default=1)
    parser.add_argument('--games', type=int, default=1000)
    parser.add_argument('--shards', type=int, default=4)
    parser.add_argument('--actor-nodes', type=int, default=5000)
    parser.add_argument('--teacher-nodes', type=int, default=200000)
    parser.add_argument('--samples', type=int, default=2)
    parser.add_argument('--match-budget', type=int, default=100)
    parser.add_argument('--sample-pool', type=int)
    parser.add_argument('--screen-nodes', type=int, default=5000)
    parser.add_argument('--generation-seed', type=int, default=64101)
    parser.add_argument('--replay-state', type=Path)
    args = parser.parse_args()
    if min(args.iterations, args.games, args.shards, args.actor_nodes, args.teacher_nodes, args.match_budget) <= 0 or args.shards > args.games:
        raise ValueError('Positive budgets and no more shards than games are required')
    if min(args.actor_nodes, args.teacher_nodes) < 500 or not 0 <= args.samples <= 32:
        raise ValueError('At least 500 search nodes and 0..32 samples are required')
    pool = args.samples if args.sample_pool is None else args.sample_pool
    if not args.samples <= pool <= 32 or args.screen_nodes < 500 or (pool > args.samples and args.screen_nodes > args.teacher_nodes):
        raise ValueError('The sample pool must cover the retained samples and screening must be cheaper than labelling')
    args.parent = args.parent.resolve().relative_to(Path.cwd())
    args.output = args.output.resolve().relative_to(Path.cwd())  # GPU container mounts this workspace only.
    args.output.mkdir(parents=True, exist_ok=True)
    config = {key: str(value) if isinstance(value, Path) else value for key, value in vars(args).items() if key != 'iterations'}
    config['parent_model_sha256'] = digest(args.parent / 'model.nnue')
    config_path = args.output / 'config.json'
    if config_path.exists():
        existing = json.loads(config_path.read_text())
        for key, default in {'sample_pool': None, 'screen_nodes': 5000, 'generation_seed': 64101, 'replay_state': None}.items():
            existing.setdefault(key, default)
        if existing != config:
            raise ValueError('This loop directory belongs to a different configuration')
    write_json(config_path, config)
    state_path = args.output / 'state.json'
    state = json.loads(state_path.read_text()) if state_path.exists() else {
        'incumbent': str(args.parent), 'iterations': [], 'replay': replay_inputs(args.replay_state) if args.replay_state else []}
    for iteration in range(len(state['iterations']) + 1, args.iterations + 1):
        parent = Path(state['incumbent'])
        folder = args.output / f'iteration-{iteration}'
        folder.mkdir(exist_ok=True)
        exclusion_path = args.output / ('exclusions.json' if iteration == 1 else f'exclusions-{iteration}.json')
        base_paths, frozen = prepare_exclusions(parent, exclusion_path)
        jobs, shards = [], []
        for shard in range(args.shards):
            seed = args.generation_seed + (iteration - 1) * 1000 + shard
            data = Path('training/data', f'{args.output.name}-{seed}.jsonl')
            shards.append(data)
            info_path = Path(str(data) + '.meta.json')
            count = args.games // args.shards + int(shard < args.games % args.shards)
            if info_path.exists():
                info = json.loads(info_path.read_text())
                if (info['parentModelSha256'] != digest(parent / 'model.nnue') or info['maxNodes'] != args.teacher_nodes
                        or info['actorNodes'] != args.actor_nodes or info['completedGames'] != count
                        or info['sampleCount'] != args.samples or info.get('samplePool', info['sampleCount']) != pool
                        or (pool > args.samples and info.get('screenNodes') != args.screen_nodes)
                        or info['excludedTrainingKeysSha256'] != digest(exclusion_path)):
                    raise ValueError(f'Incompatible existing shard: {data}')
                continue
            if data.exists():
                raise ValueError(f'Incomplete shard retained for inspection: {data}')
            log = (args.output / f'generate-{seed}.log').open('w')
            command = ['node', 'training/selfplay.mjs', '--reference', args.reference, '--model', parent / 'model.nnue',
                       '--exclusions', exclusion_path, '--games', count, '--seed', seed, '--actor-nodes', args.actor_nodes,
                       '--teacher-nodes', args.teacher_nodes, '--samples', args.samples, '--output', data]
            if args.sample_pool is not None:
                command.extend(['--sample-pool', args.sample_pool, '--screen-nodes', args.screen_nodes])
            jobs.append((subprocess.Popen(list(map(str, command)), stdout=log, stderr=subprocess.STDOUT), log))
        failed = False
        for job, log in jobs:
            failed |= job.wait() != 0
            log.close()
        if failed:
            raise RuntimeError('A generation shard failed; inspect its log')
        replay = sorted({*map(str, base_paths), *state['replay'], *map(str, shards)})
        rows, metadata = load_corpus(replay)
        splits = split_samples(rows, seed=frozen['splitSeed'], prefer_stronger=True)
        for name in ('validation', 'test'):
            checksum = hashlib.sha256('\n'.join(sorted(row['position_key'] for row in splits[name])).encode()).hexdigest()
            if checksum != frozen['parentPartitionHashes'][name]['positions_sha256']:
                raise ValueError('Validation/test positions changed during data aggregation')
        write_json(folder / 'data-report.json', {'raw_positions': len(rows), 'partitions': {name: len(items) for name, items in splits.items()},
            'new_games': sum(json.loads(Path(str(path) + '.meta.json').read_text())['completedGames'] for path in shards),
            'frozen_split_seed': frozen['splitSeed'], 'datasets': [{'file': Path(path).name, 'sha256': digest(path)} for path in replay]})
        candidates = []
        for seed in (42, 43, 44):
            output = folder / f'seed-{seed}'
            if not (output / 'metrics.json').exists():
                run(['docker', 'run', '--rm', '--device=/dev/kfd', '--device=/dev/dri', '--user', f'{os.getuid()}:{os.getgid()}',
                     '--mount', f'type=bind,source={Path.cwd()},target=/workspace', '--workdir', '/workspace',
                     '--env', f'LINKX_TRAINING_IMAGE={IMAGE}', IMAGE, 'python3', 'training/train.py', '--data', *replay,
                     '--output', output, '--device', 'cuda', '--nnue-width', '512', '--epochs', '200', '--seed', seed,
                     '--split-seed', frozen['splitSeed'], '--resume-weights', parent / 'weights.pt', '--prefer-stronger-labels'],
                    folder / f'train-{seed}.log')
            if not (output / 'export.json').exists():
                run([sys.executable, 'training/check-nnue.py', output], folder / f'export-{seed}.log')
            candidates.append(output)
        # Evaluation and promotion are delegated to the same recorded match harness used by the diagnostic.
        run(['node', 'training/evaluate-loop.mjs', '--reference', args.reference, '--parent', parent,
             '--candidates', ','.join(map(str, candidates)), '--output', folder, '--budget', args.match_budget,
             '--seed', 73200 + iteration], folder / 'evaluation.log')
        selection = json.loads((folder / 'selection.json').read_text())
        if selection['identical_to_parent']:
            decision = {'promoted': False, 'reason': 'All selected weights equal the parent checkpoint.'}
        else:
            decision = promotion_decision(read_matches(folder / 'candidate-parent.jsonl'),
                                          read_matches(folder / 'candidate-teacher.jsonl'), read_matches(folder / 'parent-teacher.jsonl'))
            decision['exact_cases_passed'] = selection['exact_cases_passed']
            decision['promotion_overlap'] = selection['promotion_overlap']
            decision['promoted'] &= selection['exact_cases_passed'] and selection['promotion_overlap'] == 0
        decision.update(iteration=iteration, parent=str(parent), selected=selection['candidate'], selection=selection)
        write_json(folder / 'decision.json', decision)
        if decision['promoted']:
            state['incumbent'] = selection['candidate']
        state['replay'] = replay
        state['iterations'].append(decision)
        write_json(state_path, state)
        print(json.dumps({'iteration': iteration, 'promoted': decision['promoted'], 'incumbent': state['incumbent']}, indent=2), flush=True)


if __name__ == '__main__':
    main()
