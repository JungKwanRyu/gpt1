# Run with GNU make in a devkitPro shell. All paths are relative to this folder.
ifeq ($(strip $(DEVKITPRO)),)
$(error DEVKITPRO is not set; install switch-dev and use the devkitPro shell)
endif
DEVKITA64 ?= $(DEVKITPRO)/devkitA64
LIBNX := $(DEVKITPRO)/libnx
CC := $(DEVKITA64)/bin/aarch64-none-elf-gcc
TOOLS := $(DEVKITPRO)/tools/bin
TARGET := g3nx
APP_TITLE := G3NX - The War of Genesis III
ARCH := -march=armv8-a+crc+crypto -mtune=cortex-a57 -mtp=soft -fPIE
CFLAGS := $(ARCH) -std=gnu11 -O2 -g -Wall -Wextra -Werror -ffunction-sections -fdata-sections -D__SWITCH__ -Iinclude -I$(LIBNX)/include
LDFLAGS := $(ARCH) -specs=$(LIBNX)/switch.specs -Wl,--gc-sections,-Map,build/g3nx.map -L$(LIBNX)/lib
OBJECTS := build/main.o build/g3_formats.o build/g3_menu.o build/g3_story.o build/g3_field.o build/g3_field_render.o build/g3_progress.o build/g3_audio.o build/g3_map.o build/g3_navigation.o build/g3_combat.o build/g3_combat_math.o build/g3_battle_events.o build/g3_aftermath.o build/g3_session.o

.PHONY: all
all: $(TARGET).nro

build:
	mkdir -p build

build/%.o: source/%.c include/g3_formats.h include/g3_menu.h include/g3_story.h include/g3_story_data.h include/g3_field2_data.h include/g3_battle6_data.h include/g3_field3_data.h include/g3_battle6_program.h | build
	$(CC) $(CFLAGS) -MMD -MP -c $< -o $@

$(TARGET).elf: $(OBJECTS)
	$(CC) $(OBJECTS) $(LDFLAGS) -lnx -o $@

$(TARGET).nacp: Makefile
	$(TOOLS)/nacptool --create "$(APP_TITLE)" "G3NX" "0.7.0" $@

$(TARGET).nro: $(TARGET).elf $(TARGET).nacp
	$(TOOLS)/elf2nro $< $@ --nacp=$(TARGET).nacp

-include $(OBJECTS:.o=.d)
