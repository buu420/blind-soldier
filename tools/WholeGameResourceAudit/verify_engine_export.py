"""Check a completed private Ghidra export, preserving degraded-code evidence."""
import argparse
import hashlib
import json
from pathlib import Path
import re


def verify(root, binary, log):
    root = root.resolve()
    summary = json.loads((root / 'summary.json').read_text(encoding='utf-8-sig'))
    identity = json.loads((root / 'identity.json').read_text(encoding='utf-8-sig'))
    digest = hashlib.sha256(binary.read_bytes()).hexdigest()
    errors = []
    if digest.lower() != identity['sha256'].lower() or digest.lower() != summary['sha256'].lower():
        errors.append('source binary identity mismatch')
    rows = [json.loads(line) for line in (root / 'functions.jsonl').read_text().splitlines()]
    # Enumerate once. Repeated Path.resolve/is_file on tens of thousands of UNC
    # artifacts adds many unnecessary network round trips. Expected names are
    # constructed from validated hexadecimal entries, never arbitrary paths.
    files = {p.relative_to(root).as_posix(): p for p in (root / 'functions').glob('*/*')}
    if len(rows) != summary['processed'] or len({r['entry'] for r in rows}) != len(rows):
        errors.append('function index count mismatch or duplicate entries')
    failures, warnings, truncated = [], [], []
    for row in rows:
        entry = row['entry']
        if not re.fullmatch('[0-9a-fA-F]{8,16}', entry):
            errors.append(f'invalid internal entry: {entry}')
            continue
        prefix = f'functions/{entry[:4]}/{entry}'
        metadata_path = files.get(prefix + '.json')
        if metadata_path is None:
            errors.append(f'metadata missing: {entry}')
            continue
        metadata = json.loads(metadata_path.read_text())
        if any(metadata.get(k) != row.get(k) for k in ('entry', 'bytes', 'prototype', 'decompileCompleted')):
            errors.append(f'stale metadata/index disagreement: {entry}')
        for kind in ('assemblyPath', 'cPath'):
            if kind == 'cPath' and not row['decompileCompleted']:
                continue
            relative = row.get(kind)
            expected = prefix + ('.c' if kind == 'cPath' else '.asm')
            path = files.get(expected)
            if not relative or Path(relative).as_posix() != expected or path is None:
                errors.append(f'missing or invalid {kind}: {entry}')
                continue
            if kind == 'cPath':
                code = path.read_text()
                if not code.strip():
                    errors.append(f'empty C output: {entry}')
                if 'WARNING:' in code:
                    warnings.append(entry)
                if any(x in code for x in ('halt_baddata', 'Bad instruction', 'Unable to resolve')):
                    truncated.append(entry)
        if not row['decompileCompleted']:
            failures.append({'entry': entry, 'error': row.get('error')})
    if len(failures) != summary['failed'] or len(rows) - len(failures) != summary['succeeded']:
        errors.append('success/failure summary count mismatch')
    external_path = root / 'external-functions.jsonl'
    external_count = len(external_path.read_text().splitlines()) if external_path.is_file() else None
    if external_count is not None and summary.get('externalFunctionCount') != external_count:
        errors.append('external import count mismatch')
    text = log.read_text(errors='replace')
    pcode = sorted(set(re.findall(r'WARN\s+Decompiling ([0-9a-f]+), pcode error', text)))
    return {'sha256': digest, 'structuralErrors': errors, 'functions': len(rows), 'externalFunctions': external_count,
            'hardFailures': failures, 'functionsWithWarnings': warnings, 'functionsWithTruncatedControlFlow': truncated,
            'functionsWithPcodeErrors': pcode, 'executableBytes': summary['executableBytes'],
            'executableBytesOutsideFunctions': summary['executableBytesOutsideFunctions'],
            'claimBoundary': 'Artifact integrity and approximate decompiler output only. Warnings, unassigned bytes, and runtime correctness require separate review.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ('export_root', 'binary', 'log', 'report'):
        parser.add_argument(name, type=Path)
    args = parser.parse_args()
    result = verify(args.export_root, args.binary, args.log)
    args.report.write_text(json.dumps(result, separators=(',', ':')) + '\n', encoding='utf-8')
    print(json.dumps({k: len(v) if isinstance(v, list) else v for k, v in result.items()}))
    raise SystemExit(1 if result['structuralErrors'] else 0)


if __name__ == '__main__':
    main()
