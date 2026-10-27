"""Native field-2 command traces, simultaneous motion, renderer and campaign saves."""
import ctypes as C
from pathlib import Path
import struct
import tempfile
import unittest
import zlib
from PIL import Image
from g3_image import GameImage
from g3_scenario import parse_field
from g3_resources import parse_index,read_resource
from g3_obs import decode_obs_actions
from test_story import Story
from test_resources import Pack


class Actor(C.Structure):
    _fields_=[(n,C.c_int32) for n in 'x y action clock mirror visible dx dy remaining loop'.split()]
class Field(C.Structure):
    _fields_=[('view',Story)]+[(n,C.c_int32) for n in 'camera_x camera_y camera_dx camera_dy camera_remaining'.split()]+[('actors',Actor*4)]
class Progress(C.Structure):
    _fields_=[('stage',C.c_int32),('story',Story),('field',Field)]
class Renderer(C.Structure):
    _fields_=[('pack',C.POINTER(Pack)),('pixels',C.c_void_p)]


class FieldTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lib=C.CDLL(str(Path('homebrew/build/g3_formats.dll').resolve()))
        cls.lib.g3_field_init.argtypes=[C.POINTER(Field)]
        cls.lib.g3_field_tick.argtypes=[C.POINTER(Field),C.c_int]
        cls.lib.g3_field_valid.argtypes=[C.POINTER(Field)]
        cls.lib.g3_field_renderer_open.argtypes=[C.POINTER(Renderer),C.POINTER(Pack)]
        cls.lib.g3_field_renderer_close.argtypes=[C.POINTER(Renderer)]
        cls.lib.g3_field_render.argtypes=[C.POINTER(Renderer),C.POINTER(Field),C.c_void_p,C.c_size_t]
        cls.lib.g3_pack_open.argtypes=[C.POINTER(Pack),C.c_char_p]
        cls.lib.g3_pack_close.argtypes=[C.POINTER(Pack)]
        cls.lib.g3_progress_init.argtypes=[C.POINTER(Progress)]
        cls.lib.g3_progress_save.argtypes=[C.POINTER(Progress),C.c_char_p]
        cls.lib.g3_progress_load.argtypes=[C.POINTER(Progress),C.c_char_p]
        cls.lib.g3_story_save.argtypes=[C.POINTER(Story),C.c_char_p]

    def field(self):
        f=Field();self.lib.g3_field_init(C.byref(f));return f

    def test_original_dialogue_order_and_completed_movement(self):
        with GameImage('Store/image.img') as image:
            program=parse_field(image.read('G3P1/Flds/0002.FLD'))
        expected=[a[2] for e in program['events'] for a in e['actions'] if a[0] in (200,201,202)]
        f=self.field();trace=[];last=None
        for _ in range(20000):
            self.lib.g3_field_tick(C.byref(f),1)
            self.assertEqual(self.lib.g3_field_valid(C.byref(f)),1)
            if f.view.wait:
                key=(f.view.active,f.view.pc)
                if key!=last:trace.append(f.view.dialog);last=key
            if f.view.status:break
        self.assertEqual((f.view.status,f.view.error),(1,0))
        self.assertEqual(trace,expected)
        self.assertEqual(f.camera_x,650)
        self.assertEqual([(a.x,a.y) for a in f.actors],[(1205,321),(1252,302),(1190,392),(1231,426)])

    def test_four_actors_move_together_and_barrier_waits(self):
        f=self.field()
        for _ in range(200):
            self.lib.g3_field_tick(C.byref(f),0)
            if f.actors[0].remaining:break
        self.assertEqual([a.remaining for a in f.actors],[90]*4)
        pc=f.view.pc;initial=[a.y for a in f.actors]
        self.lib.g3_field_tick(C.byref(f),0)
        self.assertEqual([a.y for a in f.actors],[y-4 for y in initial])
        self.assertEqual(f.view.pc,pc)
        for _ in range(88):self.lib.g3_field_tick(C.byref(f),0)
        self.assertFalse(f.view.wait)
        self.lib.g3_field_tick(C.byref(f),0)
        self.assertEqual([a.remaining for a in f.actors],[0]*4)
        self.assertTrue(f.view.wait)

    def test_renderer_matches_original_background_and_actor_pixels(self):
        f=self.field()
        # Choose an idle frame with actors visible in the source BGR, no UI overlays.
        for i,a in enumerate(f.actors):a.x=150+i*100;a.y=200+i*40
        with GameImage('Store/image.img') as image:
            import io
            idx='G3P1/BGRs/BGRs.idx';rs={r.name:r for r in parse_index(image.read(idx))}
            expected=Image.open(io.BytesIO(read_resource(image,idx,rs['0093.bgr'],{}))).convert('RGBA').crop((0,0,640,480))
            idx='G3P1/Obss/Obss.idx';rs={r.name:r for r in parse_index(image.read(idx))}
            for a,number in zip(f.actors,(196,352,674,682)):
                groups,_,_=decode_obs_actions(read_resource(image,idx,rs[f'{number:04d}.obs'],{}))
                sprite=groups[0][0];frame=Image.frombytes('RGBA',(sprite.width,sprite.height),sprite.rgba())
                expected.alpha_composite(frame,(a.x+sprite.x,a.y+sprite.y))
        pack=Pack();r=Renderer();canvas=C.create_string_buffer(640*480*4)
        self.assertEqual(self.lib.g3_pack_open(C.byref(pack),b'dist/switch/g3nx/g3nx.field2'),1)
        try:
            self.assertEqual(self.lib.g3_field_renderer_open(C.byref(r),C.byref(pack)),1)
            self.assertEqual(self.lib.g3_field_render(C.byref(r),C.byref(f),canvas,len(canvas)-1),0)
            self.assertEqual(self.lib.g3_field_render(C.byref(r),C.byref(f),canvas,len(canvas)),1)
            self.assertEqual(canvas.raw,expected.tobytes())
            Image.frombytes('RGBA',(640,480),canvas.raw).save('dist/switch/g3nx/field2-native-preview.png')
        finally:self.lib.g3_field_renderer_close(C.byref(r));self.lib.g3_pack_close(C.byref(pack))

    def test_progress_roundtrip_resumes_inflight_motion_and_corrupt_fallback(self):
        p=Progress();self.lib.g3_progress_init(C.byref(p));p.stage=1
        while not p.field.actors[0].remaining:self.lib.g3_field_tick(C.byref(p.field),0)
        with tempfile.TemporaryDirectory() as d:
            path=d.encode();self.assertEqual(self.lib.g3_progress_save(C.byref(p),path),1)
            before=bytes(p)
            self.lib.g3_field_tick(C.byref(p.field),0)
            self.assertEqual(self.lib.g3_progress_save(C.byref(p),path),1)
            q=Progress();self.assertEqual(self.lib.g3_progress_load(C.byref(q),path),1);self.assertEqual(bytes(p),bytes(q))
            for _ in range(20):
                self.lib.g3_field_tick(C.byref(p.field),0);self.lib.g3_field_tick(C.byref(q.field),0)
                self.assertEqual(bytes(p),bytes(q))
            latest=Path(d)/'g3nx-campaign-0.bin';b=bytearray(latest.read_bytes());b[-1]^=1;latest.write_bytes(b)
            self.assertEqual(self.lib.g3_progress_load(C.byref(q),path),1);self.assertEqual(bytes(q),before)

    def test_progress_alternating_slots_and_interrupted_temporary_write(self):
        p=Progress();self.lib.g3_progress_init(C.byref(p));p.stage=1
        with tempfile.TemporaryDirectory() as d:
            path=d.encode()
            for _ in range(6):
                self.lib.g3_field_tick(C.byref(p.field),0)
                self.assertEqual(self.lib.g3_progress_save(C.byref(p),path),1)
            (Path(d)/'g3nx-campaign-1.bin.tmp').write_bytes(b'interrupted')
            q=Progress();self.assertEqual(self.lib.g3_progress_load(C.byref(q),path),1)
            self.assertEqual(bytes(q),bytes(p))

    def test_mirrored_and_clipped_actor_rendering_keeps_buffer_guards(self):
        f=self.field();f.camera_x=650;f.actors[0].x=655;f.actors[0].y=10;f.actors[0].mirror=1
        f.actors[1].x=1290;f.actors[1].y=490
        pack=Pack();r=Renderer()
        self.assertEqual(self.lib.g3_pack_open(C.byref(pack),b'dist/switch/g3nx/g3nx.field2'),1)
        guard=C.create_string_buffer(b'\xa5'*(640*480*4+32),640*480*4+32)
        try:
            self.assertEqual(self.lib.g3_field_renderer_open(C.byref(r),C.byref(pack)),1)
            self.assertEqual(self.lib.g3_field_render(C.byref(r),C.byref(f),C.byref(guard,16),640*480*4),1)
            self.assertEqual(guard.raw[:16]+guard.raw[-16:],b'\xa5'*32)
        finally:self.lib.g3_field_renderer_close(C.byref(r));self.lib.g3_pack_close(C.byref(pack))

    def test_crc_valid_invalid_state_and_legacy_migration(self):
        p=Progress();self.lib.g3_progress_init(C.byref(p))
        with tempfile.TemporaryDirectory() as d:
            path=d.encode()
            self.assertEqual(self.lib.g3_story_save(C.byref(p.story),path),1)
            q=Progress();self.assertEqual(self.lib.g3_progress_load(C.byref(q),path),1);self.assertEqual(q.stage,0)
            self.assertEqual(self.lib.g3_progress_save(C.byref(p),path),1)
            file=Path(d)/'g3nx-campaign-1.bin';b=bytearray(file.read_bytes())
            struct.pack_into('<I',b,24,999);struct.pack_into('<I',b,20,zlib.crc32(b[:20]+b[24:]));file.write_bytes(b)
            before=bytes(q);self.assertEqual(self.lib.g3_progress_load(C.byref(q),path),0);self.assertEqual(bytes(q),before)
            p.field.actors[0].remaining=-1
            self.assertEqual(self.lib.g3_progress_save(C.byref(p),path),0)


if __name__=='__main__':unittest.main()
