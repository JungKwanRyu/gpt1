#include "g3_combat_math.h"

uint32_t g3_original_rand(uint32_t *state) {
    /* ALPHA.DLL 100E75F5..100E7616: unsigned wrap is intentional. */
    if (!state) return 0;
    *state=*state*214013u+2531011u;
    return (*state>>16)&32767u;
}

int32_t g3_original_damage_variation(int32_t base,uint32_t *state) {
    /* 10041643..10041671. Signed IDIV truncates toward zero, including healing.
     * For |base|<3 the original skips the RNG call entirely. This is the stage
     * BEFORE status multipliers and critical effects, not final damage. */
    int32_t divisor=base/3;
    if (!divisor) return base;
    int32_t remainder=(int32_t)g3_original_rand(state)%divisor;
    return (int32_t)((int64_t)base+remainder-base/6);
}
