"""PCM backend integrity, end-of-stream and loop-boundary behavior."""
import ctypes as C
import io
from pathlib import Path
import struct
import tempfile
import unittest
import wave


class Pcm(C.Structure):
    _fields_=[('file',C.c_void_p),('offset',C.c_uint32),('bytes',C.c_uint32),('at',C.c_uint32),('loop',C.c_int)]


class AudioTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.lib=C.CDLL(str(Path('homebrew/build/g3_formats.dll').resolve()))
        cls.lib.g3_pcm_open.argtypes=[C.POINTER(Pcm),C.c_char_p,C.c_int]
        cls.lib.g3_pcm_close.argtypes=[C.POINTER(Pcm)]
        cls.lib.g3_pcm_read.argtypes=[C.POINTER(Pcm),C.POINTER(C.c_int16),C.c_size_t]
        cls.lib.g3_pcm_read.restype=C.c_size_t

    def wav(self,rate=48000):
        out=io.BytesIO()
        with wave.open(out,'wb') as w:
            w.setnchannels(2);w.setsampwidth(2);w.setframerate(rate)
            w.writeframes(struct.pack('<6h',1,-1,32000,-32000,7,9))
        return out.getvalue()

    def test_finite_stream_and_exact_loop_wrap(self):
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/'sample.wav';path.write_bytes(self.wav())
            for loop in (0,1):
                pcm=Pcm();self.assertEqual(self.lib.g3_pcm_open(C.byref(pcm),str(path).encode(),loop),1)
                try:
                    output=(C.c_int16*16)(*([123]*16))
                    self.assertEqual(self.lib.g3_pcm_read(C.byref(pcm),output,8),8 if loop else 3)
                    expected=([1,-1,32000,-32000,7,9]*3)[:16] if loop else [1,-1,32000,-32000,7,9]+[123]*10
                    self.assertEqual(list(output),expected)
                    if not loop:self.assertEqual(self.lib.g3_pcm_read(C.byref(pcm),output,1),0)
                finally:self.lib.g3_pcm_close(C.byref(pcm))

    def test_malformed_truncated_wrong_format_and_partial_frame_rejected(self):
        valid=self.wav();bad_frame=bytearray(valid[:-1]);struct.pack_into('<I',bad_frame,4,len(bad_frame)-8)
        variants=[valid[:n] for n in (0,4,12,len(valid)-1)]+[self.wav(44100),bytes(bad_frame)]
        with tempfile.TemporaryDirectory() as d:
            path=Path(d)/'sample.wav'
            for raw in variants:
                path.write_bytes(raw);pcm=Pcm()
                self.assertEqual(self.lib.g3_pcm_open(C.byref(pcm),str(path).encode(),0),0)
                self.assertFalse(pcm.file)

    def test_original_music_conversion_matches_wave_reader(self):
        for track in (35,42):
            path=Path(f'dist/switch/g3nx/audio/bgm{track:04d}.wav')
            pcm=Pcm();self.assertEqual(self.lib.g3_pcm_open(C.byref(pcm),str(path).encode(),1),1)
            try:
                with wave.open(str(path),'rb') as w:
                    expected=w.readframes(4096)
                    self.assertEqual(w.getframerate(),48000)
                    self.assertGreater(w.getnframes(),48000)
                buffer=(C.c_int16*(4096*2))()
                self.assertEqual(self.lib.g3_pcm_read(C.byref(pcm),buffer,4096),4096)
                self.assertEqual(bytes(buffer),expected)
                self.assertNotEqual(expected,bytes(len(expected)))
            finally:self.lib.g3_pcm_close(C.byref(pcm))


if __name__=='__main__':unittest.main()
