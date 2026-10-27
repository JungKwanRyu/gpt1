"""Decode OBS palettes and span streams, reconstructed from alpha.dll's loader."""
from dataclasses import dataclass
import struct


@dataclass
class Sprite:
    width: int
    height: int
    x: int
    y: int
    indices: bytes
    palette: bytes
    transparent: int

    def rgba(self):
        colors = [self.palette[i*3:i*3+3]+bytes([0 if i==self.transparent else 255]) for i in range(256)]
        return b"".join(colors[index] for index in self.indices)


def decode_spans(payload, width, height, transparent):
    if not 0 < width <= 640 or not 0 < height <= 4096 or len(payload)<2:
        raise ValueError("Invalid OBS dimensions or span header")
    count, = struct.unpack_from("<H",payload)
    output = bytearray([transparent])*(width*height)
    at, cursor = 2, 0
    for _ in range(count):
        if at+4>len(payload):
            raise ValueError("Truncated OBS span")
        skip, quads, tail = struct.unpack_from("<HBB",payload,at)
        at += 4
        cursor += skip
        size = quads*4+tail
        # Encoded skip distances use a 640-pixel source stride. The original
        # 0x100A6BF0 routine adjusts these distances to the tight frame width.
        x, y = cursor % 640, cursor // 640
        if x+size>width or y>=height or at+size>len(payload):
            raise ValueError("OBS span exceeds frame")
        destination = y*width+x
        output[destination:destination+size] = payload[at:at+size]
        cursor += size
        at += size
    if at != len(payload):
        raise ValueError("Unexpected trailing OBS span bytes")
    return bytes(output)


def _decode_images(data):
    if len(data)<4:
        raise ValueError("Truncated OBS header")
    count, = struct.unpack_from("<I",data)
    if count>1024:
        raise ValueError("Excessive OBS groups")
    at = 4
    groups = []
    for _ in range(count):
        if at+128>len(data):
            raise ValueError("Truncated OBS group")
        transparent = data[at+17]
        frames, = struct.unpack_from("<H",data,at+66)
        at += 128
        sprites = []
        for _ in range(frames):
            if at+12>len(data):
                raise ValueError("Truncated OBS frame")
            length, width, height, x, y = struct.unpack_from("<IHHhh",data,at)
            at += 12
            if at+length>len(data):
                raise ValueError("Truncated OBS payload")
            pixels = decode_spans(data[at:at+length],width,height,transparent)
            sprites.append(Sprite(width,height,x,y,pixels,b"",transparent))
            at += length
        if at+768>len(data):
            raise ValueError("Truncated OBS palette")
        palette = data[at:at+768]
        at += 768
        for sprite in sprites:
            sprite.palette = palette
        groups.append(sprites)
    return groups, at


def decode_obs(data):
    # Existing portrait consumers only need the image groups.
    return _decode_images(data)[0]


def decode_obs_actions(data):
    """Read timed action records (100A71D4); preserve unknown command words.

    Record word 0 is start time, word 1 duration; opcode 0 draws the group/frame
    in words 3/4. Other opcodes must be interpreted by the runtime separately.
    """
    groups, at = _decode_images(data)
    if at+4 > len(data):
        raise ValueError("Missing OBS action table")
    count, = struct.unpack_from("<I", data, at)
    at += 4
    if count > 4096:
        raise ValueError("Excessive OBS actions")
    actions = []
    for _ in range(count):
        if at+8 > len(data):
            raise ValueError("Truncated OBS action header")
        duration, commands = struct.unpack_from("<II", data, at)
        at += 8
        if commands > (len(data)-at)//18:
            raise ValueError("Truncated OBS action records")
        records = list(struct.iter_unpack("<9H", data[at:at+commands*18]))
        at += commands*18
        for record in records:
            if record[2] == 0 and (record[3] >= len(groups) or record[4] >= len(groups[record[3]])):
                raise ValueError("OBS action frame out of bounds")
        actions.append(dict(duration=duration, records=records))
    return groups, actions, data[at:]
