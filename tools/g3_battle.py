"""Read original battle/map records without assigning unverified combat rules.

Layout evidence: ALPHA.DLL 1002C948 (BTL), 100C3AB0 (OBT image),
1009D7E0 (CHR). Unknown words remain raw; terrain bytes are not walk costs.
"""
from dataclasses import dataclass
import struct
from g3_scenario import Reader, events
from g3_resources import xor_text


def parse_battle(data):
    r = Reader(data)
    header = struct.unpack("<11H", r.take(22))
    objects = [struct.unpack("<3H", r.take(6)) for _ in range(r.u16())]
    units = [struct.unpack("<4HB4H", r.take(17)) for _ in range(r.u16())]
    auxiliary_a = [struct.unpack("<HHBH", r.take(7)) for _ in range(r.u16())]
    auxiliary_b = [struct.unpack("<HHBHH", r.take(9)) for _ in range(r.u16())]
    program = events(r)
    if r.at != len(data):
        raise ValueError(f"Unexpected BTL trailer at {r.at:#x}")
    return dict(header=header, objects=objects, units=units,
                auxiliary_a=auxiliary_a, auxiliary_b=auxiliary_b, events=program)


@dataclass
class TileLayer:
    x: int
    y: int
    width: int
    height: int
    cells: bytes


@dataclass
class BattleMap:
    width: int
    height: int
    terrain: bytes
    attributes: bytes
    base: TileLayer
    objects: list
    tiles: bytes
    tile_attributes: bytes
    trailer: bytes

    @property
    def tile_count(self):
        return len(self.tiles) // 640


def _dimensions(width, height):
    if not 0 < width <= 4096 or not 0 < height <= 4096 or width*height > 4_000_000:
        raise ValueError("Invalid OBT dimensions")


def parse_map(data):
    r = Reader(data)
    blocks, = struct.unpack("<I", r.take(4))
    # Other block variants need their own verified loader path.
    if blocks != 1:
        raise ValueError(f"Unsupported OBT block count: {blocks}")
    width, height = struct.unpack("<HH", r.take(4))
    _dimensions(width, height)
    terrain = r.take(width*height)
    attributes = r.take(width*height)
    columns, rows, x, y = struct.unpack("<HHhh", r.take(8))
    _dimensions(columns, rows)
    base = TileLayer(x, y, columns, rows, r.take(columns*rows*4))
    count, = struct.unpack("<I", r.take(4))
    if count > 4096:
        raise ValueError("Excessive OBT objects")
    objects = []
    for _ in range(count):
        x, y, columns, rows = struct.unpack("<HHHH", r.take(8))
        _dimensions(columns, rows)
        objects.append(TileLayer(x, y, columns, rows, r.take(columns*rows*4)))
    count, = struct.unpack("<I", r.take(4))
    if not 0 < count <= 65536:
        raise ValueError("Invalid OBT tile count")
    tiles = r.take(count*640)
    tile_attributes = r.take(count)
    # The first map has a 64-byte tail. Preserve, don't silently discard it.
    trailer = r.take(len(data)-r.at)
    if len(trailer) != 64:
        raise ValueError("Unsupported OBT trailer size")
    for layer in [base, *objects]:
        for _, index in struct.iter_unpack("<HH", layer.cells):
            if index >= count:
                raise ValueError("OBT tile reference out of range")
    return BattleMap(width, height, terrain, attributes, base, objects,
                     tiles, tile_attributes, trailer)


def map_layer_rgb(map_data, layer=None):
    """Reconstruct one layer's 40x8 RGB555 tiles; object compositing is separate."""
    import numpy as np
    from PIL import Image
    if layer is None:
        layer = map_data.base
    values = np.frombuffer(map_data.tiles, dtype="<u2")
    rgb = np.stack(((values >> 10) & 31, (values >> 5) & 31, values & 31), axis=1)
    rgb = (rgb.astype(np.uint16)*255//31).astype(np.uint8)
    tiles = rgb.reshape(map_data.tile_count, 8, 40, 3)
    indices = np.frombuffer(layer.cells, dtype="<u2").reshape(layer.height, layer.width, 2)[:, :, 1]
    pixels = tiles[indices].transpose(0, 2, 1, 3, 4).reshape(layer.height*8, layer.width*40, 3)
    return Image.fromarray(pixels)


def parse_character(data):
    if len(data) != 175:
        raise ValueError("Unsupported CHR record size")
    return dict(name=xor_text(data[:24]), secondary_name=xor_text(data[24:48]),
                label=xor_text(data[48:58]), model=struct.unpack_from("<H", data, 58)[0],
                value_58=struct.unpack_from("<I", data, 77)[0],
                hp_base=struct.unpack_from("<I", data, 81)[0],
                stats_raw=list(data[85:95]), uninterpreted_prefix=data[60:77].hex(),
                uninterpreted_suffix=data[95:].hex())
