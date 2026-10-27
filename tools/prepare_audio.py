"""Decode original ADPCM BGM to the Switch backend's 48kHz stereo PCM WAV."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import wave
from g3_image import GameImage
from g3_resources import parse_index,read_resource
from g3_obs import decode_obs_actions

ROOT=Path(__file__).resolve().parent.parent


def prepare(image_path, tracks, output, battle_effects=False):
    output.mkdir(parents=True,exist_ok=True)
    manifest=[]
    with GameImage(image_path) as image, tempfile.TemporaryDirectory() as work:
        for track in sorted(set(tracks)):
            raw=image.read(f"G3P1/BGMs/{track:04d}.BGM")
            source=Path(work)/"source.wav"
            source.write_bytes(raw)
            target=output/f"bgm{track:04d}.wav"
            temporary=target.with_suffix(".partial")
            subprocess.run(["ffmpeg","-nostdin","-v","error","-y","-i",str(source),
                            "-vn","-ac","2","-ar","48000","-c:a","pcm_s16le","-f","wav",str(temporary)],check=True)
            with wave.open(str(temporary),"rb") as pcm:
                if (pcm.getnchannels(),pcm.getframerate(),pcm.getsampwidth())!=(2,48000,2) or not pcm.getnframes():
                    raise ValueError("Unexpected decoded audio format")
                frames=pcm.getnframes()
            temporary.replace(target)
            with target.open("rb") as stream: digest=hashlib.file_digest(stream,"sha256").hexdigest()
            manifest.append(dict(track=track,source_sha256=hashlib.sha256(raw).hexdigest(),
                                 sha256=digest,file=target.name,frames=frames,rate=48000,channels=2))
    if battle_effects:
        with GameImage(image_path) as image, tempfile.TemporaryDirectory() as work:
            obs_path="G3P1/Obss/Obss.idx";obs={r.name:r for r in parse_index(image.read(obs_path))}
            snd_path="G3P1/Snd/Snd.idx";sounds={r.name:r for r in parse_index(image.read(snd_path))}
            numbers=set()
            for model in (196,352,682,674,99,537):
                _,actions,_=decode_obs_actions(read_resource(image,obs_path,obs[f"{model:04d}.obs"],{}))
                numbers.update(c[3] for a in actions[6:12] for c in a["records"] if c[2]==1)
            effects=[]
            for number in sorted(numbers):
                raw=read_resource(image,snd_path,sounds[f"{number:04d}.snd"],{})
                source=Path(work)/"source.wav";source.write_bytes(raw)
                target=output/f"sfx{number:04d}.wav"
                subprocess.run(["ffmpeg","-nostdin","-v","error","-y","-i",str(source),"-vn","-ac","2","-ar","48000","-c:a","pcm_s16le",str(target)],check=True)
                with wave.open(str(target),"rb") as pcm:
                    if (pcm.getnchannels(),pcm.getframerate(),pcm.getsampwidth())!=(2,48000,2) or not pcm.getnframes():raise ValueError("Invalid effect PCM")
                    frames=pcm.getnframes()
                effects.append(dict(effect=number,file=target.name,frames=frames,sha256=hashlib.sha256(target.read_bytes()).hexdigest(),source_sha256=hashlib.sha256(raw).hexdigest()))
            (output/"effects.json").write_text(json.dumps(effects,indent=2)+"\n")
            print(f"Decoded {len(effects)} original first-battle attack sounds")
    (output/"manifest.json").write_text(json.dumps(manifest,indent=2)+"\n")
    print(f"Decoded and checked {len(manifest)} original BGM tracks")
    return manifest


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image",type=Path)
    parser.add_argument("--tracks",type=int,nargs="+",default=[42])
    parser.add_argument("--output",type=Path,default=ROOT/"dist/switch/g3nx/audio")
    parser.add_argument("--battle-effects",action="store_true")
    args=parser.parse_args()
    prepare(args.image,args.tracks,args.output,args.battle_effects)
