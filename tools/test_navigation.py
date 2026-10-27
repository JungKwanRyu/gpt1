"""Original-ground movement vectors and the integrated battle navigation state."""
import ctypes as C
from pathlib import Path
import shutil
import struct
import tempfile
import unittest
from PIL import Image
from g3_image import GameImage
from g3_rules import Rules, table
from test_battle import NativeMap
from test_combat import Combat


class Pack(C.Structure):
    _fields_ = [("file",C.c_void_p),("count",C.c_uint32),("file_size",C.c_uint64)]


class Unit(C.Structure):
    _fields_ = [(key,C.c_int32) for key in ("character","model","x","y","team","movement","hp","period")]


class Navigation(C.Structure):
    _fields_ = [("map_data",C.c_void_p),("map",NativeMap),("pack",Pack),
               ("images",C.c_void_p*10),("units",Unit*22),("remaining",C.c_int32*22),
               ("selected",C.c_int32),("cursor_x",C.c_int32),("cursor_y",C.c_int32),
               ("cost",C.c_int32*4096),("predecessor",C.c_int32*4096),
               ("combat",Combat),("combat_mode",C.c_int32),("active_clock",C.c_uint32)]


class NavigationTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lib = C.CDLL(str(Path("homebrew/build/g3_formats.dll").resolve()))
        cls.lib.g3_map_open.argtypes=[C.POINTER(NativeMap),C.c_void_p,C.c_size_t]
        cls.lib.g3_map_project.argtypes=[C.POINTER(NativeMap),C.c_int,C.c_int,C.POINTER(C.c_int),C.POINTER(C.c_int)]
        cls.lib.g3_map_step.argtypes=[C.POINTER(NativeMap)]+[C.c_int]*5
        cls.lib.g3_map_routes.argtypes=[C.POINTER(NativeMap)]+[C.c_int]*4+[
            C.c_void_p,C.c_size_t,C.POINTER(C.c_int32),C.POINTER(C.c_int32)]
        cls.lib.g3_navigation_open.argtypes=[C.POINTER(Navigation),C.c_char_p]
        cls.lib.g3_navigation_close.argtypes=[C.POINTER(Navigation)]
        cls.lib.g3_navigation_reset.argtypes=[C.POINTER(Navigation)]
        cls.lib.g3_navigation_input.argtypes=[C.POINTER(Navigation),C.c_uint]
        cls.lib.g3_navigation_render.argtypes=[C.POINTER(Navigation),C.c_void_p,C.c_size_t]
        cls.lib.g3_navigation_attackable.argtypes=[C.POINTER(Navigation),C.c_uint]

    def fixture(self,heights,attributes):
        raw = (struct.pack("<IHH",1,3,3)+bytes(heights)+bytes(attributes)+
               struct.pack("<HHhhHHII",1,1,0,-12,0,0,0,1)+bytes(640+1+64))
        buf=C.create_string_buffer(raw);m=NativeMap()
        self.assertEqual(self.lib.g3_map_open(C.byref(m),buf,len(raw)),1)
        return buf,m

    def test_projection_and_ground_edges(self):
        buf,m=self.fixture([0,1,3,0,2,2,0,0,0],[0,0,0,1,0,2,0,0,0])
        sx,sy=C.c_int(),C.c_int()
        self.assertEqual(self.lib.g3_map_project(C.byref(m),1,1,C.byref(sx),C.byref(sy)),1)
        self.assertEqual((sx.value,sy.value),(60,120)) # 48 - 24 + 96
        step=lambda x,y,tx,ty:self.lib.g3_map_step(C.byref(m),x,y,tx,ty,0)
        self.assertEqual(step(0,0,1,0),6)
        self.assertEqual(step(0,0,1,1),12)
        self.assertEqual(step(1,0,2,0),8)
        self.assertEqual(step(1,1,2,1),4) # bit 2 blocks flying, not ground
        self.assertEqual(step(0,0,0,1),-1)
        self.assertEqual(step(0,1,2,0),-1)
        self.assertEqual(step(0,0,0,0),-1)
        self.assertEqual(step(2,0,1,-1),-1)
        buf,m=self.fixture([0,3,0,0,0,0,0,0,0],[0]*9)
        self.assertEqual(step(0,0,1,0),-1) # height difference > 2

    def test_route_budget_detour_and_occupancy(self):
        buf,m=self.fixture([0]*9,[0,1,0,0,1,0,0,0,0])
        cost=(C.c_int32*9)();prev=(C.c_int32*9)()
        occupied=(C.c_uint8*9)()
        route=lambda budget:self.lib.g3_map_routes(C.byref(m),0,0,0,budget,occupied,9,cost,prev)
        self.assertEqual(route(23),1)
        self.assertEqual(cost[2],-1)
        self.assertEqual(route(24),1)
        self.assertEqual(cost[2],24)
        # Walk the returned route, independently summing measured step prices.
        at,total=2,0
        while prev[at]>=0:
            before=prev[at]
            total+=self.lib.g3_map_step(C.byref(m),before%3,before//3,at%3,at//3,0)
            at=before
        self.assertEqual((at,total),(0,24))
        occupied[6]=1
        self.assertEqual(route(100),1)
        self.assertEqual(cost[2],24) # diagonal edge: no inferred corner prohibition
        occupied[7]=1
        self.assertEqual(route(100),1)
        self.assertEqual(cost[2],-1)
        self.assertEqual(self.lib.g3_map_routes(C.byref(m),0,0,1,24,occupied,9,cost,prev),0)
        self.assertEqual(self.lib.g3_map_routes(C.byref(m),0,0,0,24,occupied,8,cost,prev),0)

    def open_navigation(self,combat=False):
        n=Navigation()
        self.assertEqual(self.lib.g3_navigation_open(C.byref(n),b"dist/switch/g3nx"),1)
        self.addCleanup(self.lib.g3_navigation_close,C.byref(n))
        if not combat:self.assertEqual(self.lib.g3_navigation_input(C.byref(n),1024),1)
        return n

    def test_manual_combat_entry_wait_focus_and_mode_reset(self):
        n=self.open_navigation(combat=True)
        self.assertEqual((n.combat_mode,n.selected,n.combat.clock),(1,1,8))
        n.cursor_x=0;n.cursor_y=0
        self.lib.g3_navigation_input(C.byref(n),128)
        self.assertEqual((n.cursor_x,n.cursor_y),(40,34))
        self.lib.g3_navigation_input(C.byref(n),256)
        self.assertEqual((n.selected,n.combat.clock),(3,8))
        self.lib.g3_navigation_input(C.byref(n),256)
        self.assertEqual((n.selected,n.combat.clock),(0,9))
        self.lib.g3_navigation_input(C.byref(n),512)
        self.assertEqual((n.selected,n.combat.clock),(1,8))
        self.lib.g3_navigation_input(C.byref(n),1024)
        self.assertEqual((n.combat_mode,n.selected),(0,0))

    def test_manual_combat_map_range_and_hp_integration(self):
        n=self.open_navigation(combat=True)
        # Place one existing enemy on a free cell two tiles west of Marzana.
        # This is a test fixture change, not a shipping alternate battle layout.
        n.units[4].x,n.units[4].y=38,34
        self.assertEqual(self.lib.g3_navigation_attackable(C.byref(n),4),1)
        n.cursor_x,n.cursor_y=38,34
        before=n.combat.hp[4]
        self.assertEqual(self.lib.g3_navigation_input(C.byref(n),16),1)
        self.assertLess(n.combat.hp[4],before)
        self.assertEqual(n.units[4].hp,n.combat.hp[4])
        self.assertEqual((n.combat.last_source,n.combat.last_target),(1,4))
        state=bytes(n.combat)
        self.lib.g3_navigation_input(C.byref(n),16)
        self.assertEqual(bytes(n.combat),state)
        self.lib.g3_navigation_input(C.byref(n),512)
        n.units[4].x,n.units[4].y=39,34 # cost 4, below original minimum 5
        self.assertEqual(self.lib.g3_navigation_attackable(C.byref(n),4),0)
        n.units[4].x,n.units[4].y=37,34 # cost 12, beyond maximum 8
        self.assertEqual(self.lib.g3_navigation_attackable(C.byref(n),4),0)
        self.assertEqual(self.lib.g3_navigation_attackable(C.byref(n),0),0)

    def test_combat_render_and_single_move_action(self):
        n=self.open_navigation(combat=True)
        # Marzana can move west onto a free original ground cell.
        self.lib.g3_navigation_input(C.byref(n),4|16)
        self.assertEqual((n.units[1].x,n.remaining[1],n.combat.move[1]),(39,0,0))
        before=(n.units[1].x,n.units[1].y)
        self.lib.g3_navigation_input(C.byref(n),4|16)
        self.assertEqual((n.units[1].x,n.units[1].y),before)
        out=C.create_string_buffer(640*480*4)
        self.assertEqual(self.lib.g3_navigation_render(C.byref(n),out,len(out)),1)
        Image.frombytes('RGBA',(640,480),out.raw).save('dist/battle6-combat-preview.png')

    def test_repeated_turn_of_same_survivor_restores_movement_budget(self):
        n=self.open_navigation(combat=True)
        # Different periods can make the same survivor act twice consecutively.
        n.combat.hp[:]=[0]*22
        n.combat.hp[0]=1000;n.combat.hp[4]=300
        n.combat.stats[0].period=2
        n.combat.clock=2;n.combat.phase=1;n.combat.active=0
        n.selected=0;n.active_clock=2;n.remaining[0]=0
        n.combat.move[0]=0;n.combat.attack[0]=1
        self.lib.g3_navigation_input(C.byref(n),256)
        self.assertEqual((n.selected,n.combat.clock,n.remaining[0],n.combat.move[0]),(0,4,24,1))

    def test_first_battle_move_budget_blocked_destination_and_reset(self):
        n=self.open_navigation()
        self.assertEqual([(u.x,u.y,u.movement,u.hp,u.period) for u in n.units[:4]],
                         [(39,35,24,1000,9),(40,34,24,500,8),(40,37,16,1300,11),(39,36,29,580,8)])
        # Occupied Marzana cell cannot be a destination in the workbench.
        self.assertEqual(n.cost[34*45+40],-1)
        self.assertEqual(n.cost[35*45+38],4)
        self.lib.g3_navigation_input(C.byref(n),4|16) # left + A
        self.assertEqual((n.units[0].x,n.units[0].y,n.remaining[0]),(38,35,20))
        self.lib.g3_navigation_input(C.byref(n),256) # next ally retains its own budget
        self.assertEqual((n.selected,n.cursor_x,n.cursor_y,n.remaining[1]),(1,40,34,24))
        self.lib.g3_navigation_input(C.byref(n),128)
        self.assertEqual(n.remaining[0],20)
        self.lib.g3_navigation_input(C.byref(n),512)
        self.assertEqual((n.selected,n.units[0].x,n.remaining[0]),(0,39,24))
        # Unreachable destination leaves the unit and its budget untouched.
        n.cursor_x,n.cursor_y=0,0
        self.lib.g3_navigation_input(C.byref(n),16)
        self.assertEqual((n.units[0].x,n.remaining[0]),(39,24))

    def test_render_buffer_guards_and_preview(self):
        n=self.open_navigation()
        capacity=640*480*4
        buf=C.create_string_buffer(b"\xa5"*(capacity+32),capacity+32)
        target=C.cast(C.byref(buf,16),C.c_void_p)
        self.assertEqual(self.lib.g3_navigation_render(C.byref(n),target,capacity-1),0)
        self.assertEqual(buf.raw,b"\xa5"*len(buf))
        self.assertEqual(self.lib.g3_navigation_render(C.byref(n),target,capacity),1)
        self.assertEqual(buf.raw[:16]+buf.raw[-16:],b"\xa5"*32)
        Image.frombytes("RGBA",(640,480),buf.raw[16:-16]).save("dist/battle6-navigation-preview.png")

    def test_map_mismatch_is_rejected_and_close_is_idempotent(self):
        with tempfile.TemporaryDirectory() as folder:
            data=bytearray(Path("dist/switch/g3nx/battle6.obt").read_bytes())
            data[100]^=1
            Path(folder,"battle6.obt").write_bytes(data)
            shutil.copyfile("dist/switch/g3nx/g3nx.battle6",Path(folder,"g3nx.battle6"))
            n=Navigation()
            self.assertEqual(self.lib.g3_navigation_open(C.byref(n),str(folder).encode()),0)
            self.assertFalse(n.map_data)
            self.lib.g3_navigation_close(C.byref(n))
            self.lib.g3_navigation_close(C.byref(n))


class OriginalStatsTests(unittest.TestCase):
    def test_measured_attack_records_and_variant_rejection(self):
        with GameImage('Store/image.img') as image:
            rules=Rules(image)
            for number,hits in ((1,1),(297,1),(352,2)):
                a=rules.physical_attack(number)
                self.assertEqual((a['kind'],a['range'],a['min_range'],a['height_cost'],a['hits'],a['morale_cost']),
                                 (0,8,5,1,hits,0))
            for number in (0,4,65535):
                with self.assertRaises(ValueError):rules.physical_attack(number)

    def test_first_battle_initial_stats(self):
        with GameImage("Store/image.img") as image:
            rules=Rules(image)
            expected={0:(1000,24,9),1:(500,24,8),2:(1300,16,11),5:(580,29,8),501:(300,20,13),505:(550,12,12)}
            for number,values in expected.items():
                c=rules.initial(image.read(f"G3P1/Chars/{number:04d}.CHR"))
                self.assertEqual((c["hp_max"],c["movement"],c["period"]),values)
                self.assertEqual((c["large"],c["flying"]),(0,0))
            self.assertEqual(rules.weapon(35),dict(name="다마스커스",power=40,defense=22,
                                                  accuracy=120,critical=8,element=0,modifier=0,multiplier=60))

    def test_strict_dat_lengths(self):
        for raw in (b"",b"\x01\0",struct.pack("<H",2)+bytes(39+2),struct.pack("<H",1)+bytes(39+3)):
            with self.assertRaises(ValueError):table(raw,39)


if __name__=="__main__":unittest.main()
