"""Deployment package completeness and stale runtime asset rejection."""
from contextlib import redirect_stdout
import hashlib
import io
import json
from pathlib import Path
import tempfile
import unittest
from unittest.mock import patch
import zipfile
import package_release


class PackageTests(unittest.TestCase):
    def fixture(self,root,version='0.4.0'):
        folder=root/'dist/switch/g3nx'
        files=('g3nx.nro','g3nx.title','g3nx.story','g3nx.field2',
               'audio/manifest.json','audio/bgm0035.wav','audio/bgm0042.wav')
        if version=='0.5.0':files+=('g3nx.battle6','battle6.obt')
        report={'version':version,'runtime_files':{}}
        for name in files:
            path=folder/name;path.parent.mkdir(parents=True,exist_ok=True)
            data=name.encode();path.write_bytes(data)
            report['runtime_files'][name]={'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest()}
        (folder/'README.txt').write_text('fixture')
        (folder/'build-report.json').write_text(json.dumps(report))
        return folder,report

    def test_field_and_audio_are_included_with_valid_zip_checksums(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);_,report=self.fixture(root)
            with patch.object(package_release,'ROOT',root),redirect_stdout(io.StringIO()):package_release.main()
            with zipfile.ZipFile(root/'dist/g3nx-scenario-0.4.0.zip') as z:
                self.assertIsNone(z.testzip())
                for name in report['runtime_files']:self.assertIn('switch/g3nx/'+name,z.namelist())

    def test_modified_field_or_music_cannot_be_packaged(self):
        for name in ('g3nx.field2','audio/bgm0035.wav'):
            with tempfile.TemporaryDirectory() as d:
                root=Path(d);folder,_=self.fixture(root);(folder/name).write_bytes(b'changed')
                with patch.object(package_release,'ROOT',root),self.assertRaisesRegex(ValueError,'changed'):
                    package_release.main()
                self.assertFalse((root/'dist/g3nx-scenario-0.4.0.zip').exists())

    def test_missing_field_and_unsafe_paths_are_rejected(self):
        for bad in ('missing','traversal'):
            with tempfile.TemporaryDirectory() as d:
                root=Path(d);folder,report=self.fixture(root)
                if bad=='missing':del report['runtime_files']['g3nx.field2']
                else:report['runtime_files']['../outside.bin']={'sha256':'unused','bytes':0}
                (folder/'build-report.json').write_text(json.dumps(report))
                with patch.object(package_release,'ROOT',root),self.assertRaises(ValueError):package_release.main()

    def test_navigation_package_includes_map_and_sprites(self):
        with tempfile.TemporaryDirectory() as d:
            root=Path(d);_,report=self.fixture(root,'0.5.0')
            with patch.object(package_release,'ROOT',root),redirect_stdout(io.StringIO()):package_release.main()
            with zipfile.ZipFile(root/'dist/g3nx-scenario-0.5.0.zip') as archive:
                self.assertIsNone(archive.testzip())
                self.assertEqual(set(archive.namelist()),{'switch/g3nx/'+n for n in (*report['runtime_files'],'README.txt','build-report.json')})

    def test_navigation_assets_cannot_be_omitted_or_modified(self):
        for name in ('g3nx.battle6','battle6.obt'):
            for corrupt in (False,True):
                with tempfile.TemporaryDirectory() as d:
                    root=Path(d);folder,report=self.fixture(root,'0.5.0')
                    if corrupt:(folder/name).write_bytes(b'changed')
                    else:
                        del report['runtime_files'][name]
                        (folder/'build-report.json').write_text(json.dumps(report))
                    with patch.object(package_release,'ROOT',root),self.assertRaises(ValueError):package_release.main()
                    self.assertFalse((root/'dist/g3nx-scenario-0.5.0.zip').exists())


if __name__=='__main__':unittest.main()
