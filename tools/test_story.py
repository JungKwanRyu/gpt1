"""Exercise the original first-field program, the C compositor and native checkpoints."""
import ctypes as C
from pathlib import Path
import struct
import tempfile
import unittest
import zlib
from PIL import Image
from g3_image import GameImage
from g3_scenario import parse_field,parse_chapter,parse_episodes
from test_resources import Pack,Asset


FIELDS="active pc background left right center dialog page wait timer status next_field error track".split()
class Story(C.Structure):
    _fields_=[(name,C.c_int32) for name in FIELDS]+[("flags",C.c_uint8*256),
                ("enabled",C.c_uint8*16),("finished",C.c_uint8*16)]


class ScenarioTests(unittest.TestCase):
    def test_original_entry_chapters_and_first_field_records(self):
        with GameImage("Store/image.img") as image:
            episodes=parse_episodes(image.read("G3P1/Eps/EPISODE.EPS"))["groups"]
            self.assertEqual([len(group) for group in episodes],[20,17,20])
            for i,number in enumerate((0,20,37)):
                self.assertEqual(episodes[i][0]["words"][3],number)
                chapter=parse_chapter(image.read(f"G3P1/Chps/{number:04d}.CPR"))
                self.assertTrue(any(a[0]==503 and a[1]==(1,83,68)[i]
                                    for event in chapter["events"] for a in event["actions"]))
            field=parse_field(image.read("G3P1/Flds/0001.FLD"))
            self.assertEqual(len(field["events"]),6)
            self.assertEqual(field["events"][0]["kind"],65535)
            self.assertEqual(field["header"][0],137)

    def test_truncated_scenarios_are_rejected(self):
        with GameImage("Store/image.img") as image:
            for path,parser in (("G3P1/Flds/0001.FLD",parse_field),("G3P1/Chps/0000.CPR",parse_chapter),
                                ("G3P1/Eps/EPISODE.EPS",parse_episodes)):
                data=image.read(path)
                for size in (0,1,10,len(data)//2,len(data)-1):
                    with self.assertRaises((ValueError,struct.error)):
                        parser(data[:size])


class RuntimeTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lib=C.CDLL(str(Path("homebrew/build/g3_formats.dll").resolve()))
        cls.lib.g3_story_init.argtypes=[C.POINTER(Story)]
        cls.lib.g3_story_tick.argtypes=[C.POINTER(Story),C.c_int]
        cls.lib.g3_story_asset.argtypes=[C.c_int,C.c_int,C.c_int]
        cls.lib.g3_story_identity.restype=C.c_uint32
        cls.lib.g3_story_save.argtypes=[C.POINTER(Story),C.c_char_p]
        cls.lib.g3_story_load.argtypes=[C.POINTER(Story),C.c_char_p]
        cls.lib.g3_story_blit.argtypes=[C.c_void_p,C.c_void_p,C.c_int]
        cls.lib.g3_menu_render.argtypes=[C.c_void_p,C.c_size_t,C.c_void_p]
        cls.lib.g3_pack_open.argtypes=[C.POINTER(Pack),C.c_char_p]
        cls.lib.g3_pack_close.argtypes=[C.POINTER(Pack)]
        cls.lib.g3_pack_record.argtypes=[C.POINTER(Pack),C.c_uint32,C.POINTER(Asset)]
        cls.lib.g3_pack_pixels.argtypes=[C.POINTER(Pack),C.POINTER(Asset),C.c_void_p,C.c_size_t]

    def first_dialog(self):
        story=Story()
        self.lib.g3_story_init(C.byref(story))
        for _ in range(100):
            self.lib.g3_story_tick(C.byref(story),0)
            if story.wait:
                return story
        self.fail("First dialogue was not reached")

    def next_dialog(self,story):
        previous=(story.active,story.pc,story.dialog,story.page)
        for _ in range(1000):
            self.lib.g3_story_tick(C.byref(story),1)
            if story.wait and (story.active,story.pc,story.dialog,story.page)!=previous:
                return
        self.fail("Dialogue did not advance")

    def test_full_original_field_order_and_boundary(self):
        with GameImage("Store/image.img") as image:
            events=parse_field(image.read("G3P1/Flds/0001.FLD"))["events"]
        expected=[a[2] for event in events for a in event["actions"] if a[0] in (200,201,202)]
        story=Story()
        self.lib.g3_story_init(C.byref(story))
        trace=[]
        previous=None
        preview=None
        for _ in range(20000):
            self.lib.g3_story_tick(C.byref(story),1)
            if story.wait:
                key=(story.active,story.pc)
                if key!=previous:
                    trace.append(story.dialog)
                    previous=key
                if story.dialog==2 and story.page==0:
                    preview=Story.from_buffer_copy(story)
            if story.status:
                break
        self.assertEqual(trace,expected)
        self.assertEqual(len(trace),51)
        self.assertEqual((story.status,story.next_field,story.error),(1,2,0))
        self.assertEqual(story.flags[1],4)
        self.assertIsNotNone(preview)
        self.render_preview(preview)

    def render_preview(self,story):
        pack,record=Pack(),Asset()
        self.assertEqual(self.lib.g3_pack_open(C.byref(pack),b"dist/switch/g3nx/g3nx.story"),1)
        canvas=C.create_string_buffer(bytes([0,0,0,255])*(640*480))
        layer=C.create_string_buffer(640*480*4)
        try:
            self.assertEqual(pack.count,self.lib.g3_story_asset_count())
            for kind,ident,dx in ((1,story.background,0),(2,story.left,-150),(2,story.right,150),
                                  (2,story.center,0),(3,story.dialog,0)):
                if ident<0: continue
                index=self.lib.g3_story_asset(kind,ident,story.page if kind==3 else 0)
                self.assertGreaterEqual(index,0)
                self.assertEqual(self.lib.g3_pack_record(C.byref(pack),index,C.byref(record)),1)
                self.assertEqual((record.width,record.height),(640,480))
                self.assertEqual(self.lib.g3_pack_pixels(C.byref(pack),C.byref(record),layer,640*480*4),1)
                self.lib.g3_story_blit(canvas,layer,dx)
        finally:
            self.lib.g3_pack_close(C.byref(pack))
        screen=(C.c_uint32*(1280*720))()
        self.lib.g3_menu_render(screen,1280,canvas)
        Image.frombytes("RGBA",(1280,720),bytes(screen)).save("dist/switch/g3nx/story-native-preview.png")

    def test_all_story_layers_match_the_runtime_canvas(self):
        pack,record=Pack(),Asset()
        self.assertEqual(self.lib.g3_pack_open(C.byref(pack),b"dist/switch/g3nx/g3nx.story"),1)
        try:
            for index in range(1,pack.count):
                self.assertEqual(self.lib.g3_pack_record(C.byref(pack),index,C.byref(record)),1)
                self.assertEqual((record.width,record.height),(640,480),record.name)
        finally:self.lib.g3_pack_close(C.byref(pack))

    def test_checkpoint_roundtrip_and_corrupt_latest_fallback(self):
        story=self.first_dialog()
        with tempfile.TemporaryDirectory(dir="homebrew/build") as directory:
            relative=Path(directory).resolve().relative_to(Path.cwd()).as_posix().encode("ascii")
            self.assertEqual(self.lib.g3_story_save(C.byref(story),relative),1)
            old=bytes(story)
            self.next_dialog(story)
            self.assertEqual(self.lib.g3_story_save(C.byref(story),relative),1)
            restored=Story()
            self.assertEqual(self.lib.g3_story_load(C.byref(restored),relative),1)
            self.assertEqual(bytes(story),bytes(restored))
            latest=Path(directory)/"g3nx-progress-0.bin"
            corrupt=bytearray(latest.read_bytes());corrupt[-1]^=1;latest.write_bytes(corrupt)
            self.assertEqual(self.lib.g3_story_load(C.byref(restored),relative),1)
            self.assertEqual(bytes(restored),old)

    def test_invalid_checkpoints_leave_runtime_unchanged(self):
        story=self.first_dialog()
        with tempfile.TemporaryDirectory(dir="homebrew/build") as directory:
            relative=Path(directory).resolve().relative_to(Path.cwd()).as_posix().encode("ascii")
            before=bytes(story)
            self.assertEqual(self.lib.g3_story_load(C.byref(story),relative),0)
            self.assertEqual(bytes(story),before)
            self.assertEqual(self.lib.g3_story_save(C.byref(story),relative),1)
            file=Path(directory)/"g3nx-progress-1.bin"
            bad=bytearray(file.read_bytes())
            struct.pack_into("<I",bad,24,999)
            struct.pack_into("<I",bad,20,zlib.crc32(bad[:20]+bad[24:]))
            file.write_bytes(bad)
            self.assertEqual(self.lib.g3_story_load(C.byref(story),relative),0)
            self.assertEqual(bytes(story),before)
            story.dialog=999
            self.assertEqual(self.lib.g3_story_save(C.byref(story),relative),0)


if __name__=="__main__":
    unittest.main(verbosity=2)
