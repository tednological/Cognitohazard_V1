"""Compare two complete render outputs, including manifest and PNG hashes."""
import argparse
import hashlib
import json
from pathlib import Path


def compare(first, second):
    manifests = [json.loads((root / 'manifest.json').read_text())
                 for root in (first, second)]
    if manifests[0] != manifests[1]:
        raise SystemExit('FAIL: manifests differ')
    expected = {entry['path'] for entry in manifests[0]}
    if not expected:
        raise SystemExit('FAIL: empty manifest')
    for root in (first, second):
        actual = {path.relative_to(root).as_posix() for path in root.rglob('*.png')}
        # The map kits have their own manifests and reproducibility command.
        # Exempt only registered, fully validated companion-kit files.
        if (root / 'tilesets').exists():
            from check_tilesets import validate
            supplemental = validate(root / 'tilesets')['sha256']
            actual -= {'tilesets/' + path for path in supplemental}
        if actual != expected:
            raise SystemExit(f'FAIL: PNG inventory differs from manifest: {root}')
    hashes = {}
    for path in sorted(expected):
        pair = [hashlib.sha256((root / path).read_bytes()).hexdigest()
                for root in (first, second)]
        if pair[0] != pair[1]:
            raise SystemExit(f'FAIL: PNG bytes differ: {path}')
        hashes[path] = pair[0]
    return hashes


if __name__ == '__main__':
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('first', type=Path)
    parser.add_argument('second', type=Path)
    parser.add_argument('--report', type=Path)
    args = parser.parse_args()
    hashes = compare(args.first, args.second)
    if args.report:
        args.report.write_text(json.dumps({'png_count': len(hashes),
            'manifests_equal': True, 'sha256': hashes}, indent=2) + '\n')
    print(f'PASS: {len(hashes)} PNGs byte-identical; manifests identical.')
