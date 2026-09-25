import struct
import unittest
from engine_map import extract


class EngineMapTests(unittest.TestCase):
    def test_contiguous_mappings_preserve_aliases_and_boundaries(self):
        image = b''.join(struct.pack('<QQ', *pair) for pair in [(0, 0), (0x100, 0x200), (0x110, 0x200), (0x120, 0x220), (0, 0)])
        result = extract(image, 32, [(0x100, 0x130)], [(0x200, 0x230)])
        self.assertEqual(result['tableStartRva'], 16)
        self.assertEqual(result['tableEndRvaExclusive'], 64)
        self.assertEqual([r['legacyAddress'] for r in result['records']], [0x100, 0x110, 0x120])
        self.assertEqual(result['boundaryAfter'], [0, 0])

    def test_rejects_anchor_pointing_outside_code(self):
        with self.assertRaises(ValueError):
            extract(struct.pack('<QQ', 0x100, 0x900), 0, [(0x100, 0x130)], [(0x200, 0x230)])


if __name__ == '__main__':
    unittest.main()
