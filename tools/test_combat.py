"""Fixed original arithmetic/turn vectors and transactional manual-combat actions."""
import ctypes as C
from pathlib import Path
import unittest

STAT_FIELDS=('max_hp','strength','dexterity','armor','power','modifier','accuracy','critical',
             'block_accuracy','block_defense','flags','period','team','attack_id','hits','bonus',
             'range','min_range','height_cost','element','secondary')


class CombatStats(C.Structure):
    _fields_=[(key,C.c_int32) for key in STAT_FIELDS]+[('scales',C.c_int32*9)]


class Combat(C.Structure):
    _fields_=[('stats',CombatStats*22)]+[(key,C.c_int32*22) for key in ('hp','move','attack')]+[
        (key,C.c_uint32) for key in ('count','clock','phase','rng')]+[
        (key,C.c_int32) for key in ('active','last_source','last_target','last_damage','last_hits',
                                   'last_critical','last_state','status')]


def fighter(team=4,period=8,**kwargs):
    result=CombatStats(max_hp=1000,strength=100,dexterity=100,armor=100,power=40,
                       accuracy=100,period=period,team=team,attack_id=1,hits=1,range=8,
                       min_range=5,height_cost=1,secondary=1)
    result.scales[:]=[10]*9
    for key,value in kwargs.items():setattr(result,key,value)
    return result


class CombatTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lib=C.CDLL(str(Path('homebrew/build/g3_formats.dll').resolve()))
        cls.lib.g3_combat_morale_stat.argtypes=[C.c_int32,C.c_int32]
        cls.lib.g3_combat_morale_stat.restype=C.c_int32
        cls.lib.g3_combat_init.argtypes=[C.POINTER(Combat),C.POINTER(CombatStats),C.c_uint,C.c_uint32]
        for name in ('g3_combat_next','g3_combat_pass','g3_combat_moved'):
            getattr(cls.lib,name).argtypes=[C.POINTER(Combat)]
        cls.lib.g3_combat_strike.argtypes=[C.POINTER(Combat),C.c_uint]

    def battle(self,*stats):
        c=Combat();rows=(CombatStats*len(stats))(*stats)
        self.assertEqual(self.lib.g3_combat_init(C.byref(c),rows,len(stats),1),1)
        return c

    def test_morale_integer_rounding_and_clamp(self):
        for stat,morale,expected in ((147,80,113),(150,80,115),(60,80,46),(135,80,104),
                                     (100,150,100),(100,0,50),(1,0,1),(100,-200,0)):
            self.assertEqual(self.lib.g3_combat_morale_stat(stat,morale),expected)

    def test_period_team_and_index_order(self):
        periods=[9,8,11,8]+[13]*15+[12]*3
        c=self.battle(*(fighter(4 if i<4 else 0,period) for i,period in enumerate(periods)))
        observed=[]
        for _ in range(25):
            observed.append((c.clock,c.phase,c.active))
            self.assertEqual(self.lib.g3_combat_pass(C.byref(c)),1)
        expected=[(8,4,1),(8,4,3),(9,4,0),(11,4,2)]+[(12,0,i) for i in range(19,22)]+[
            (13,0,i) for i in range(4,19)]+[(16,4,1),(16,4,3),(18,4,0)]
        self.assertEqual(observed,expected)

    def test_same_tick_enemy_phase_precedes_players_and_flags_reset(self):
        c=self.battle(fighter(0,8),fighter(4,8))
        self.assertEqual((c.clock,c.phase,c.active),(8,0,0))
        self.lib.g3_combat_pass(C.byref(c))
        self.assertEqual((c.clock,c.phase,c.active),(8,4,1))
        self.lib.g3_combat_pass(C.byref(c))
        self.assertEqual((c.clock,c.phase,c.active,c.move[1],c.attack[1]),(16,0,0,1,1))

    def test_original_double_hit_vector_and_rng(self):
        c=self.battle(fighter(4,9,strength=147,dexterity=135,armor=150,accuracy=120,
                              max_hp=1000,critical=8,hits=2),
                      fighter(0,13,max_hp=300,strength=60,dexterity=60,armor=80,power=30,accuracy=70,critical=12))
        self.assertEqual(self.lib.g3_combat_strike(C.byref(c),1),1)
        # STR=113, armor=62 -> base 91. RNG [41,18467,6334,26500]:
        # first 91+11-15=87; second (91+4-15)*2=160.
        self.assertEqual((c.hp[1],c.last_damage,c.last_hits,c.last_critical),(53,247,2,1))
        self.assertEqual(c.rng,3884216597)
        self.assertEqual((c.active,c.move[0],c.attack[0]),(0,1,0))
        before=bytes(c)
        self.assertEqual(self.lib.g3_combat_strike(C.byref(c),1),0)
        self.assertEqual(bytes(c),before)
        self.assertEqual(self.lib.g3_combat_moved(C.byref(c)),1)
        self.assertEqual((c.clock,c.active),(13,1))

    def test_move_then_attack_advances_and_dead_unit_is_not_scheduled(self):
        c=self.battle(fighter(),fighter(0,period=13,max_hp=10))
        self.assertEqual(self.lib.g3_combat_moved(C.byref(c)),1)
        self.assertEqual((c.active,c.move[0],c.attack[0]),(0,0,1))
        self.assertEqual(self.lib.g3_combat_strike(C.byref(c),1),1)
        self.assertEqual((c.hp[1],c.active,c.status),(0,-1,1))
        before=bytes(c)
        self.assertEqual(self.lib.g3_combat_pass(C.byref(c)),0)
        self.assertEqual(bytes(c),before)

    def test_passive_dodge_block_and_unblockable_rng_paths(self):
        c=self.battle(fighter(),fighter(0,period=13,dexterity=400,flags=1))
        self.lib.g3_combat_strike(C.byref(c),1)
        self.assertEqual((c.last_damage,c.last_state,c.rng),(0,5,2745024))
        c=self.battle(fighter(),fighter(0,period=13,flags=2,block_accuracy=200,block_defense=50))
        self.lib.g3_combat_strike(C.byref(c),1)
        self.assertEqual((c.last_damage,c.last_state,c.last_critical),(32,4,0))
        self.assertEqual(c.rng,3357800067)
        c=self.battle(fighter(flags=8),fighter(0,period=13,dexterity=400,flags=1|2,block_accuracy=1000))
        self.lib.g3_combat_strike(C.byref(c),1)
        self.assertEqual((c.last_damage,c.last_state),(51,2))

    def test_element_and_secondary_scaling_precedes_defense_and_variation(self):
        a=fighter(element=1,secondary=2);d=fighter(0,period=13)
        d.scales[0]=12;d.scales[6]=20
        c=self.battle(a,d);self.lib.g3_combat_strike(C.byref(c),1)
        # 3850*12/10*20/10 /77 =120; variation +41%40-20 =>101.
        self.assertEqual(c.last_damage,101)

    def test_invalid_actions_and_unsupported_inputs_preserve_state(self):
        c=self.battle(fighter(),fighter(0,period=13))
        before=bytes(c)
        for target in (0,2,22,0xffffffff):
            self.assertEqual(self.lib.g3_combat_strike(C.byref(c),target),0)
            self.assertEqual(bytes(c),before)
        for key,value in (('period',0),('period',1),('hits',0),('team',2),('critical',101),('min_range',9)):
            rows=(CombatStats*2)(fighter(**{key:value}),fighter(0,period=13))
            self.assertEqual(self.lib.g3_combat_init(C.byref(c),rows,2,1),0)
            self.assertEqual(bytes(c),before)


if __name__=='__main__':unittest.main()
