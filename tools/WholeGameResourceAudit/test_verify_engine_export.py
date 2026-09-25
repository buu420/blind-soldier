import json
from pathlib import Path
import tempfile
import unittest
from verify_engine_export import verify


class ExportVerificationTests(unittest.TestCase):
    def make_export(self, root):
        # Handwritten minimal Ghidra artifact contract, not produced by exporter.
        digest = 'e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855'
        (root / 'source.bin').write_bytes(b'')
        (root / 'log.txt').write_text('WARN  Decompiling 00401000, pcode error at 00401000\n')
        (root / 'identity.json').write_text(json.dumps({'sha256': digest}))
        summary = {'sha256': digest, 'processed': 1, 'succeeded': 1, 'failed': 0,
                   'executableBytes': 8, 'executableBytesOutsideFunctions': 4}
        (root / 'summary.json').write_text(json.dumps(summary))
        row = {'entry': '00401000', 'bytes': 4, 'prototype': 'void f()', 'decompileCompleted': True,
               'cPath': 'functions/0040/00401000.c', 'assemblyPath': 'functions/0040/00401000.asm'}
        (root / 'functions.jsonl').write_text(json.dumps(row) + '\n')
        directory = root / 'functions/0040'
        directory.mkdir(parents=True)
        (directory / '00401000.json').write_text(json.dumps(row))
        (directory / '00401000.c').write_text('/* WARNING: Bad instruction */\nvoid f() { halt_baddata(); }')
        (directory / '00401000.asm').write_text('00401000 RET\n')

    def test_success_status_does_not_hide_truncated_code(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.make_export(root)
            result = verify(root, root / 'source.bin', root / 'log.txt')
            self.assertFalse(result['structuralErrors'])
            self.assertEqual(result['functionsWithTruncatedControlFlow'], ['00401000'])
            self.assertEqual(result['functionsWithPcodeErrors'], ['00401000'])

    def test_stale_summary_does_not_validate_partial_export(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.make_export(root)
            (root / 'functions.jsonl').write_text('')
            result = verify(root, root / 'source.bin', root / 'log.txt')
            self.assertIn('function index count mismatch or duplicate entries', result['structuralErrors'])

    def test_source_hash_mismatch_is_reported(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            self.make_export(root)
            (root / 'source.bin').write_bytes(b'changed')
            result = verify(root, root / 'source.bin', root / 'log.txt')
            self.assertIn('source binary identity mismatch', result['structuralErrors'])


if __name__ == '__main__':
    unittest.main()
