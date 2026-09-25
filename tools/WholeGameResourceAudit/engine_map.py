"""Index the loaded game's contiguous translated-function registration table."""
import argparse
import hashlib
import json
from pathlib import Path
import struct


def extract(image, anchor, guest_ranges, host_ranges):
    def read(offset):
        return struct.unpack_from('<QQ', image, offset)

    def valid(offset):
        if offset < 0 or offset + 16 > len(image):
            return False
        guest, host = read(offset)
        return any(lo <= guest < hi for lo, hi in guest_ranges) and any(lo <= host < hi for lo, hi in host_ranges)

    if anchor % 16 or not valid(anchor):
        raise ValueError('anchor does not identify an executable-to-executable registration')
    start = last = anchor
    while valid(start - 16):
        start -= 16
    while valid(last + 16):
        last += 16
    rows = [{'recordRva': offset, 'legacyAddress': read(offset)[0], 'hostAddress': read(offset)[1]}
            for offset in range(start, last + 16, 16)]
    return {'tableStartRva': start, 'tableEndRvaExclusive': last + 16, 'records': rows,
            'boundaryBefore': list(read(start - 16)) if start >= 16 else None,
            'boundaryAfter': list(read(last + 16)) if last + 32 <= len(image) else None}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('private_research_root', type=Path)
    args = parser.parse_args()
    root = args.private_research_root
    capture = json.loads((root / 'inputs/steam2026-loaded-capture.json').read_text(encoding='utf-8-sig'))
    image = (root / 'inputs/steam2026-loaded-analysis.pe').read_bytes()
    digest = hashlib.sha256(image).hexdigest()
    if digest.lower() != capture['OutputSha256'].lower():
        raise ValueError('capture image hash mismatch')
    base = int(capture['LoadedBase'], 16)
    blocks = json.loads((root / 'engine/legacy/memory-blocks.json').read_text())
    guest_ranges = [(int(b['start'], 16), int(b['end'], 16) + 1) for b in blocks if b['execute'] and b['initialized']]
    host_ranges = [(base + b['Rva'], base + b['Rva'] + b['VirtualBytes']) for b in capture['Sections'] if b['Name'] == '.text' and b['UnreadablePages'] == 0]
    anchor = 0x16ea2e0
    if struct.unpack_from('<QQ', image, anchor) != (0x6123e2, base + 0xbb0c30):
        raise ValueError('established native field REQ mapping differs')
    report = extract(image, anchor, guest_ranges, host_ranges)
    report.update(captureSha256=digest, imageBase=base, anchorRva=anchor,
                  evidence='Contiguous registrations surrounding the independently validated field REQ entry; every guest and host address lies in initialized executable memory.')
    # A running export truncates/rebuilds its JSONL index. Never compare against
    # a partial index and misreport the unfinished export as thousands of gaps.
    summary = json.loads((root / 'engine/legacy/summary.json').read_text())
    index = [json.loads(line) for line in (root / 'engine/legacy/functions.jsonl').read_text().splitlines()]
    if len(index) != summary['processed']:
        raise ValueError('legacy export is incomplete or still being regenerated')
    entries = {int(row['entry'], 16) for row in index}
    for row in report['records']:
        row['hostRva'] = row['hostAddress'] - base
    report['legacyFunctionEntriesNotYetDiscovered'] = [row for row in report['records'] if row['legacyAddress'] not in entries]
    output = root / 'reports/translated-function-map.json'
    output.write_text(json.dumps(report, separators=(',', ':')) + '\n', encoding='utf-8')
    print(json.dumps({'output': str(output), 'records': len(report['records']),
                      'legacyEntriesToReview': len(report['legacyFunctionEntriesNotYetDiscovered'])}))


if __name__ == '__main__':
    main()
