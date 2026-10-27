"""Fixed vectors for recovered RNG and the isolated original variation stage."""
import ctypes as C
from pathlib import Path
import unittest


class CombatArithmeticTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lib=C.CDLL(str(Path("homebrew/build/g3_formats.dll").resolve()))
        cls.lib.g3_original_rand.argtypes=[C.POINTER(C.c_uint32)]
        cls.lib.g3_original_rand.restype=C.c_uint32
        cls.lib.g3_original_damage_variation.argtypes=[C.c_int32,C.POINTER(C.c_uint32)]
        cls.lib.g3_original_damage_variation.restype=C.c_int32

    def test_original_rng_sequence(self):
        state=C.c_uint32(1)
        self.assertEqual([self.lib.g3_original_rand(C.byref(state)) for _ in range(10)],
                         [41,18467,6334,26500,19169,15724,11478,29358,26962,24464])

    def test_skip_rng_for_tiny_damage_or_healing(self):
        for base in (-2,-1,0,1,2):
            state=C.c_uint32(1)
            self.assertEqual(self.lib.g3_original_damage_variation(base,C.byref(state)),base)
            self.assertEqual(state.value,1)

    def test_original_signed_division_and_variation_stage(self):
        # First RNG output is 41; positive numerator IDIV has positive remainder
        # even when its divisor is negative. Don't replace this with abs(damage).
        for base,expected in ((3,3),(6,6),(300,291),(-300,-209),(-7,-5),
                              (2147483647,1789569747),(-2147483648,-1789569666)):
            state=C.c_uint32(1)
            self.assertEqual(self.lib.g3_original_damage_variation(base,C.byref(state)),expected)
            self.assertEqual(state.value,2745024)


if __name__=="__main__":
    unittest.main()
