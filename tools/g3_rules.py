"""Measured CHR/DAT layouts and initial battle stats (ALPHA 1009D7E0..1009F200).

These are initial values without battle statuses, morale or mounted-unit links.
Ability inheritance from equipment is not used for the basic stat IDs 1..9.
The ten-byte CHR stats end with action period, then RANGE, in that order.
"""
import struct
from g3_battle import parse_character
from g3_resources import xor_text


def table(data, size):
    if len(data) < 4:
        raise ValueError("Truncated DAT table")
    count, = struct.unpack_from("<H", data)
    if len(data) != 2 + count*size + 2:
        raise ValueError("Unsupported DAT record count/size")
    return [data[2+i*size:2+(i+1)*size] for i in range(count)]


class Rules:
    def __init__(self, image):
        self.items = [None] + table(image.read("G3P1/Dats/Item.dat"), 204)
        self.jobs = [None] + table(image.read("G3P1/Dats/Job.dat"), 100)
        self.abilities = [None] + table(image.read("G3P1/Dats/Abi.dat"), 39)
        self.attacks = [None] + table(image.read("G3P1/Dats/Attack.dat"), 94)

    def initial(self, data):
        char = parse_character(data)
        job = data[65]
        if job != 94 and job >= len(self.jobs):
            raise ValueError("Unsupported job reference")
        equipment = struct.unpack_from("<5H", data, 113)
        if any(item >= len(self.items) for item in equipment):
            raise ValueError("Invalid item reference")
        ranks = {}
        for at in range(123, 173, 5):
            ability, rank = struct.unpack_from("<IB", data, at)
            if 0 < ability < 200 and rank:
                ranks[ability] = rank

        def rank(ability):
            personal = ranks.get(ability)
            if job == 0:
                return personal or 0
            if job == 94:
                return personal or 1
            if self.abilities[ability][35] in (2, 4):
                return 1
            row = self.jobs[job]
            for at in range(72, 100, 2):
                if row[at] == ability:
                    return min(personal, row[at+1]) if personal else 0
            return 0

        def effect(number):
            total = 0
            for item in equipment:
                if not item:
                    continue
                row = self.items[item]
                for at in (66, 70, 74):
                    key, value = struct.unpack_from("<Hh", row, at)
                    if key == number:
                        total += value
            return total

        raw = char["stats_raw"]
        char.update(job=job, kind=data[69], large=data[70], flying=data[71],
                    equipment=list(equipment), personal_abilities=ranks,
                    default_attack=struct.unpack_from("<H",data,67)[0],
                    passive_ranks={i:rank(i) for i in (76,77,80)},
                    hp_max=char["hp_base"]+100*rank(1),
                    strength=raw[0]+5*rank(2)+effect(201),
                    skill=raw[1]+5*rank(3), dexterity=raw[2]+5*rank(4)+effect(200),
                    intelligence=raw[3]+5*rank(5)+effect(203),
                    movement=raw[5]+5*rank(7)+effect(205),
                    armor=raw[6]+5*rank(8)+effect(202),
                    resistance=raw[7]+5*rank(9)+effect(204),
                    period=raw[8], range=raw[9]+5*rank(6)+effect(206),
                    element_scales=list(struct.unpack_from("<5H", data, 95)))
        return char

    def physical_attack(self, number):
        """First-battle type-1, constant-range, kind-0 subset; reject other paths.

        Disk 94-byte records -> runtime 108 bytes: 10023070..10023409.
        Range selection: 10038B20, 100390B0, 1008AFB0, 1008AD80.
        """
        if not 0 < number < len(self.attacks):
            raise ValueError("Invalid attack reference")
        r = self.attacks[number]
        if (r[30],r[31],r[61],r[45],r[46]) != (1,3,0,1,0):
            raise ValueError("Unsupported physical attack variant")
        if tuple(r[40:45]) != (0,1,1,0,0):
            raise ValueError("Unsupported target/height range flags")
        return dict(name=xor_text(r[:30]),id=number,kind=r[61],
                    range=struct.unpack_from("<i",r,32)[0],
                    min_range=struct.unpack_from("<i",r,36)[0],
                    height_cost=r[41],bonus=struct.unpack_from("<i",r,62)[0],
                    element=r[88],secondary=r[89],hits=r[90],
                    morale_cost=struct.unpack_from("<i",r,67)[0])

    def weapon(self, number):
        if not 0 < number < len(self.items):
            raise ValueError("Invalid weapon reference")
        row = self.items[number]
        return dict(name=xor_text(row[:30]), power=struct.unpack_from("<i", row, 78)[0],
                    defense=struct.unpack_from("<i", row, 82)[0],
                    accuracy=struct.unpack_from("<i", row, 90)[0],
                    critical=row[95], element=row[100], modifier=row[102], multiplier=row[103])
