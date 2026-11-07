#!/usr/bin/env python3
"""Read ECMA-335 assembly dependencies without executing code or using NuGet.

Supports standard CLI metadata tables (0..44), PE32/PE32+, and #~ / #-.
This is a dependency inventory, not a verifier of runtime/API compatibility.
"""
import argparse
import hashlib
import json
from pathlib import Path
import struct


# ECMA-335 II.22 / II.24.2.6. Integers denote simple table indices.
SCHEMAS = [
    'u2 str guid guid guid', 'ResolutionScope str str',
    'u4 str str TypeDefOrRef 4 6', '4', 'u2 str blob', '6',
    'u4 u2 u2 str blob 8', '8', 'u2 u2 str', '2 TypeDefOrRef',
    'MemberRefParent str blob', 'u2 HasConstant blob',
    'HasCustomAttribute CustomAttributeType blob', 'HasFieldMarshal blob',
    'u2 HasDeclSecurity blob', 'u2 u4 2', 'u4 4', 'blob', '2 20',
    '20', 'u2 str TypeDefOrRef', '2 23', '23', 'u2 str blob',
    'u2 6 HasSemantics', '2 MethodDefOrRef MethodDefOrRef', 'str', 'blob',
    'u2 MemberForwarded str 26', 'u4 4', 'u4 u4', 'u4',
    'u4 u2 u2 u2 u2 u4 blob str str', 'u4', 'u4 u4 u4',
    'u2 u2 u2 u2 u4 blob str str blob', 'u4 35', 'u4 u4 u4 35',
    'u4 str blob', 'u4 u4 str str Implementation', 'u4 u4 str Implementation',
    '2 2', 'u2 u2 TypeOrMethodDef str', 'MethodDefOrRef blob', '42 TypeDefOrRef',
]
CODED = {
    'TypeDefOrRef': (2, [2, 1, 27]),
    'HasConstant': (2, [4, 8, 23]),
    'HasCustomAttribute': (5, [6, 4, 1, 2, 8, 9, 10, 0, 14, 23, 20, 17,
                              26, 27, 32, 35, 38, 39, 40, 42, 44, 43]),
    'HasFieldMarshal': (1, [4, 8]),
    'HasDeclSecurity': (2, [2, 6, 32]),
    'MemberRefParent': (3, [2, 1, 26, 6, 27]),
    'HasSemantics': (1, [20, 23]),
    'MethodDefOrRef': (1, [6, 10]),
    'MemberForwarded': (1, [4, 6]),
    'Implementation': (2, [38, 35, 39]),
    'CustomAttributeType': (3, [None, None, 6, 10, None]),
    'ResolutionScope': (2, [0, 26, 35, 1]),
    'TypeOrMethodDef': (1, [2, 6]),
}


class Metadata:
    def __init__(self, path):
        self.path = Path(path)
        self.data = self.path.read_bytes()
        if self.data[:2] != b'MZ':
            raise ValueError('Not a PE image')
        pe = self.uint(0x3c, 4)
        if self.data[pe:pe + 4] != b'PE\0\0':
            raise ValueError('Invalid PE header')
        self.machine = self.uint(pe + 4, 2)
        optional = pe + 24
        magic = self.uint(optional, 2)
        if magic not in (0x10b, 0x20b):
            raise ValueError('Unsupported optional header')
        directories = optional + (96 if magic == 0x10b else 112)
        self.sections = []
        section_start = optional + self.uint(pe + 20, 2)
        for i in range(self.uint(pe + 6, 2)):
            start = section_start + i * 40
            virtual_size, virtual, raw_size, raw = struct.unpack_from('<IIII', self.data, start + 8)
            self.sections.append((virtual, virtual_size, raw, raw_size))
        cli = self.rva(self.uint(directories + 14 * 8, 4))
        self.flags = self.uint(cli + 16, 4)
        self.entry_token = self.uint(cli + 20, 4)
        root = self.rva(self.uint(cli + 8, 4))
        if self.data[root:root + 4] != b'BSJB':
            raise ValueError('Invalid CLI metadata root')
        n = self.uint(root + 12, 4)
        self.runtime_version = self.data[root + 16:root + 16 + n].rstrip(b'\0').decode()
        offset = root + 16 + ((n + 3) & ~3)
        streams = self.uint(offset + 2, 2)
        offset += 4
        self.streams = {}
        for _ in range(streams):
            relative, size = struct.unpack_from('<II', self.data, offset)
            end = self.data.index(0, offset + 8)
            name = self.data[offset + 8:end].decode()
            self.streams[name] = self.data[root + relative:root + relative + size]
            offset = (end + 4) & ~3
        self.strings = self.streams['#Strings']
        self.blobs = self.streams.get('#Blob', b'\0')
        self.tables = self.streams.get('#~', self.streams.get('#-'))
        if self.tables is None:
            raise ValueError('No metadata tables')
        self.heap_flags = self.tables[6]
        valid = struct.unpack_from('<Q', self.tables, 8)[0]
        self.counts = [0] * 64
        offset = 24
        for table in range(64):
            if valid & (1 << table):
                self.counts[table] = struct.unpack_from('<I', self.tables, offset)[0]
                offset += 4
        self.offsets = {}
        for table, count in enumerate(self.counts):
            if count:
                if table >= len(SCHEMAS):
                    raise ValueError(f'Unsupported metadata table {table}')
                self.offsets[table] = offset
                offset += count * sum(self.width(x) for x in SCHEMAS[table].split())
        if offset > len(self.tables):
            raise ValueError('Metadata tables outside stream bounds')

    def uint(self, offset, size):
        if offset < 0 or offset + size > len(self.data):
            raise ValueError('Read outside image bounds')
        return int.from_bytes(self.data[offset:offset + size], 'little')

    def rva(self, address):
        if not address:
            raise ValueError('Missing RVA')
        for virtual, virtual_size, raw, raw_size in self.sections:
            if virtual <= address < virtual + max(virtual_size, raw_size):
                delta = address - virtual
                if delta >= raw_size:
                    raise ValueError('RVA has no file backing')
                return raw + delta
        raise ValueError(f'Unmapped RVA: {address:#x}')

    def width(self, kind):
        if kind in ('u2', 'u4'):
            return int(kind[1])
        if kind in ('str', 'guid', 'blob'):
            return 4 if self.heap_flags & {'str': 1, 'guid': 2, 'blob': 4}[kind] else 2
        if kind in CODED:
            bits, tables = CODED[kind]
            return 4 if max((self.counts[t] for t in tables if t is not None), default=0) >= 1 << (16 - bits) else 2
        return 4 if self.counts[int(kind)] >= 65536 else 2

    def rows(self, table):
        offset = self.offsets.get(table, 0)
        for _ in range(self.counts[table]):
            row = []
            for kind in SCHEMAS[table].split():
                size = self.width(kind)
                row.append(int.from_bytes(self.tables[offset:offset + size], 'little'))
                offset += size
            yield row

    def string(self, index):
        return self.strings[index:self.strings.index(0, index)].decode('utf-8')

    @staticmethod
    def compressed(data, offset):
        first = data[offset]
        if first < 0x80:
            return first, offset + 1
        if first < 0xc0:
            return ((first & 0x3f) << 8) | data[offset + 1], offset + 2
        if first < 0xe0:
            return ((first & 0x1f) << 24) | int.from_bytes(data[offset + 1:offset + 4], 'big'), offset + 4
        raise ValueError('Invalid compressed integer')

    def blob(self, index):
        if not index:
            return b''
        size, start = self.compressed(self.blobs, index)
        return self.blobs[start:start + size]

    def report(self):
        assemblies = list(self.rows(32))
        own = assemblies[0] if assemblies else None
        refs = []
        for row in self.rows(35):
            refs.append({'name': self.string(row[6]), 'version': '.'.join(map(str, row[:4])),
                         'public_key_or_token': self.blob(row[5]).hex()})
        type_refs = list(self.rows(1))
        member_refs = list(self.rows(10))
        # Attribute MemberRefs also appear here. TypeSpec parents are left as
        # tokens; resolving generic signatures is outside this inventory's scope.
        external_members = []
        for parent, name, signature in member_refs:
            if parent & 7 != 1:
                continue
            scope, type_name, name_space = type_refs[(parent >> 3) - 1]
            assembly = None
            while scope & 3 == 3:
                scope = type_refs[(scope >> 2) - 1][0]
            if scope & 3 == 2:
                assembly = refs[(scope >> 2) - 1]['name']
            external_members.append({'assembly': assembly,
                                     'type': self.string(name_space) + '.' + self.string(type_name),
                                     'member': self.string(name),
                                     'signature_blob': self.blob(signature).hex()})
        frameworks = []
        for _, ctor, value in self.rows(12):
            if ctor & 7 != 3:
                continue
            member = member_refs[(ctor >> 3) - 1]
            parent = member[0]
            if parent & 7 != 1:
                continue
            t = type_refs[(parent >> 3) - 1]
            if self.string(t[1]) == 'TargetFrameworkAttribute' and self.string(t[2]) == 'System.Runtime.Versioning':
                data = self.blob(value)
                if data[:2] == b'\1\0' and data[2] != 0xff:
                    size, start = self.compressed(data, 2)
                    frameworks.append(data[start:start + size].decode())
        modules = list(self.rows(26))
        methods = list(self.rows(6))
        fields = list(self.rows(4))
        imports = []
        for flags, forwarded, name, scope in self.rows(28):
            target = methods if forwarded & 1 else fields
            row = target[(forwarded >> 1) - 1]
            imports.append({'library': self.string(modules[scope - 1][0]),
                            'entry_point': self.string(name),
                            'member': self.string(row[3] if forwarded & 1 else row[1]),
                            'flags': flags})
        return {'file': self.path.name, 'sha256': hashlib.sha256(self.data).hexdigest(),
                'assembly': self.string(own[7]) if own else None,
                'version': '.'.join(map(str, own[1:5])) if own else None,
                'target_framework': frameworks, 'metadata_version': self.runtime_version,
                'machine': hex(self.machine), 'cli_flags': hex(self.flags),
                'il_only': bool(self.flags & 1), 'requires_32bit': bool(self.flags & 2),
                'entry_point_token': hex(self.entry_token),
                'assembly_references': refs, 'native_imports': imports,
                'external_members': external_members,
                'type_definitions': self.counts[2], 'method_definitions': self.counts[6]}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('assemblies', nargs='+', type=Path)
    parser.add_argument('--output', type=Path)
    args = parser.parse_args()
    reports = [Metadata(p).report() for p in args.assemblies]
    result = json.dumps(reports, indent=2, ensure_ascii=False) + '\n'
    if args.output:
        args.output.parent.mkdir(parents=True, exist_ok=True)
        args.output.write_text(result)
    else:
        print(result, end='')


if __name__ == '__main__':
    main()
