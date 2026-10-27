"""Compose the original title assets into controller-selectable frontend scenes."""
import argparse
import hashlib
import io
import json
from pathlib import Path
import struct
from PIL import Image, ImageDraw, ImageFont
from g3_image import GameImage
from g3_resources import parse_index, parse_strings, read_resource
from g3_obs import decode_obs

ROOT = Path(__file__).resolve().parent.parent
HEADER = struct.Struct("<8sII")
RECORD = struct.Struct("<16sIIII")


def wrapped(draw, text, x, y, font, width, color, line_height=22):
    line = ""
    for char in text:
        if char=="\n" or draw.textlength(line+char,font=font)>width:
            draw.text((x,y),line,font=font,fill=color)
            y += line_height
            line = "" if char=="\n" else char
        else:
            line += char
    draw.text((x,y),line,font=font,fill=color)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image",type=Path)
    parser.add_argument("--font",type=Path,default=Path("C:/Windows/Fonts/malgun.ttf"))
    parser.add_argument("--output",type=Path,default=ROOT/"dist/switch/g3nx/g3nx.title")
    args = parser.parse_args()
    if not args.font.is_file():
        raise FileNotFoundError("Use --font to specify a Korean font for frontend explanations")
    small = ImageFont.truetype(str(args.font),11)
    body = ImageFont.truetype(str(args.font),14)
    heading = ImageFont.truetype(str(args.font),20)
    scenes = []
    sources = {}
    with GameImage(args.image) as image:
        bgr_idx = "G3P1/BGRs/BGRs.idx"
        obs_idx = "G3P1/Obss/Obss.idx"
        bgr = {r.name:r for r in parse_index(image.read(bgr_idx))}
        obs = {r.name:r for r in parse_index(image.read(obs_idx))}
        cache = {}

        def resource(index, rec):
            blob = read_resource(image,index,rec,cache)
            sources[rec.name] = {"archive":rec.archive,"start":rec.start,"end":rec.end,
                                 "sha256":hashlib.sha256(blob).hexdigest()}
            return blob

        def background(name):
            with Image.open(io.BytesIO(resource(bgr_idx,bgr[name]))) as original:
                if original.size != (640,480):
                    raise ValueError("Unexpected title background size")
                return original.convert("RGBA")

        base = background("0015.bgr")
        episode_base = background("0096.bgr")
        logos = decode_obs(resource(obs_idx,obs["0360.obs"]))[0]
        menus = decode_obs(resource(obs_idx,obs["0751.obs"]))[0]
        descriptions = parse_strings(image.read("G3P1/Eps/EPISODE.ELG"))

        def sprite(canvas, source, anchor, opacity=255):
            picture = Image.frombytes("RGBA",(source.width,source.height),source.rgba())
            if opacity != 255:
                picture.putalpha(picture.getchannel("A").point(lambda v:v*opacity//255))
            canvas.alpha_composite(picture,(anchor[0]+source.x,anchor[1]+source.y))

        for selected in range(4):
            page = base.copy()
            sprite(page,logos[0],(320,244))
            sprite(page,logos[1],(320,244))
            for i in range(4):
                sprite(page,menus[i],(320,240),255 if i==selected else 115)
            sprite(page,menus[4],(320,240))
            draw = ImageDraw.Draw(page)
            draw.text((8,7),"G3NX 0.4.0",font=small,fill=(210,197,174,255))
            draw.text((467,7),"SCENARIO PORT",font=small,fill=(210,197,174,255))
            draw.text((212,445),"↑↓ 선택    A 확인    + 종료",font=small,fill=(207,196,177,255))
            scenes.append((f"title{selected}",page))

        episode_names = ["Shivan Scimitar","Crimson Crusade","Apocalypse"]
        chapter_names = ["카디스 요새","반기","인페르노의 음모"]
        for selected in range(3):
            page = episode_base.copy()
            draw = ImageDraw.Draw(page,"RGBA")
            for i in range(3):
                x=33+i*195
                draw.rounded_rectangle((x,143,x+183,385),radius=6,
                    fill=(15,9,8,210 if i==selected else 170),
                    outline=(220,185,105,255) if i==selected else (89,65,47,255),width=2)
                draw.text((x+14,161),chapter_names[i],font=body,
                          fill=(255,224,161,255) if i==selected else (155,144,130,255))
                wrapped(draw,descriptions[(1,2,0)[i]],x+14,204,body,156,
                        (230,220,200,255) if i==selected else (140,132,120,255))
            draw.text((166,416),"←→ 에피소드 선택    A 확인    B 타이틀",font=body,fill=(235,218,181,255))
            draw.text((140,449),"시반슈미터: 첫 대화·이동 장면 지원 / 나머지 에피소드: 복원 중",font=small,fill=(198,184,166,255))
            scenes.append((f"episode{selected}",page))

        notices = [
            (episode_names[i],"이 에피소드의 이동·이벤트 실행은 아직 복원 중입니다.\n\n시반슈미터의 첫 대화 장면부터 지원합니다.\n전투와 전체 스토리 진행은 아직 구현되지 않았습니다.",scenes[4+i][1])
            for i in range(3)]
        notices += [("Load Game","읽을 수 있는 G3NX 진행 저장이 없습니다.\n첫 대화 장면에서 X로 저장할 수 있습니다.\n기존 PC판의 세이브 파일은 아직 지원하지 않습니다.",scenes[1][1]),
                    ("Tutorial","튜토리얼의 전투 및 이벤트 실행은 아직 구현 중입니다.",scenes[2][1])]
        for i,(title,message,under) in enumerate(notices):
            page=under.copy()
            page.alpha_composite(Image.new("RGBA",page.size,(0,0,0,165)))
            draw=ImageDraw.Draw(page,"RGBA")
            draw.rounded_rectangle((80,151,560,346),radius=8,fill=(23,17,15,255),outline=(179,143,83,255),width=2)
            draw.text((105,172),title,font=heading,fill=(242,216,169,255))
            wrapped(draw,message,105,216,body,430,(226,218,204,255),21)
            draw.text((245,315),"A / B 돌아가기",font=body,fill=(225,195,135,255))
            scenes.append((f"notice{i}",page))

    args.output.parent.mkdir(parents=True,exist_ok=True)
    temporary=args.output.with_suffix(".partial")
    entries=[]
    with temporary.open("wb") as out:
        out.write(HEADER.pack(b"G3NXRAW1",len(scenes),0))
        offset=HEADER.size+len(scenes)*RECORD.size
        for name,page in scenes:
            size=page.width*page.height*4
            out.write(RECORD.pack(name.encode("ascii"),page.width,page.height,offset,size))
            entries.append({"name":name,"offset":offset,"size":size})
            offset += size
        for _,page in scenes:
            out.write(page.tobytes())
    temporary.replace(args.output)
    manifest={"sources":sources,"scenes":entries,"layout_evidence":
        "alpha.dll 0x100D5790; BGR15 at 0x100D585B; OBS360 at (320,244); OBS751 at (320,240)",
        "notes":"Original title graphics decoded from IDX/PAK; episode cards and notices are new frontend UI. No screenshots used."}
    args.output.with_suffix(".title.json").write_text(json.dumps(manifest,indent=2),encoding="utf-8")
    scenes[0][1].save(args.output.parent/"title-preview.png")
    scenes[4][1].save(args.output.parent/"episode-preview.png")
    print(f"Prepared {len(scenes)} frontend scenes: {args.output}")


if __name__=="__main__":
    main()
