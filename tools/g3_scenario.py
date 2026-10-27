"""Strict readers for original EPS, CPR and FLD scenario records."""
from dataclasses import dataclass
import struct
from g3_resources import xor_text


class Reader:
    def __init__(self, data):
        self.data, self.at = data, 0

    def take(self, size):
        if size<0 or self.at+size>len(self.data):
            raise ValueError(f"Scenario truncated at {self.at:#x}")
        result=self.data[self.at:self.at+size]
        self.at+=size
        return result

    def u16(self):
        return struct.unpack("<H",self.take(2))[0]

    def commands(self, count):
        return [struct.unpack("<7H",self.take(14)) for _ in range(count)]


def events(reader):
    result=[]
    for _ in range(reader.u16()):
        kind=reader.u16()
        conditions=reader.commands(reader.u16())
        actions=reader.commands(reader.u16())
        result.append({"kind":kind,"conditions":conditions,"actions":actions})
    return result


def parse_field(data):
    r=Reader(data)
    header=struct.unpack("<6H",r.take(12))
    actors=[struct.unpack("<6H",r.take(12)) for _ in range(header[5])]
    result={"header":header,"actors":actors,"events":events(r)}
    if r.at!=len(data):
        raise ValueError("Unexpected FLD trailer")
    return result


def parse_chapter(data):
    r=Reader(data)
    header=struct.unpack("<12H",r.take(24))
    places=[struct.unpack("<14H",r.take(28)) for _ in range(r.u16())]
    route_ids=[r.u16() for _ in range(r.u16())]
    routes=[]
    for route in route_ids:
        routes.append({"id":route,"points":[struct.unpack("<HH",r.take(4)) for _ in range(r.u16())]})
    parties=[struct.unpack("<6H",r.take(12)) for _ in range(r.u16())]
    result={"header":header,"places":places,"routes":routes,"parties":parties,"events":events(r)}
    if r.at!=len(data):
        raise ValueError("Unexpected CPR trailer")
    return result


def parse_episodes(data):
    r=Reader(data)
    groups=[]
    for _ in range(3):
        group=[]
        for _ in range(r.u16()):
            raw=r.take(34)
            group.append({"date":struct.unpack_from("<HH",raw),"name":xor_text(raw[4:24]),
                          "words":struct.unpack_from("<5H",raw,24)})
        groups.append(group)
    trailer=r.take(len(data)-r.at)
    if len(trailer)!=2:
        raise ValueError("Unexpected EPS trailer size")
    return {"groups":groups,"uninterpreted_trailer":trailer.hex()}
