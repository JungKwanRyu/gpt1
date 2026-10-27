"""Prepare the verified Shivan opening field for the native scenario runtime."""
import argparse
import io
import json
from pathlib import Path
import re
import struct
import zlib
from PIL import Image, ImageDraw, ImageFont
from g3_image import GameImage
from g3_resources import parse_index, parse_strings, read_resource
from g3_scenario import parse_field, parse_chapter, parse_episodes
from g3_obs import decode_obs
from prepare_title import HEADER, RECORD

ROOT=Path(__file__).resolve().parent.parent


def dialogue(raw):
    match=re.match(r"/m0\[(.*?)\]/m1",raw)
    name=match.group(1) if match else ""
    body=raw[match.end():] if match else raw
    body=body.replace("/n","\n")
    body=re.sub(r"/(?:d|t|c)\d+","",body)
    if re.search(r"/[a-zA-Z]",body):
        raise ValueError(f"Unimplemented text control: {body}")
    return name,body.lstrip("\n")


def main():
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image",type=Path)
    parser.add_argument("--font",type=Path,default=Path("C:/Windows/Fonts/malgun.ttf"))
    args=parser.parse_args()
    body_font=ImageFont.truetype(str(args.font),17)
    name_font=ImageFont.truetype(str(args.font),17)
    small_font=ImageFont.truetype(str(args.font),11)
    output=ROOT/"dist/switch/g3nx"
    output.mkdir(parents=True,exist_ok=True)
    images=[]
    def asset(name,image):
        index=len(images)
        images.append((name,image))
        return index

    with GameImage(args.image) as image:
        raw_field=image.read("G3P1/Flds/0001.FLD")
        raw_text=image.read("G3P1/Flds/0001.FLG")
        program=parse_field(raw_field)
        texts=parse_strings(raw_text)
        identity=zlib.crc32(raw_field+raw_text+b"g3nx-story-0.3.0-layout1")
        asset(f"id{identity:08x}",Image.new("RGBA",(1,1)))
        audit={"episodes":parse_episodes(image.read("G3P1/Eps/EPISODE.EPS")),
               "chapters":{},"fields":{},"scope":"Only original field 1 is executable in this build"}
        for number in (0,20,37):
            audit["chapters"][number]=parse_chapter(image.read(f"G3P1/Chps/{number:04d}.CPR"))
        for number in (1,83,68):
            audit["fields"][number]=parse_field(image.read(f"G3P1/Flds/{number:04d}.FLD"))
        (ROOT/"reverse/scenario-analysis.json").write_text(json.dumps(audit,indent=2,ensure_ascii=False),encoding="utf-8")
        bgr_idx="G3P1/BGRs/BGRs.idx"
        obs_idx="G3P1/Obss/Obss.idx"
        bgr={r.name:r for r in parse_index(image.read(bgr_idx))}
        obs={r.name:r for r in parse_index(image.read(obs_idx))}
        cache={}
        backgrounds={}
        for number in (137,138):
            blob=read_resource(image,bgr_idx,bgr[f"{number:04d}.bgr"],cache)
            with Image.open(io.BytesIO(blob)) as original:
                backgrounds[number]=asset(f"bg{number:04d}",original.convert("RGBA").crop((0,0,640,480)))
        portraits={}
        for number in sorted({a[1] for event in program["events"] for a in event["actions"] if a[0] in (200,201,202)}):
            groups=decode_obs(read_resource(image,obs_idx,obs[f"{number:04d}.obs"],cache))
            canvas=Image.new("RGBA",(640,480))
            for sprite in groups[0]:
                canvas.alpha_composite(Image.frombytes("RGBA",(sprite.width,sprite.height),sprite.rgba()),(320+sprite.x,240+sprite.y))
            portraits[number]=asset(f"pt{number:04d}",canvas)
        pages=[]
        for i,raw in enumerate(texts):
            name,body=dialogue(raw)
            draw=ImageDraw.Draw(Image.new("RGBA",(1,1)))
            lines=[]
            for paragraph in body.split("\n"):
                line=""
                for char in paragraph:
                    if draw.textlength(line+char,font=body_font)>570:
                        lines.append(line)
                        line=char
                    else:
                        line+=char
                lines.append(line)
            first=len(images)
            for page_at in range(0,len(lines),4):
                panel=Image.new("RGBA",(640,480))
                draw=ImageDraw.Draw(panel,"RGBA")
                draw.rounded_rectangle((12,330,628,461),radius=5,fill=(21,20,55,225),outline=(194,196,221,255),width=1)
                draw.text((26,339),name,font=name_font,fill=(75,250,194,255))
                for row,line in enumerate(lines[page_at:page_at+4]):
                    draw.text((26,364+row*22),line,font=body_font,fill=(246,244,236,255))
                draw.rectangle((0,462,639,479),fill=(5,5,12,245))
                draw.text((140,465),"A 다음    X 저장    B 에피소드 선택    (음성·음악 미지원)",font=small_font,fill=(244,231,195,255))
                asset(f"d{i:03d}_{page_at//4}",panel)
            pages.append((first,len(images)-first))

    for label,message in (("boundary","이 장면의 대화가 끝났습니다.\n다음 필드의 캐릭터 이동·전투 실행은 아직 복원 중입니다.\nB로 에피소드 선택 화면으로 돌아갈 수 있습니다."),
                          ("unsupported","아직 지원하지 않는 이벤트 또는 자산에 도달했습니다.\n원본 명령을 건너뛰지 않고 진행을 멈췄습니다.\nB로 에피소드 선택 화면으로 돌아갈 수 있습니다.")):
        panel=Image.new("RGBA",(640,480),(0,0,0,155))
        draw=ImageDraw.Draw(panel,"RGBA")
        draw.rounded_rectangle((30,156,610,319),radius=6,fill=(22,18,33,255),outline=(212,184,112,255),width=2)
        for row,line in enumerate(message.split("\n")):
            draw.text((48,180+row*34),line,font=body_font,fill=(242,232,211,255))
        asset(label,panel)
    path=output/"g3nx.story"
    tmp=path.with_suffix(".partial")
    with tmp.open("wb") as stream:
        stream.write(HEADER.pack(b"G3NXRAW1",len(images),0))
        offset=HEADER.size+RECORD.size*len(images)
        for name,picture in images:
            size=picture.width*picture.height*4
            stream.write(RECORD.pack(name.encode(),picture.width,picture.height,offset,size))
            offset+=size
        for _,picture in images:
            stream.write(picture.tobytes())
    tmp.replace(path)
    code=["/* Generated from original field 1. Regenerate with tools/prepare_story.py. */",
          "#ifndef G3_STORY_DATA_H","#define G3_STORY_DATA_H",
          f"#define G3_STORY_ID {identity}u",f"#define G3_STORY_ASSETS {len(images)}u",
          f"#define G3_STORY_BOUNDARY_ASSET {len(images)-2}",f"#define G3_STORY_ERROR_ASSET {len(images)-1}"]
    for i,event in enumerate(program["events"]):
        for label in ("conditions","actions"):
            values=event[label]
            code.append(f"static const G3Command event_{i}_{label}[] = {{")
            code.extend("    {{"+",".join(str(x) for x in command)+"}}," for command in values)
            if not values: code.append("    {{0}},")
            code.append("};")
    code.append("static const G3Event g3_story_events[] = {")
    for i,event in enumerate(program["events"]):
        code.append(f"    {{{event['kind']}, {len(event['conditions'])}, {len(event['actions'])}, event_{i}_conditions, event_{i}_actions}},")
    code.append("};")
    code.append(f"#define G3_STORY_EVENTS {len(program['events'])}u")
    for name,items in (("backgrounds",backgrounds.items()),("portraits",portraits.items()),("texts",enumerate(pages))):
        ctype="G3TextPages" if name=="texts" else "G3ResourceMap"
        code.append(f"static const {ctype} g3_story_{name}[] = {{")
        for key,value in items:
            code.append("    {"+(f"{value[0]}, {value[1]}" if name=="texts" else f"{key}, {value}")+"},")
        code.append("};")
    code.append("#endif")
    (ROOT/"homebrew/include/g3_story_data.h").write_text("\n".join(code)+"\n",encoding="ascii")
    manifest={"id":identity,"field":1,"assets":len(images),"texts":len(texts),"pages":pages,
              "backgrounds":backgrounds,"portraits":portraits,"events":program["events"],
              "unsupported_next_field":2,"scope":"Original Shivan opening; other episodes and gameplay not yet executable"}
    (output/"g3nx.story.json").write_text(json.dumps(manifest,indent=2),encoding="utf-8")
    print(f"Prepared field 1: {len(images)} assets, {len(texts)} text entries, {path.stat().st_size:,} bytes")


if __name__=="__main__":
    main()
