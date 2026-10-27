"""Reproduce first-battle evidence and maps; does not simulate combat."""
import argparse
from collections import Counter
import hashlib
import json
from pathlib import Path
from g3_image import GameImage
from g3_battle import parse_battle, parse_map, parse_character, map_layer_rgb
from g3_scenario import parse_chapter, parse_field
from g3_resources import parse_index, read_resource
from g3_obs import decode_obs_actions


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image", type=Path)
    parser.add_argument("--output", type=Path, default=Path("reverse"))
    args = parser.parse_args()
    args.output.mkdir(parents=True, exist_ok=True)
    sources = {}
    with GameImage(args.image) as image:
        def read(path):
            raw = image.read(path)
            sources[path] = dict(bytes=len(raw), sha256=hashlib.sha256(raw).hexdigest())
            return raw

        chapter = parse_chapter(read("G3P1/Chps/0000.CPR"))
        calls = [dict(event=i, opcode=a[0], resource=a[1])
                 for i, event in enumerate(chapter["events"])
                 for a in event["actions"] if a[0] in (502, 503)]
        field = parse_field(read("G3P1/Flds/0002.FLD"))
        battle = parse_battle(read("G3P1/Btls/0006.BTL"))
        map_data = parse_map(read(f"G3P1/Obts/{battle['header'][0]:04d}.OBT"))
        map_layer_rgb(map_data).save(args.output/"battle6-base-map.png")
        characters = {number: parse_character(read(f"G3P1/Chars/{number:04d}.CHR"))
                      for number in sorted({unit[0] for unit in battle["units"]})}
        index_path = "G3P1/Obss/Obss.idx"
        records = {}
        for record in parse_index(read(index_path)):
            records.setdefault(record.name, record)
        cache, sprites = {}, {}
        for model in sorted({c["model"] for c in characters.values()}):
            record = records[f"{model:04d}.obs"]
            raw = read_resource(image, index_path, record, cache)
            groups, actions, trailer = decode_obs_actions(raw)
            sprites[model] = dict(archive=record.archive, start=record.start, end=record.end,
                                 sha256=hashlib.sha256(raw).hexdigest(),
                                 frames=[len(group) for group in groups], actions=actions,
                                 uninterpreted_trailer=trailer.hex())
        survey = dict(parsed=0, errors=[])
        for entry in image.entries:
            if not entry.path.upper().endswith(".BTL"):
                continue
            try:
                parse_battle(image.read(entry.path))
                survey["parsed"] += 1
            except ValueError as error:
                survey["errors"].append(dict(path=entry.path, error=str(error)))
    report = dict(scope="Static reconstruction only; no damage, initiative, movement or AI rules asserted",
                  input_policy="Loose scenario/CHR/OBT files; indexed OBS. Loader precedence remains unverified.",
                  sources=sources, chapter_calls=calls, field2=field, battle6=battle,
                  map79=dict(width=map_data.width, height=map_data.height,
                             bitmap=[map_data.base.width*40, map_data.base.height*8],
                             tile_origin=[map_data.base.x, map_data.base.y],
                             tiles=map_data.tile_count, objects=len(map_data.objects),
                             terrain_histogram=dict(Counter(map_data.terrain)),
                             attributes_histogram=dict(Counter(map_data.attributes)),
                             uninterpreted_trailer=map_data.trailer.hex()),
                  characters=characters, sprites=sprites, battle_survey=survey)
    (args.output/"battle-analysis.json").write_text(json.dumps(report, indent=2, ensure_ascii=False), encoding="utf-8")
    print(f"Battle 6: {len(battle['units'])} units; map {map_data.width}x{map_data.height}, "
          f"{map_data.tile_count} tiles; BTL survey {survey['parsed']} accepted, {len(survey['errors'])} rejected")


if __name__ == "__main__":
    main()
