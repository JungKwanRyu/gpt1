"""First-battle events, AI and deterministic mid-action checkpoint replay."""
import ctypes as C
from pathlib import Path
import tempfile
import unittest
import zlib
import struct
import test_navigation as navigation_tests
from test_navigation import Navigation
NavigationFixture=navigation_tests.NavigationTests
from test_field import Progress
from test_story import Story
from g3_image import GameImage
from g3_scenario import parse_field

class SessionTests(NavigationFixture):
    # Reuse the real asset fixture and ABI, without rerunning inherited cases.
    @classmethod
    def setUpClass(cls):
        super().setUpClass()
        for name in ('g3_navigation_valid','g3_battle_events_tick'):
            getattr(cls.lib,name).argtypes=[C.POINTER(Navigation)]+([C.c_int] if name.endswith('tick') else [])
        cls.lib.g3_navigation_export.argtypes=[C.POINTER(Navigation),C.POINTER(C.c_uint32),C.c_uint]
        cls.lib.g3_navigation_import.argtypes=cls.lib.g3_navigation_export.argtypes
        cls.lib.g3_progress_init.argtypes=[C.POINTER(Progress)]
        cls.lib.g3_aftermath_init.argtypes=[C.POINTER(Story)]
        cls.lib.g3_aftermath_tick.argtypes=[C.POINTER(Story),C.c_int]
        cls.lib.g3_aftermath_valid.argtypes=[C.POINTER(Story)]
        cls.lib.g3_session_save.argtypes=[C.c_int,C.POINTER(Progress),C.POINTER(Navigation),C.POINTER(Story),C.c_char_p]
        cls.lib.g3_session_load.argtypes=[C.POINTER(C.c_int),C.POINTER(Progress),C.POINTER(Navigation),C.POINTER(Story),C.c_char_p]

    def words(self,n):
        out=(C.c_uint32*483)()
        self.assertEqual(self.lib.g3_navigation_export(C.byref(n),out,483),1)
        return bytes(out)

    def test_save_resume_midwalk_midattack_and_dialogue(self):
        for mode in ('walk','attack','effects','dialogue'):
            with self.subTest(mode=mode),tempfile.TemporaryDirectory() as folder:
                n=self.open_navigation(combat=True);p=Progress();a=Story()
                self.lib.g3_progress_init(C.byref(p));self.lib.g3_aftermath_init(C.byref(a))
                if mode=='walk':self.lib.g3_navigation_input(C.byref(n),4|16)
                elif mode in ('attack','effects'):
                    n.units[4].x,n.units[4].y=38,34;n.cursor_x,n.cursor_y=38,34
                    self.lib.g3_navigation_input(C.byref(n),16)
                else:self.lib.g3_navigation_reset(C.byref(n));self.lib.g3_navigation_tick(C.byref(n))
                for _ in range(19 if mode=='effects' else 3):self.assertEqual(self.lib.g3_navigation_tick(C.byref(n)),1)
                if mode=='effects':
                    self.assertTrue(any(e.model for e in n.effects))
                    canvas=C.create_string_buffer(640*480*4)
                    self.assertEqual(self.lib.g3_navigation_render(C.byref(n),canvas,len(canvas)),1)
                saved=self.words(n)
                self.assertEqual(self.lib.g3_session_save(1,C.byref(p),C.byref(n),C.byref(a),folder.encode()),1)
                for _ in range(90):self.lib.g3_navigation_tick(C.byref(n))
                expected=self.words(n)
                stage=C.c_int(-1)
                self.assertEqual(self.lib.g3_session_load(C.byref(stage),C.byref(p),C.byref(n),C.byref(a),folder.encode()),1)
                self.assertEqual(stage.value,1);self.assertEqual(self.words(n),saved)
                for _ in range(90):self.lib.g3_navigation_tick(C.byref(n))
                self.assertEqual(self.words(n),expected)

    def test_checkpoint_corruption_fallback_and_atomic_invalid_import(self):
        n=self.open_navigation(combat=True);p=Progress();a=Story()
        self.lib.g3_progress_init(C.byref(p));self.lib.g3_aftermath_init(C.byref(a))
        with tempfile.TemporaryDirectory() as folder:
            directory=folder.encode();stage=C.c_int()
            self.assertEqual(self.lib.g3_session_save(1,C.byref(p),C.byref(n),C.byref(a),directory),1)
            old=self.words(n);self.lib.g3_navigation_tick(C.byref(n))
            self.assertEqual(self.lib.g3_session_save(1,C.byref(p),C.byref(n),C.byref(a),directory),1)
            path=Path(folder,'g3nx-session-0.bin');b=bytearray(path.read_bytes());b[-1]^=1;path.write_bytes(b)
            self.assertEqual(self.lib.g3_session_load(C.byref(stage),C.byref(p),C.byref(n),C.byref(a),directory),1)
            self.assertEqual(self.words(n),old)
            Path(folder,'g3nx-session-1.bin').write_bytes(b'broken')
            self.assertEqual(self.lib.g3_session_load(C.byref(stage),C.byref(p),C.byref(n),C.byref(a),directory),0)
            self.assertEqual(self.words(n),old)
        for at,value in ((0,22),(9,22),(20,99999),(253,99999),(17+220+4,0xffffffff)):
            w=(C.c_uint32*483).from_buffer_copy(old);w[at]=value
            result=self.lib.g3_navigation_import(C.byref(n),w,483)
            self.assertEqual(result,0,(at,value));self.assertEqual(self.words(n),old)

    def test_legacy_campaign_migrates_only_when_new_slots_are_absent(self):
        n=self.open_navigation(combat=True);p=Progress();a=Story();stage=C.c_int(-1)
        self.lib.g3_progress_init(C.byref(p));self.lib.g3_aftermath_init(C.byref(a))
        self.lib.g3_progress_save.argtypes=[C.POINTER(Progress),C.c_char_p]
        with tempfile.TemporaryDirectory() as folder:
            directory=folder.encode()
            self.assertEqual(self.lib.g3_progress_save(C.byref(p),directory),1)
            self.assertEqual(self.lib.g3_session_load(C.byref(stage),C.byref(p),C.byref(n),C.byref(a),directory),1)
            self.assertEqual(stage.value,0)
            self.assertEqual(self.lib.g3_session_save(1,C.byref(p),C.byref(n),C.byref(a),directory),1)
            path=Path(folder,'g3nx-session-1.bin');raw=bytearray(path.read_bytes())
            # An out-of-bounds unit index with a valid CRC must still fail validation.
            struct.pack_into('<I',raw,24+4+872+344,22)
            struct.pack_into('<I',raw,20,zlib.crc32(raw[:20]+raw[24:]))
            path.write_bytes(raw);before=self.words(n)
            self.assertEqual(self.lib.g3_session_load(C.byref(stage),C.byref(p),C.byref(n),C.byref(a),directory),0)
            self.assertEqual(self.words(n),before)

    def test_original_exit_loss_and_pacification_events(self):
        n=self.open_navigation(combat=True)
        # The actual BTL rectangle is inclusive x=6..16, y=0..3.
        n.units[0].x,n.units[0].y=6,3
        seen=[]
        for _ in range(8):
            self.assertEqual(self.lib.g3_battle_events_tick(C.byref(n),1),1)
            if n.events.wait:seen.append(n.events.dialog)
            if n.events.outcome>=0:break
        self.assertEqual(seen,[3,4]);self.assertEqual(n.events.outcome,0)
        self.lib.g3_navigation_reset(C.byref(n))
        for i in range(4):n.combat.hp[i]=n.units[i].hp=0
        self.assertEqual(self.lib.g3_battle_events_tick(C.byref(n),1),1)
        self.assertEqual(n.events.outcome,3)
        n=self.open_navigation(combat=True)
        boss=n.units[19];n.units[0].x,n.units[0].y=boss.x-2,boss.y
        for _ in range(10):self.assertEqual(self.lib.g3_battle_events_tick(C.byref(n),1),1)
        self.assertEqual((boss.team,n.combat.stats[19].team),(3,3))

    def test_original_obs_hit_frames_and_sound_commands(self):
        n=self.open_navigation(combat=True)
        n.units[4].x,n.units[4].y=38,34;n.cursor_x,n.cursor_y=38,34
        self.assertEqual(self.lib.g3_navigation_input(C.byref(n),16),1)
        self.lib.g3_navigation_sounds.argtypes=[C.POINTER(Navigation),C.POINTER(C.c_int),C.c_uint]
        hits=[];sounds=[];last=0
        for tick in range(40):
            self.assertEqual(self.lib.g3_navigation_input(C.byref(n),0),1)
            self.assertEqual(self.lib.g3_navigation_tick(C.byref(n)),1)
            if n.combat.last_hits!=last:hits.append(tick);last=n.combat.last_hits
            out=(C.c_int*8)();count=self.lib.g3_navigation_sounds(C.byref(n),out,8)
            sounds.extend((tick,out[i]) for i in range(count))
            if not n.motion:break
        self.assertEqual(hits,[18,21])
        self.assertEqual(sounds,[(15,96),(17,23)])

    def test_enemy_ai_acts_without_player_input_and_replay_matches(self):
        n=self.open_navigation(combat=True);n.ai_enabled=1
        # Pass player turns until the first enemy is scheduled.
        for _ in range(20):
            if n.units[n.combat.active].team!=4:break
            self.lib.g3_navigation_input(C.byref(n),256)
        self.assertEqual(n.units[n.combat.active].team,0)
        before=self.words(n);positions=[(u.x,u.y) for u in n.units];rng=n.combat.rng
        for _ in range(250):self.assertEqual(self.lib.g3_navigation_tick(C.byref(n)),1)
        self.assertNotEqual([(u.x,u.y) for u in n.units],positions)
        self.assertNotEqual(n.combat.rng,rng)
        after=self.words(n)
        w=(C.c_uint32*483).from_buffer_copy(before)
        self.assertEqual(self.lib.g3_navigation_import(C.byref(n),w,483),1)
        for _ in range(250):self.assertEqual(self.lib.g3_navigation_tick(C.byref(n)),1)
        self.assertEqual(self.words(n),after)

    def test_real_initial_layout_can_reach_exit_with_ai_and_midbattle_reload(self):
        n=self.open_navigation(combat=True);n.ai_enabled=1
        p=Progress();a=Story();self.lib.g3_progress_init(C.byref(p));self.lib.g3_aftermath_init(C.byref(a))
        cost=(C.c_int32*4096)();prev=(C.c_int32*4096)();size=n.map.width*n.map.height
        self.assertEqual(self.lib.g3_map_routes(C.byref(n.map),10,2,0,65535,None,size,cost,prev),1)
        # Only normal controller inputs; no changed coordinates, stats, HP, team or RNG.
        with tempfile.TemporaryDirectory() as folder:
            for tick in range(15000):
                if n.events.wait:self.assertEqual(self.lib.g3_navigation_input(C.byref(n),16),1)
                elif n.events.outcome>=0:break
                elif not n.motion and n.events.ready and n.units[n.combat.active].team==4:
                    actor=n.combat.active
                    targets=[i for i in range(22) if self.lib.g3_navigation_attackable(C.byref(n),i)]
                    if targets:
                        i=min(targets,key=lambda j:n.combat.hp[j]);n.cursor_x,n.cursor_y=n.units[i].x,n.units[i].y
                        self.assertEqual(self.lib.g3_navigation_input(C.byref(n),16),1)
                    elif n.combat.move[actor]:
                        options=sorted((cost[i],i) for i in range(size) if n.cost[i]>0 and cost[i]>=0)
                        for _,i in options:
                            n.cursor_x,n.cursor_y=i%n.map.width,i//n.map.width
                            self.assertEqual(self.lib.g3_navigation_input(C.byref(n),16),1)
                            if n.motion:break
                        if not n.motion:self.assertEqual(self.lib.g3_navigation_input(C.byref(n),256),1)
                    else:self.assertEqual(self.lib.g3_navigation_input(C.byref(n),256),1)
                else:self.assertEqual(self.lib.g3_navigation_input(C.byref(n),0),1)
                self.assertEqual(self.lib.g3_navigation_tick(C.byref(n)),1)
                if tick==2000:
                    before=self.words(n)
                    self.assertEqual(self.lib.g3_session_save(1,C.byref(p),C.byref(n),C.byref(a),folder.encode()),1)
                    stage=C.c_int(-1)
                    self.assertEqual(self.lib.g3_session_load(C.byref(stage),C.byref(p),C.byref(n),C.byref(a),folder.encode()),1)
                    self.assertEqual(self.words(n),before)
            self.assertEqual(n.events.outcome,0)
            self.assertTrue(any(u.team==4 and u.hp>0 and 6<=u.x<=16 and 0<=u.y<=3 for u in n.units))
            self.assertTrue(any(n.combat.hp[i]>0 and n.units[i].team==0 for i in range(22)))

    def test_field3_original_dialogue_and_checkpoint(self):
        a=Story();p=Progress();n=self.open_navigation(combat=True)
        self.lib.g3_aftermath_init(C.byref(a));self.lib.g3_progress_init(C.byref(p))
        with GameImage('Store/image.img') as image:
            program=parse_field(image.read('G3P1/Flds/0003.FLD'))
        expected=[c[2] for e in program['events'] for c in e['actions'] if c[0] in (200,201,202)]
        trace=[];last=None
        with tempfile.TemporaryDirectory() as folder:
            for tick in range(30000):
                self.lib.g3_aftermath_tick(C.byref(a),1)
                self.assertEqual(self.lib.g3_aftermath_valid(C.byref(a)),1)
                if a.wait and (a.active,a.pc)!=last:trace.append(a.dialog);last=(a.active,a.pc)
                if tick==10:
                    state=bytes(a)
                    self.assertEqual(self.lib.g3_session_save(2,C.byref(p),C.byref(n),C.byref(a),folder.encode()),1)
                    self.lib.g3_aftermath_tick(C.byref(a),1);stage=C.c_int(-1)
                    self.assertEqual(self.lib.g3_session_load(C.byref(stage),C.byref(p),C.byref(n),C.byref(a),folder.encode()),1)
                    self.assertEqual((stage.value,bytes(a)),(2,state))
                if a.status:break
        self.assertEqual((a.status,a.error),(1,0));self.assertEqual(trace,expected)

# Inherited tests already run in test_navigation.
for _name in list(vars(NavigationFixture)):
    if _name.startswith('test_'):setattr(SessionTests,_name,None)
del NavigationFixture
if __name__=='__main__':unittest.main()
