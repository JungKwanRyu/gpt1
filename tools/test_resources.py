"""Regression checks against the supplied corpus and independent native C parsers."""
import ctypes as C
import io
import json
from pathlib import Path
import random
import struct
import tempfile
import unittest

from PIL import Image
from g3_image import GameImage
from g3_resources import parse_index, parse_strings, read_resource, xor_text


class IndexRecord(C.Structure):
    _fields_ = [("name", C.c_char*10), ("archive", C.c_char*14), ("start", C.c_uint32), ("end", C.c_uint32)]


class Pack(C.Structure):
    _fields_ = [("file", C.c_void_p), ("count", C.c_uint32), ("file_size", C.c_uint64)]


class Asset(C.Structure):
    _fields_ = [("name", C.c_char*17), ("width", C.c_uint32), ("height", C.c_uint32),
                ("offset", C.c_uint32), ("size", C.c_uint32)]


class CorpusTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.image = GameImage("Store/image.img")

    @classmethod
    def tearDownClass(cls):
        cls.image.close()

    def test_fat_reader_matches_independent_7zip_extraction(self):
        for name in ("G3Launch.exe", "alpha.dll", "G3.pak"):
            self.assertEqual(self.image.read("G3P1/"+name), (Path("reverse/original/G3P1")/name).read_bytes())

    def test_known_text_and_archive_ranges(self):
        self.assertEqual(xor_text(self.image.read("G3P1/Chars/0000.CHR")[:24]), "살라딘")
        self.assertEqual(parse_strings(self.image.read("G3P1/Chps/0000.CLG"))[0], "사막요새 카디스")
        for entry in self.image.entries:
            if not entry.path.lower().endswith(".idx"):
                continue
            cache = {}
            for rec in parse_index(self.image.read(entry.path)):
                self.assertEqual(len(read_resource(self.image, entry.path, rec, cache)), rec.size)

    def test_invalid_python_inputs(self):
        for data in (b"", b"\x01", struct.pack("<I", 0xffffffff), struct.pack("<I", 1)+bytes(30)):
            with self.assertRaises((ValueError, UnicodeDecodeError)):
                parse_index(data)
        for data in (b"", b"\0", b"\x01\0\x01\0\xff\xff\xff"):
            with self.assertRaises(ValueError):
                parse_strings(data)


@unittest.skipUnless(Path("homebrew/build/g3_formats.dll").exists(), "Native C library is not built")
class NativeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.image = GameImage("Store/image.img")
        cls.lib = C.CDLL(str(Path("homebrew/build/g3_formats.dll").resolve()))
        cls.lib.g3_index_count.argtypes = [C.c_void_p, C.c_size_t, C.POINTER(C.c_uint32)]
        cls.lib.g3_index_record.argtypes = [C.c_void_p, C.c_size_t, C.c_uint32, C.POINTER(IndexRecord)]
        cls.lib.g3_string.argtypes = [C.c_void_p, C.c_size_t, C.c_uint32, C.c_void_p, C.c_size_t, C.POINTER(C.c_size_t)]
        cls.lib.g3_pack_open.argtypes = [C.POINTER(Pack), C.c_char_p]
        cls.lib.g3_pack_close.argtypes = [C.POINTER(Pack)]
        cls.lib.g3_pack_record.argtypes = [C.POINTER(Pack), C.c_uint32, C.POINTER(Asset)]
        cls.lib.g3_pack_pixels.argtypes = [C.POINTER(Pack), C.POINTER(Asset), C.c_void_p, C.c_size_t]

    @classmethod
    def tearDownClass(cls):
        cls.image.close()

    def test_native_matches_all_indices(self):
        for entry in self.image.entries:
            if not entry.path.lower().endswith(".idx"):
                continue
            data = self.image.read(entry.path)
            records = parse_index(data)
            count = C.c_uint32()
            self.assertEqual(self.lib.g3_index_count(data, len(data), C.byref(count)), 1)
            self.assertEqual(count.value, len(records))
            native = IndexRecord()
            for i, rec in enumerate(records):
                self.assertEqual(self.lib.g3_index_record(data, len(data), i, C.byref(native)), 1)
                self.assertEqual((native.name.decode(), native.archive.decode(), native.start, native.end),
                                 (rec.name, rec.archive, rec.start, rec.end))
            self.assertEqual(self.lib.g3_index_record(data, len(data), len(records), C.byref(native)), 0)

    def test_native_matches_all_text_tables(self):
        output = C.create_string_buffer(65536)
        length = C.c_size_t()
        count = 0
        for entry in self.image.entries:
            if Path(entry.path).suffix.lower() not in (".dlg", ".clg", ".flg", ".elg"):
                continue
            data = self.image.read(entry.path)
            try:
                strings = parse_strings(data)
            except (ValueError, UnicodeDecodeError):
                self.assertEqual(self.lib.g3_string(data, len(data), 0, output, len(output), C.byref(length)), 0)
                continue
            for i, text in enumerate(strings):
                self.assertEqual(self.lib.g3_string(data, len(data), i, output, len(output), C.byref(length)), 1)
                self.assertEqual(output.raw[:length.value].decode("cp949"), text)
                count += 1
        self.assertEqual(count, 10811)

    def test_native_pack_and_converted_pixels(self):
        pack, asset = Pack(), Asset()
        self.assertEqual(self.lib.g3_pack_open(C.byref(pack), b"dist/switch/g3nx/g3nx.assets"), 1)
        output = C.create_string_buffer(1040*624*4)
        metadata = json.loads(Path("dist/switch/g3nx/g3nx.json").read_text())
        try:
            self.assertEqual(pack.count, len(metadata))
            for i, item in enumerate(metadata):
                self.assertEqual(self.lib.g3_pack_record(C.byref(pack), i, C.byref(asset)), 1)
                self.assertEqual((asset.name.decode(), asset.width, asset.height, asset.size),
                                 (item["name"], *item["viewer_size"], item["bytes"]))
                self.assertEqual(self.lib.g3_pack_pixels(C.byref(pack), C.byref(asset), output, len(output)), 1)
            self.assertEqual(self.lib.g3_pack_record(C.byref(pack), pack.count, C.byref(asset)), 0)
            self.assertEqual(self.lib.g3_pack_record(C.byref(pack), 1, C.byref(asset)), 1)
            self.assertEqual(self.lib.g3_pack_pixels(C.byref(pack), C.byref(asset), output, 1), 0)
            self.lib.g3_pack_pixels(C.byref(pack), C.byref(asset), output, len(output))
            records = parse_index(self.image.read("G3P1/BGRs/BGRs.idx"))
            blob = read_resource(self.image, "G3P1/BGRs/BGRs.idx", records[1], {})
            with Image.open(io.BytesIO(blob)) as im:
                expected = im.convert("RGBA")
                expected.thumbnail((1040,624), Image.Resampling.LANCZOS)
                self.assertEqual(output.raw[:asset.size], expected.tobytes())
        finally:
            self.lib.g3_pack_close(C.byref(pack))

    def test_native_rejects_malformed_buffers(self):
        rng = random.Random(7)
        record, count, length = IndexRecord(), C.c_uint32(), C.c_size_t()
        out = C.create_string_buffer(65536)
        for i in range(2000):
            data = rng.randbytes(rng.randrange(128))
            self.assertEqual(self.lib.g3_index_count(data, len(data), C.byref(count)), 0)
            self.assertEqual(self.lib.g3_index_record(data, len(data), i, C.byref(record)), 0)
            self.assertEqual(self.lib.g3_string(data, len(data), i, out, len(out), C.byref(length)), 0)
        pack, asset = Pack(), Asset()
        with tempfile.TemporaryDirectory(dir="homebrew/build") as directory:
            target = Path(directory)/"bad.assets"
            for width, offset, size in ((0xffffffff,48,4), (1,0,4), (1,48,0xffffffff)):
                target.write_bytes(struct.pack("<8sII16sIIII", b"G3NXRAW1",1,0,b"bad.bgr",width,1,offset,size)+bytes(4))
                relative = target.resolve().relative_to(Path.cwd()).as_posix().encode("ascii")
                self.assertEqual(self.lib.g3_pack_open(C.byref(pack), relative), 1)
                try:
                    self.assertEqual(self.lib.g3_pack_record(C.byref(pack), 0, C.byref(asset)), 0)
                finally:
                    self.lib.g3_pack_close(C.byref(pack))


if __name__ == "__main__":
    unittest.main(verbosity=2)
