"""Exercise the reader against real CLI metadata and invalid PE inputs."""
from pathlib import Path
import tempfile
import unittest

from inspect_cli import Metadata

ROOT = Path(__file__).resolve().parents[1]


class MetadataTests(unittest.TestCase):
    def test_managed_libraries_are_not_native_x86_binaries(self):
        for name in ['WogShim', 'WogPatcher']:
            with self.subTest(name=name):
                report = Metadata(ROOT / 'WogiOS_project/libs' / (name + '.dll')).report()
                self.assertEqual(report['assembly'], name)
                self.assertEqual(report['target_framework'], ['.NETCoreApp,Version=v10.0'])
                self.assertTrue(report['il_only'])
                self.assertFalse(report['requires_32bit'])
                self.assertFalse(report['native_imports'])

    def test_identifies_real_third_party_requirements(self):
        shim = Metadata(ROOT / 'WogiOS_project/libs/WogShim.dll').report()
        patcher = Metadata(ROOT / 'WogiOS_project/libs/WogPatcher.dll').report()
        self.assertIn(('SkiaSharp', '3.119.0.0'),
                      [(r['name'], r['version']) for r in shim['assembly_references']])
        self.assertIn(('Mono.Cecil', '0.11.6.0'),
                      [(r['name'], r['version']) for r in patcher['assembly_references']])
        self.assertTrue(any(m['assembly'] == 'SkiaSharp' for m in shim['external_members']))

    def test_rejects_non_pe_and_truncated_image(self):
        with tempfile.TemporaryDirectory() as directory:
            file = Path(directory) / 'bad.dll'
            for content in [b'', b'not an assembly', b'MZ']:
                file.write_bytes(content)
                with self.subTest(content=content), self.assertRaises(ValueError):
                    Metadata(file)


if __name__ == '__main__':
    unittest.main()
