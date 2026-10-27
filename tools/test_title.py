"""Verify reconstructed OBS spans, native menu transitions and actual C-rendered frames."""
import ctypes as C
from pathlib import Path
import random
import struct
import unittest
from PIL import Image
from g3_obs import decode_obs, decode_spans
from g3_image import GameImage
from g3_resources import parse_index, read_resource
from test_resources import Pack, Asset


class Menu(C.Structure):
    _fields_=[("screen",C.c_int),("selected",C.c_int),("episode",C.c_int),
              ("notice",C.c_int),("quit",C.c_int)]


class SpriteTests(unittest.TestCase):
    def test_640_stride_skip_is_converted_to_tight_pixels(self):
        payload=struct.pack("<HHBB",2,1,0,2)+bytes([10,11])+struct.pack("<HBB",637,0,1)+bytes([12])
        self.assertEqual(decode_spans(payload,3,2,0),bytes([0,10,11,12,0,0]))

    def test_truncated_and_out_of_bounds_spans_are_rejected(self):
        for data in (b"",b"\x01\0",struct.pack("<HHBB",1,3,0,1)+b"x"):
            with self.assertRaises(ValueError):
                decode_spans(data,3,2,0)
        with self.assertRaises(ValueError):
            decode_obs(struct.pack("<I",1))

    def test_original_title_sprites_and_transparency(self):
        with GameImage("Store/image.img") as image:
            records={r.name:r for r in parse_index(image.read("G3P1/Obss/Obss.idx"))}
            decoded={name:decode_obs(read_resource(image,"G3P1/Obss/Obss.idx",records[name],{}))
                     for name in ("0360.obs","0751.obs")}
        logo=decoded["0360.obs"][0]
        self.assertEqual((logo[0].width,logo[0].height,logo[0].x,logo[0].y),(230,87,-220,-48))
        self.assertEqual((logo[1].width,logo[1].height,logo[1].x,logo[1].y),(212,117,17,-78))
        self.assertEqual([len(group) for group in decoded["0751.obs"]],[5,15,15,15,15,16])
        for groups in decoded.values():
            for group in groups:
                for sprite in group:
                    rgba=sprite.rgba()
                    self.assertEqual(len(rgba),sprite.width*sprite.height*4)
                    for i in (0,len(sprite.indices)//2,len(sprite.indices)-1):
                        self.assertEqual(rgba[i*4+3],0 if sprite.indices[i]==sprite.transparent else 255)


class NativeFrontendTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lib=C.CDLL(str(Path("homebrew/build/g3_formats.dll").resolve()))
        cls.lib.g3_menu_init.argtypes=[C.POINTER(Menu)]
        cls.lib.g3_menu_input.argtypes=[C.POINTER(Menu),C.c_uint]
        cls.lib.g3_menu_scene.argtypes=[C.POINTER(Menu)]
        cls.lib.g3_menu_scene.restype=C.c_uint
        cls.lib.g3_menu_render.argtypes=[C.POINTER(C.c_uint32),C.c_size_t,C.c_void_p]
        cls.lib.g3_pack_open.argtypes=[C.POINTER(Pack),C.c_char_p]
        cls.lib.g3_pack_close.argtypes=[C.POINTER(Pack)]
        cls.lib.g3_pack_record.argtypes=[C.POINTER(Pack),C.c_uint32,C.POINTER(Asset)]
        cls.lib.g3_pack_pixels.argtypes=[C.POINTER(Pack),C.POINTER(Asset),C.c_void_p,C.c_size_t]

    def test_start_episodes_notices_and_back_navigation(self):
        menu=Menu()
        self.lib.g3_menu_init(C.byref(menu))
        self.assertEqual(self.lib.g3_menu_scene(C.byref(menu)),0)
        for button,scene in ((16,4),(8,5),(16,8),(32,5),(32,0),(2,1),(16,10),(32,1),
                             (2,2),(16,11),(16,2),(1,1),(1,0),(1,3)):
            self.lib.g3_menu_input(C.byref(menu),button)
            self.assertEqual(self.lib.g3_menu_scene(C.byref(menu)),scene)
        self.lib.g3_menu_input(C.byref(menu),16)
        self.assertEqual(menu.quit,1)
        self.lib.g3_menu_init(C.byref(menu))
        self.lib.g3_menu_input(C.byref(menu),64)
        self.assertEqual(menu.quit,1)

    def test_menu_scenes_remain_in_bounds(self):
        menu=Menu()
        self.lib.g3_menu_init(C.byref(menu))
        rng=random.Random(17)
        for _ in range(1000):
            self.lib.g3_menu_input(C.byref(menu),rng.choice((1,2,4,8,16,32)))
            self.assertLess(self.lib.g3_menu_scene(C.byref(menu)),12)
            if menu.quit:
                self.lib.g3_menu_init(C.byref(menu))

    def test_supported_story_and_load_requests(self):
        menu=Menu()
        self.lib.g3_menu_init(C.byref(menu))
        self.lib.g3_menu_input(C.byref(menu),16)
        self.lib.g3_menu_input(C.byref(menu),16)
        self.assertEqual(menu.screen,3)
        self.lib.g3_menu_input(C.byref(menu),32)
        self.assertEqual(menu.screen,1)
        self.lib.g3_menu_input(C.byref(menu),32)
        self.lib.g3_menu_input(C.byref(menu),2)
        self.lib.g3_menu_input(C.byref(menu),16)
        self.assertEqual(menu.screen,4)

    def test_title_pack_and_shared_native_renderer(self):
        pack,asset=Pack(),Asset()
        pixels=C.create_string_buffer(640*480*4)
        self.assertEqual(self.lib.g3_pack_open(C.byref(pack),b"dist/switch/g3nx/g3nx.title"),1)
        try:
            self.assertEqual(pack.count,12)
            for i in range(pack.count):
                self.assertEqual(self.lib.g3_pack_record(C.byref(pack),i,C.byref(asset)),1)
                self.assertEqual((asset.width,asset.height),(640,480))
                self.assertEqual(self.lib.g3_pack_pixels(C.byref(pack),C.byref(asset),pixels,len(pixels)),1)
            self.lib.g3_pack_record(C.byref(pack),0,C.byref(asset))
            self.lib.g3_pack_pixels(C.byref(pack),C.byref(asset),pixels,len(pixels))
        finally:
            self.lib.g3_pack_close(C.byref(pack))
        stride=1296
        screen=(C.c_uint32*(stride*720))(*([0x11223344]*(stride*720)))
        self.lib.g3_menu_render(screen,stride,pixels)
        for y in (0,1,300,719):
            self.assertEqual(screen[y*stride],0xff000000)
            self.assertEqual(screen[y*stride+1279],0xff000000)
            for x in (0,1,2,500,959):
                source=((y*2//3)*640+x*2//3)*4
                r,g,b=pixels.raw[source:source+3]
                self.assertEqual(screen[y*stride+160+x],0xff000000 | b<<16 | g<<8 | r)
            self.assertEqual(screen[y*stride+1280],0x11223344)
        Image.frombytes("RGBA",(stride,720),bytes(screen)).crop((0,0,1280,720)).save("dist/switch/g3nx/title-native-preview.png")


if __name__=="__main__":
    unittest.main(verbosity=2)
