import gzip
import struct
import unittest

import resource_audit as audit


def event(words, entries=((1, 1),)):
    header = bytearray(struct.pack('<HH', 0xffff, 0) * 256)
    for i, pair in enumerate(entries):
        struct.pack_into('<HH', header, i * 4, *pair)
    return bytes(header) + struct.pack(f'<{len(words)}H', *words)


class WorldTests(unittest.TestCase):
    def test_jump_crosses_other_entry_and_early_return(self):
        # Cutting at the next entry or the first RET loses the field transition.
        data = event([0x203, 0x201, 5, 0x203, 0x203, 0x318, 0x203], ((1, 1), (2, 4)))
        result = audit.audit_world(data)
        self.assertEqual(len(result['entries']), 2)
        self.assertEqual(result['entries'][0]['reachable'], [1, 3, 5, 6])
        self.assertEqual(result['instructions']['5']['opcode'], 0x318)

    def test_aliases_keep_each_call_identity(self):
        result = audit.audit_world(event([0x203, 0x203], ((1, 1), (2, 1))))
        self.assertEqual([e['functionId'] for e in result['entries']], [1, 2])
        self.assertEqual(result['entries'][0]['reachable'], result['entries'][1]['reachable'])

    def test_truncated_operand_is_not_a_success(self):
        result = audit.audit_world(event([0x203, 0x110]))
        self.assertTrue(any('truncated' in x['reason'] for x in result['issues']))

    def test_branch_into_operand_is_reported(self):
        result = audit.audit_world(event([0x203, 0x201, 2, 0x203]))
        self.assertTrue(any('overlap' in x['reason'] for x in result['issues']))

    def test_invalid_entry_survives_as_issue(self):
        result = audit.audit_world(event([0x203], ((1, 0x7000),)))
        self.assertEqual(len(result['entries']), 1)
        self.assertTrue(result['issues'])


class SceneTests(unittest.TestCase):
    def test_gzip_member_padding_does_not_truncate_ff_trailer(self):
        scene = bytes([0xff]) * 0x1e80
        payload = gzip.compress(scene, mtime=0)
        block = struct.pack('<16I', 16, *([0xffffffff] * 15)) + payload
        block += bytes([0xff]) * (8192 - len(block))
        records = audit.unpack_scenes(block)
        self.assertEqual(len(records), 1)
        self.assertEqual(records[0]['data'], scene)

    def test_truncated_scene_block_is_rejected(self):
        with self.assertRaises(ValueError):
            audit.unpack_scenes(b'\xff' * 8191)

    def test_ai_branch_continues_after_early_return(self):
        result = audit.decode_battle_script(bytes([0x70, 4, 0, 0x73, 0x60, 7, 0x73]))
        self.assertEqual([x['offset'] for x in result['instructions']], [0, 3, 4, 6])
        self.assertFalse(result['issues'])

    def test_message_ends_at_ff_not_zero(self):
        result = audit.decode_battle_script(bytes([0x93, 0, 0x21, 0xff, 0x73]))
        self.assertEqual(len(result['instructions']), 2)
        self.assertEqual(result['instructions'][0]['bytes'], '930021ff')
        self.assertEqual(result['instructions'][1]['offset'], 4)

    def test_message_without_terminator_is_reported(self):
        result = audit.decode_battle_script(bytes([0x93, 0x21]))
        self.assertTrue(result['issues'])

    def test_kernel_section_size_is_checked(self):
        payload = gzip.compress(b'123', mtime=0)
        with self.assertRaises(ValueError):
            audit.unpack_kernel(struct.pack('<HHH', len(payload), 9, 0) + payload)

    def test_kernel_allows_zero_alignment_after_all_27_sections(self):
        payload = gzip.compress(b'123', mtime=0)
        archive = (struct.pack('<HHH', len(payload), 3, 0) + payload) * 27
        archive += b'\0' * (-len(archive) % 4)
        self.assertEqual(len(audit.unpack_kernel(archive)), 27)

    def test_incomplete_kernel_is_not_hidden_as_alignment(self):
        payload = gzip.compress(b'123', mtime=0)
        archive = struct.pack('<HHH', len(payload), 3, 0) + payload + b'\0\0'
        with self.assertRaises(ValueError):
            audit.unpack_kernel(archive)


class CompleteInventoryTests(unittest.TestCase):
    def summary(self):
        summary = {
            'world': [{'name': name, 'issues': []} for name in ('wm0.ev', 'wm2.ev', 'wm3.ev')],
            'battleScenes': [{'index': i, 'issues': 0} for i in range(256)],
            'kernelSections': [{'index': i} for i in range(27)],
        }
        summary['kernelSections'][2]['characterAI'] = [{'slot': i, 'absent': True} for i in range(12)]
        return summary

    def test_complete_native_inventory_is_accepted(self):
        self.assertEqual(audit.inventory_issues(self.summary()), [])

    def test_duplicate_world_file_cannot_hide_missing_world(self):
        summary = self.summary()
        summary['world'][2]['name'] = 'wm0.ev'
        self.assertTrue(any('world' in issue for issue in audit.inventory_issues(summary)))

    def test_partial_but_decodable_archives_are_not_complete(self):
        summary = self.summary()
        summary['battleScenes'].pop()
        summary['kernelSections'].pop()
        issues = audit.inventory_issues(summary)
        self.assertTrue(any('scene' in issue for issue in issues))
        self.assertTrue(any('kernel' in issue for issue in issues))

    def test_character_ai_errors_are_not_lost_in_archive_counts(self):
        summary = self.summary()
        summary['kernelSections'][2]['characterAI'] = [
            {'issues': [], 'scripts': [{'issues': [{'reason': 'invalid jump'}]}]}
        ]
        self.assertTrue(any('character AI' in issue for issue in audit.inventory_issues(summary)))


if __name__ == '__main__':
    unittest.main()
