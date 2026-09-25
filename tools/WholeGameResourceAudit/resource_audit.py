"""Read-only inventory of FFVII world, battle and kernel script resources.

Outputs contain licensed game bytes and must stay in private research storage.
Formats: ff7-mods/ff7-flat-wiki, WorldMap_Module/Script and Battle/Battle_Scenes;
string boundaries cross-checked with Christian Bauer's ff7tools/ff7/scene.py.
No code is executed, save state is changed, or player-facing hint generated.
"""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
import struct
import zlib


def sha(data):
    return hashlib.sha256(data).hexdigest()


def u16(data, offset):
    if offset < 0 or offset + 2 > len(data):
        raise ValueError(f'uint16 outside resource at {offset:#x}')
    return struct.unpack_from('<H', data, offset)[0]


def save(path, value):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(value, ensure_ascii=False, separators=(',', ':')) + '\n', encoding='utf-8')


def lgp_entries(data):
    if len(data) < 16 or data[:12].strip(b'\0 ') != b'SQUARESOFT':
        raise ValueError('unsupported LGP header')
    count = struct.unpack_from('<I', data, 12)[0]
    if count == 0 or 16 + count * 27 > len(data):
        raise ValueError('invalid LGP table size')
    result = []
    for index in range(count):
        base = 16 + index * 27
        name = data[base:base + 20].rstrip(b'\0 ').decode('ascii')
        offset = struct.unpack_from('<I', data, base + 20)[0]
        if offset + 24 > len(data):
            raise ValueError(f'LGP entry {index} outside archive')
        stored = data[offset:offset + 20].rstrip(b'\0 ').decode('ascii')
        size = struct.unpack_from('<I', data, offset + 20)[0]
        if stored.lower() != name.lower() or offset + 24 + size > len(data):
            raise ValueError(f'LGP entry {index} mismatched header or truncated payload')
        result.append({'index': index, 'name': name, 'offset': offset + 24, 'size': size})
    return result


WORLD_KNOWN = set(int(x, 16) for x in '''
015 017 018 019 01b 030 040 041 050 051 060 061 062 063 070 080 0a0 0b0 0c0 0e0
100 110 114 117 118 119 11b 11c 11d 11f 200 201 203
300 302 303 304 305 306 307 308 309 30a 30b 30c 30d 30e 310 311 312 313 314
315 316 317 318 319 31b 31c 31d 31f 320 321 324 325 326 327 328 329 32a 32b
32c 32d 32e 32f 330 331 332 333 334 336 339 33a 33b 33c 33d 33e 347 348 349
34a 34b 34c 34d 34e 34f 350 351 352 353 354 355'''.split())


def audit_world(data):
    if len(data) < 0x400 or len(data) % 2:
        raise ValueError('world event file has a truncated header or word')
    words = struct.unpack(f'<{(len(data) - 0x400) // 2}H', data[0x400:])
    entries, instructions, issues, owners = [], {}, [], {}
    seen_issues = set()

    def issue(ip, reason):
        if (ip, reason) not in seen_issues:
            issues.append({'ip': ip, 'reason': reason})
            seen_issues.add((ip, reason))

    def decode(ip):
        if not 0 <= ip < len(words):
            issue(ip, 'entry or branch outside code')
            return None
        if ip in owners and owners[ip] != ip:
            issue(ip, 'overlapping instruction: branch or entry enters operand')
            return None
        if ip in instructions:
            return instructions[ip]
        opcode = words[ip]
        width = 2 if 0x100 < opcode < 0x200 or opcode in (0x200, 0x201) else 1
        if ip + width > len(words):
            issue(ip, 'truncated instruction operand')
            return None
        if any(x in owners and owners[x] != ip for x in range(ip, ip + width)):
            issue(ip, 'overlapping instruction ranges')
            return None
        for x in range(ip, ip + width):
            owners[x] = ip
        known = opcode in WORLD_KNOWN or 0x204 <= opcode < 0x300
        row = {'ip': ip, 'fileOffset': 0x400 + ip * 2, 'opcode': opcode,
               'words': list(words[ip:ip + width]), 'semanticsDocumented': known}
        if not known:
            issue(ip, f'undocumented opcode {opcode:#x}; successors unresolved')
            row['successors'] = []
        elif opcode == 0x203:
            row['successors'] = []
        elif opcode == 0x200:
            row['successors'] = [words[ip + 1]]
        elif opcode == 0x201:
            row['successors'] = [words[ip + 1], ip + width]
        else:
            row['successors'] = [ip + width]
        if 0x204 <= opcode < 0x300:
            row['functionCall'] = {'scriptId': opcode - 0x204, 'model': 'stack-dependent'}
        instructions[ip] = row
        return row

    for slot in range(256):
        function, entry = struct.unpack_from('<HH', data, slot * 4)
        if function == 0xffff:
            continue
        row = {'tableSlot': slot, 'functionId': function, 'entry': entry,
               'type': ('system', 'model', 'terrain', 'unknown')[function >> 14]}
        if row['type'] == 'model':
            row.update(modelId=(function >> 8) & 63, scriptId=function & 255)
        elif row['type'] == 'terrain':
            mesh = (function & 0x3ff0) >> 4
            row.update(meshX=mesh % 36, meshY=mesh // 36, terrainScriptId=(function & 15) + 3)
        visited, pending = set(), [entry]
        while pending:
            ip = pending.pop()
            if ip in visited:
                continue
            visited.add(ip)
            instruction = decode(ip)
            if instruction is not None:
                pending.extend(instruction['successors'])
        row['reachable'] = sorted(x for x in visited if x in instructions)
        entries.append(row)
    duplicates = {str(k): v for k, v in Counter(e['functionId'] for e in entries).items() if v > 1}
    return {'sha256': sha(data), 'bytes': len(data), 'entries': entries,
            'instructions': {str(k): instructions[k] for k in sorted(instructions)}, 'issues': issues,
            'duplicateFunctionIds': duplicates, 'unvisitedCodeWords': len(words) - len(owners),
            'note': 'Structural reachability only. Both conditional branches are retained; stack calls and live conditions are not evaluated. Unvisited code may be padding or unused.'}


def gunzip_one(data, max_bytes):
    decoder = zlib.decompressobj(16 + zlib.MAX_WBITS)
    result = decoder.decompress(data, max_bytes + 1)
    if len(result) > max_bytes or not decoder.eof:
        raise ValueError('gzip member truncated or exceeds expected decompressed bound')
    return result, decoder.unused_data


def unpack_scenes(data):
    if not data or len(data) % 0x2000:
        raise ValueError('scene archive is not a whole number of 8192-byte blocks')
    scenes = []
    for block_offset in range(0, len(data), 0x2000):
        block = data[block_offset:block_offset + 0x2000]
        offsets, ended = [], False
        for ptr in struct.unpack_from('<16I', block):
            if ptr == 0xffffffff:
                ended = True
            elif ended:
                raise ValueError('scene pointer follows table terminator')
            else:
                offset = ptr * 4
                if not 0x40 <= offset < 0x2000 or offsets and offset <= offsets[-1]:
                    raise ValueError('invalid scene pointer order or bounds')
                offsets.append(offset)
        for i, start in enumerate(offsets):
            end = offsets[i + 1] if i + 1 < len(offsets) else 0x2000
            payload, padding = gunzip_one(block[start:end], 0x1e80)
            if len(payload) != 0x1e80 or any(x != 255 for x in padding):
                raise ValueError('unexpected English scene size or non-padding trailing data')
            scenes.append({'index': len(scenes), 'block': block_offset // 0x2000,
                           'compressedOffset': block_offset + start, 'data': payload})
    return scenes


AI_KNOWN = set(range(4)) | set(range(0x10, 0x14)) | set(range(0x30, 0x38)) | set(range(0x40, 0x46)) | set(range(0x50, 0x53)) | set(range(0x60, 0x63)) | set(range(0x70, 0x76)) | set(range(0x80, 0x88)) | set(range(0x90, 0x97)) | {0xa0, 0xa1}


def decode_battle_script(data):
    instructions, issues, owners, visited = {}, [], {}, set()
    pending = [0]
    while pending:
        offset = pending.pop()
        if offset in visited:
            continue
        visited.add(offset)
        reason = None
        if not 0 <= offset < len(data):
            reason = 'branch or fallthrough outside script region'
        elif offset in owners and owners[offset] != offset:
            reason = 'overlapping instruction: branch into operand or message'
        if reason:
            issues.append({'offset': offset, 'reason': reason})
            continue
        op = data[offset]
        width = 3 if op in {*range(4), *range(0x10, 0x14), 0x61, 0x70, 0x71, 0x72} else {0x60: 2, 0x62: 4}.get(op, 1)
        if op in (0x93, 0xa0):
            terminator = data.find(bytes([255 if op == 0x93 else 0]), offset + 1)
            if terminator < 0:
                issues.append({'offset': offset, 'reason': 'unterminated message or debug string'})
                continue
            width = terminator + 1 - offset
        if offset + width > len(data):
            issues.append({'offset': offset, 'reason': 'truncated operand'})
            continue
        if any(i in owners and owners[i] != offset for i in range(offset, offset + width)):
            issues.append({'offset': offset, 'reason': 'overlapping instruction ranges'})
            continue
        for i in range(offset, offset + width):
            owners[i] = offset
        row = {'offset': offset, 'opcode': op, 'bytes': data[offset:offset + width].hex()}
        instructions[offset] = row
        if op not in AI_KNOWN:
            issues.append({'offset': offset, 'reason': f'undocumented opcode {op:#x}; successors unresolved'})
            row['successors'] = []
        elif op == 0x73:
            row['successors'] = []
        elif op in (0x70, 0x71, 0x72):
            row['successors'] = [u16(data, offset + 1)] + ([] if op == 0x72 else [offset + width])
        else:
            row['successors'] = [offset + width]
        pending.extend(row['successors'])
    return {'instructions': [instructions[k] for k in sorted(instructions)], 'issues': issues,
            'unvisitedBytes': len(data) - len(owners)}


def ai_table(data, table_offset, count, end):
    if table_offset + count * 2 > end or end > len(data):
        raise ValueError('AI entity table outside region')
    pointers = [u16(data, table_offset + slot * 2) for slot in range(count)]
    tables = sorted(set(table_offset + x for x in pointers if x != 0xffff))
    entities = []
    for slot, relative in enumerate(pointers):
        row = {'slot': slot, 'relativeOffset': relative, 'scripts': [], 'issues': []}
        entities.append(row)
        if relative == 0xffff:
            row['absent'] = True
            continue
        start = table_offset + relative
        limit = next((x for x in tables if x > start), end)
        if start < table_offset + count * 2 or start + 32 > limit or limit > end:
            row['issues'].append('AI entity header outside region')
            continue
        for script in range(16):
            entry = u16(data, start + script * 2)
            record = {'scriptId': script, 'relativeOffset': entry}
            row['scripts'].append(record)
            if entry == 0xffff:
                record['absent'] = True
            elif entry < 32 or start + entry >= limit:
                record['issues'] = [{'reason': 'AI script entry outside entity region'}]
            else:
                record.update(fileOffset=start + entry, **decode_battle_script(data[start + entry:limit]))
    return entities


def unpack_kernel(data):
    sections, offset = [], 0
    while offset < len(data):
        # Installed PC kernel.bin has 27 complete sections followed by up to
        # three zero bytes aligning the archive to four bytes. Preserve this
        # evidence; do not accept a partial next header in a shorter archive.
        if len(sections) == 27 and 0 < len(data) - offset < 4 and len(data) % 4 == 0 and not any(data[offset:]):
            sections[-1]['archiveAlignmentBytes'] = len(data) - offset
            break
        if offset + 6 > len(data):
            raise ValueError('truncated kernel section header')
        packed, expected, kind = struct.unpack_from('<HHH', data, offset)
        end = offset + 6 + packed
        if end > len(data):
            raise ValueError('truncated kernel section body')
        result, extra = gunzip_one(data[offset + 6:end], expected)
        if len(result) != expected or extra:
            raise ValueError('kernel decompressed size or member boundary mismatch')
        sections.append({'index': len(sections), 'type': kind, 'offset': offset,
                         'compressedBytes': packed, 'data': result})
        offset = end
    return sections


def inventory_issues(summary):
    """Reject incomplete archives even when every remaining member decodes cleanly."""
    issues = []
    if sorted(x['name'].lower() for x in summary['world']) != ['wm0.ev', 'wm2.ev', 'wm3.ev']:
        issues.append('world inventory must contain wm0.ev, wm2.ev and wm3.ev exactly once')
    if sorted(x['index'] for x in summary['battleScenes']) != list(range(256)):
        issues.append('battle scene inventory must contain all 256 scene indices exactly once')
    if sorted(x['index'] for x in summary['kernelSections']) != list(range(27)):
        issues.append('kernel inventory must contain all 27 section indices exactly once')
    world_errors = sum(len(x['issues']) for x in summary['world'])
    battle_errors = sum(x['issues'] for x in summary['battleScenes'])
    characters = next((x.get('characterAI', []) for x in summary['kernelSections'] if x['index'] == 2), [])
    if len(characters) != 12:
        issues.append('character AI inventory must retain all 12 entity slots, including absent entries')
    character_errors = sum(len(e.get('issues', [])) + sum(len(s.get('issues', [])) for s in e.get('scripts', []))
                           for e in characters)
    for name, count in (('world', world_errors), ('battle AI', battle_errors), ('character AI', character_errors)):
        if count:
            issues.append(f'{name}: {count} structural decoding issues require review')
    return issues


def audit_game(game, output):
    output.mkdir(parents=True, exist_ok=True)
    summary = {'gameRoot': str(game), 'sources': [], 'world': [], 'battleScenes': [], 'kernelSections': [],
               'scope': 'Native resource inventory and control-flow decoding, not complete engine semantics or proof of accessibility.'}
    for relative in ('data/wm/world_us.lgp', 'data/lang-en/battle/scene.bin', 'data/lang-en/kernel/kernel.bin'):
        path = game / relative
        data = path.read_bytes()
        summary['sources'].append({'path': relative, 'bytes': len(data), 'sha256': sha(data)})
        if path.suffix.lower() == '.lgp':
            entries = lgp_entries(data)
            save(output / 'world-archive-index.json', entries)
            for entry in entries:
                if not entry['name'].lower().endswith('.ev'):
                    continue
                payload = data[entry['offset']:entry['offset'] + entry['size']]
                result = audit_world(payload)
                # Never use archive-provided filenames as output paths.
                filename = f"world-event-{entry['index']:04d}"
                (output / (filename + '.ev')).write_bytes(payload)
                save(output / (filename + '.json'), result)
                summary['world'].append({'name': entry['name'], 'report': filename + '.json', 'sha256': sha(payload),
                    'entries': len(result['entries']), 'instructions': len(result['instructions']),
                    'issues': result['issues'], 'unvisitedCodeWords': result['unvisitedCodeWords']})
        elif path.name == 'scene.bin':
            for record in unpack_scenes(data):
                scene = record.pop('data')
                record.update(sha256=sha(scene), bytes=len(scene),
                    enemyIds=[u16(scene, i * 2) for i in range(3)],
                    formationAI=ai_table(scene, 0xc80, 4, 0xe80), enemyAI=ai_table(scene, 0xe80, 3, len(scene)))
                filename = f"scene-{record['index']:03d}"
                (output / (filename + '.bin')).write_bytes(scene)
                save(output / (filename + '.json'), record)
                scripts = [s for e in record['formationAI'] + record['enemyAI'] for s in e['scripts'] if not s.get('absent')]
                summary['battleScenes'].append({'index': record['index'], 'sha256': record['sha256'], 'scripts': len(scripts),
                    'issues': sum(len(s.get('issues', [])) for s in scripts) + sum(len(e['issues']) for e in record['formationAI'] + record['enemyAI'])})
        else:
            for record in unpack_kernel(data):
                payload = record.pop('data')
                record.update(sha256=sha(payload), bytes=len(payload))
                filename = f"kernel-{record['index']:02d}"
                (output / (filename + '.bin')).write_bytes(payload)
                if record['index'] == 2:
                    record['characterAI'] = ai_table(payload, 0x61c, 12, 0xc00)
                    save(output / 'kernel-character-ai.json', record['characterAI'])
                summary['kernelSections'].append(record)
    summary['inventoryIssues'] = inventory_issues(summary)
    save(output / 'summary.json', summary)
    return {'output': str(output), 'worldScripts': sum(x['entries'] for x in summary['world']),
            'worldIssues': sum(len(x['issues']) for x in summary['world']), 'scenes': len(summary['battleScenes']),
            'battleScripts': sum(x['scripts'] for x in summary['battleScenes']),
            'battleIssues': sum(x['issues'] for x in summary['battleScenes']), 'kernelSections': len(summary['kernelSections']),
            'inventoryIssues': summary['inventoryIssues']}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('game_root', type=Path)
    parser.add_argument('private_output', type=Path)
    args = parser.parse_args()
    output = args.private_output.resolve()
    if any((p / '.git').exists() for p in [output, *output.parents]):
        parser.error('generated game resources must be outside a Git checkout')
    result = audit_game(args.game_root.resolve(), output)
    print(json.dumps(result))
    return 1 if result['inventoryIssues'] else 0


if __name__ == '__main__':
    raise SystemExit(main())
