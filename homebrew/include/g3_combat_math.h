#ifndef G3_COMBAT_MATH_H
#define G3_COMBAT_MATH_H
#include <stdint.h>

/* Recovered arithmetic only, not a complete damage/turn/AI implementation.
 * A future battle state must share this RNG with every original RNG consumer. */
uint32_t g3_original_rand(uint32_t *state);
int32_t g3_original_damage_variation(int32_t base, uint32_t *state);
#endif
