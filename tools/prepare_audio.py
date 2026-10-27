"""Decode original ADPCM BGM to the Switch backend's 48kHz stereo PCM WAV."""
import argparse
import hashlib
import json
from pathlib import Path
import subprocess
import tempfile
import wave
from g3_image import GameImage

ROOT=Path(__file__).resolve().parent.parent


def prepare(image_path, tracks, output):
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
    (output/"manifest.json").write_text(json.dumps(manifest,indent=2)+"\n")
    print(f"Decoded and checked {len(manifest)} original BGM tracks")
    return manifest


if __name__=="__main__":
    parser=argparse.ArgumentParser(description=__doc__)
    parser.add_argument("image",type=Path)
    parser.add_argument("--tracks",type=int,nargs="+",default=[42])
    parser.add_argument("--output",type=Path,default=ROOT/"dist/switch/g3nx/audio")
    args=parser.parse_args()
    prepare(args.image,args.tracks,args.output)
