"""Check measured original records and compare native map pixels to Python decoding."""
import ctypes as C
from pathlib import Path
import struct
import unittest
from PIL import Image
from g3_battle import parse_battle, parse_map, parse_character, map_layer_rgb
from g3_image import GameImage
from g3_obs import decode_obs_actions
from g3_resources import parse_index, read_resource
from g3_scenario import parse_chapter


class NativeMap(C.Structure):
    _fields_ = [(name, C.c_void_p) for name in ("cells", "tiles", "terrain", "attributes")]+[
        (name, C.c_uint32) for name in ("columns", "rows", "tile_count", "width", "height")]+[
        ("origin_x", C.c_int32), ("origin_y", C.c_int32)]


class BattleTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.image = GameImage("Store/image.img")
        cls.btl = cls.image.read("G3P1/Btls/0006.BTL")
        cls.obt = cls.image.read("G3P1/Obts/0079.OBT")
        cls.lib = C.CDLL(str(Path("homebrew/build/g3_formats.dll").resolve()))
        cls.lib.g3_map_open.argtypes = [C.POINTER(NativeMap), C.c_void_p, C.c_size_t]
        cls.lib.g3_map_render.argtypes = [C.POINTER(NativeMap), C.c_int, C.c_int,
                                         C.c_void_p, C.c_size_t, C.c_uint, C.c_uint]

    @classmethod
    def tearDownClass(cls):
        cls.image.__exit__(None, None, None)

    def test_chapter_calls_fields_then_battle(self):
        chapter = parse_chapter(self.image.read("G3P1/Chps/0000.CPR"))
        calls = [(a[0], a[1]) for event in chapter["events"] for a in event["actions"]
                 if a[0] in (502, 503)]
        self.assertEqual(calls[:3], [(503, 1), (503, 2), (502, 6)])

    def test_battle_units_and_events(self):
        battle = parse_battle(self.btl)
        self.assertEqual(battle["header"][0], 79)
        self.assertEqual(len(battle["units"]), 22)
        self.assertEqual([u[:5] for u in battle["units"][:4]],
                         [(0, 0, 39, 35, 1), (1, 0, 40, 34, 1),
                          (2, 0, 40, 37, 1), (5, 0, 39, 36, 1)])
        self.assertEqual(sum(u[0] == 501 for u in battle["units"]), 15)
        self.assertEqual(sum(u[0] == 505 for u in battle["units"]), 3)
        self.assertEqual(len(battle["events"]), 9)

    def test_battle_truncation_and_trailing_bytes(self):
        for length in (0, 1, 22, 43, 417, len(self.btl)-1):
            with self.assertRaises(ValueError):
                parse_battle(self.btl[:length])
        with self.assertRaises(ValueError):
            parse_battle(self.btl+b"\0")

    def test_character_model_and_base_hp(self):
        saladin = parse_character(self.image.read("G3P1/Chars/0000.CHR"))
        self.assertEqual((saladin["name"], saladin["model"], saladin["value_58"], saladin["hp_base"]),
                         ("살라딘", 196, 800, 700))
        self.assertEqual(len(saladin["stats_raw"]), 10)

    def test_character_walk_frames_and_timing(self):
        path = "G3P1/Obss/Obss.idx"
        records = {r.name: r for r in parse_index(self.image.read(path))}
        for model in (196, 352, 674, 682):
            raw = read_resource(self.image, path, records[f"{model:04d}.obs"], {})
            groups, actions, trailer = decode_obs_actions(raw)
            self.assertEqual(len(groups), 1)
            self.assertEqual(len(actions), 174)
            self.assertEqual(len(trailer), 34)
            walk = actions[3]
            self.assertEqual(walk["duration"], 18)
            self.assertEqual([(r[0], r[1], r[4]) for r in walk["records"] if r[2] == 0],
                             [(0, 3, 3), (3, 3, 4), (6, 3, 5),
                              (9, 3, 6), (12, 3, 7), (15, 3, 8)])
            with self.assertRaises(ValueError):
                decode_obs_actions(raw[:len(raw)//2])

    def open_native(self, raw):
        buffer = C.create_string_buffer(raw)
        view = NativeMap()
        self.assertEqual(self.lib.g3_map_open(C.byref(view), buffer, len(raw)), 1)
        return buffer, view  # Keep immutable source alive while rendering.

    def test_native_map_matches_original_bitmap(self):
        parsed = parse_map(self.obt)
        buffer, view = self.open_native(self.obt)
        self.assertEqual((view.width, view.height, view.columns, view.rows, view.tile_count, view.origin_y),
                         (45, 45, 45, 192, 5762, -12))
        reference = map_layer_rgb(parsed).convert("RGBA")
        for x, y in ((0, 0), (1137, 1001), (1770, 1510), (-10, -20)):
            out = C.create_string_buffer(640*480*4)
            self.assertEqual(self.lib.g3_map_render(C.byref(view), x, y, out, len(out), 640, 480), 1)
            expected = Image.new("RGBA", (640, 480), (0, 0, 0, 255))
            expected.paste(reference, (-x, -y))
            self.assertEqual(out.raw, expected.tobytes())
            if (x, y) == (1137, 1001):
                path = Path("dist/switch/g3nx/battle6-native-base-preview.png")
                path.parent.mkdir(parents=True, exist_ok=True)
                Image.frombytes("RGBA", (640, 480), out.raw).save(path)

    def test_rgb555_known_colors_and_buffer_guards(self):
        # Two solid tiles exercise channel order independently of the Python renderer.
        raw = (struct.pack("<IHH", 1, 1, 1)+bytes(2)+struct.pack("<HHhh", 2, 1, 0, 0)+
               struct.pack("<HHHHII", 0, 0, 0, 1, 0, 2)+
               struct.pack("<H", 0x7c00)*320+struct.pack("<H", 0x03ff)*320+bytes(2+64))
        buffer, view = self.open_native(raw)
        guarded = C.create_string_buffer(b"\xa5"*(80*8*4+32), 80*8*4+32)
        destination = C.cast(C.byref(guarded, 16), C.c_void_p)
        self.assertEqual(self.lib.g3_map_render(C.byref(view), 0, 0, destination, 80*8*4-1, 80, 8), 0)
        self.assertEqual(guarded.raw, b"\xa5"*len(guarded))
        self.assertEqual(self.lib.g3_map_render(C.byref(view), 0, 0, destination, 80*8*4, 80, 8), 1)
        self.assertEqual(guarded.raw[:16]+guarded.raw[-16:], b"\xa5"*32)
        self.assertEqual(guarded.raw[16:20], bytes((255, 0, 0, 255)))
        self.assertEqual(guarded.raw[16+40*4:20+40*4], bytes((0, 255, 255, 255)))

    def test_map_bad_references_counts_and_truncation(self):
        broken = bytearray(self.obt)
        base = 4+4+45*45*2+8
        struct.pack_into("<H", broken, base+2, 65535)
        variants = [bytes(broken), self.obt[:4], self.obt[:-1], self.obt[:-64], self.obt+b"\0"]
        broken = bytearray(self.obt)
        struct.pack_into("<I", broken, 0, 0xffffffff)
        variants.append(bytes(broken))
        for raw in variants:
            with self.assertRaises(ValueError):
                parse_map(raw)
            buffer = C.create_string_buffer(raw)
            view = NativeMap()
            self.assertEqual(self.lib.g3_map_open(C.byref(view), buffer, len(raw)), 0)
            self.assertFalse(view.tiles)


if __name__ == "__main__":
    unittest.main()
