/* Field 3 has no actors and uses the same synchronous FLD instruction set.
 * Compile one interpreter implementation against its independent program. */
#define G3_STORY_DATA_HEADER "g3_field3_data.h"
#define g3_story_init g3_aftermath_init
#define g3_story_tick g3_aftermath_tick
#define g3_story_valid g3_aftermath_valid
#define g3_story_asset g3_aftermath_asset
#define g3_story_identity g3_aftermath_identity
#define g3_story_asset_count g3_aftermath_asset_count
#define g3_story_blit g3_aftermath_blit
#define g3_story_save g3_aftermath_legacy_save
#define g3_story_load g3_aftermath_legacy_load
#include "g3_story.c"
