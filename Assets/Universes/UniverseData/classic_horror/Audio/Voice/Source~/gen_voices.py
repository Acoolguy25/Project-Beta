"""Generates The Drowned Choir's voice lines from lines.tsv.

Text-to-speech is Piper (https://github.com/rhasspy/piper, MIT) with the LibriTTS-R medium voice
(en_US-libritts_r-medium, trained on LibriTTS-R, CC BY 4.0). Each profile picks a speaker and a
processing chain (radio band-pass, whisper doubling, ring modulation, reverb) so that the priest, Lily,
Mara and the choir sound like different people in different places.

Usage: python3 -I gen_voices.py <voice.onnx> <out_dir> [line_id ...]
Writes <out_dir>/<line_id>.ogg (44.1 kHz mono Vorbis, via ffmpeg).
"""
import subprocess
import sys
import tempfile
import wave
from pathlib import Path

import numpy as np
from scipy import signal

RATE = 44100
HERE = Path(__file__).resolve().parent

# speaker: LibriTTS-R speaker index; length: Piper length_scale (>1 is slower); pitch: semitones by resampling.
PROFILES = {
    "crane":        dict(speaker=137, length=1.25, pitch=-2.0, chain="crane"),
    "crane_loud":   dict(speaker=137, length=1.15, pitch=-2.0, chain="crane_loud"),
    "crane_scream": dict(speaker=137, length=1.55, pitch=-3.0, chain="crane_scream"),
    "lily":         dict(speaker=29,  length=1.05, pitch=3.5,  chain="lily"),
    "mara":         dict(speaker=378, length=0.95, pitch=0.0,  chain="room"),
    "mara_radio":   dict(speaker=378, length=0.92, pitch=0.0,  chain="radio"),
    "mara_far":     dict(speaker=378, length=1.05, pitch=-0.5, chain="radio_far"),
    "mara_scream":  dict(speaker=378, length=0.8,  pitch=2.0,  chain="scream"),
    "ghost_f":      dict(speaker=807, length=1.2,  pitch=-1.0, chain="ghost"),
    "ghost_f2":     dict(speaker=15,  length=1.2,  pitch=-1.0, chain="ghost"),
    "ghost_f3":     dict(speaker=265, length=1.2,  pitch=-1.0, chain="ghost"),
    "ghost_m":      dict(speaker=36,  length=1.2,  pitch=-1.0, chain="ghost"),
    "ghost_m2":     dict(speaker=564, length=1.2,  pitch=-1.0, chain="ghost"),
    "whisper":      dict(speaker=731, length=1.3,  pitch=-1.0, chain="whisper"),
}

rng = np.random.default_rng(1962)


def read_lines():
    lines = []
    for raw in (HERE / "lines.tsv").read_text(encoding="utf-8").splitlines():
        if not raw.strip() or raw.startswith("#"):
            continue
        line_id, speaker, profile, text = raw.split("\t")
        lines.append((line_id, speaker, profile, text))
    return lines


def synthesize(voice, text, profile):
    from piper.config import SynthesisConfig
    config = SynthesisConfig(speaker_id=profile["speaker"], length_scale=profile["length"],
                             noise_scale=0.6, noise_w_scale=0.8)
    chunks = list(voice.synthesize(text, syn_config=config))
    audio = np.concatenate([c.audio_float_array for c in chunks]).astype(np.float64)
    return signal.resample_poly(audio, RATE, chunks[0].sample_rate)


def pitch_shift(audio, semitones):
    """Resampling shift: a lower voice is also slower, which suits the dead."""
    if abs(semitones) < 1e-3:
        return audio
    ratio = 2 ** (semitones / 12)
    up, down = int(round(1000 / ratio)), 1000
    return signal.resample_poly(audio, up, down)


def band(audio, low, high, order=4):
    sos = signal.butter(order, [low, high], btype="band", fs=RATE, output="sos")
    return signal.sosfilt(sos, audio)


def highpass(audio, cutoff):
    return signal.sosfilt(signal.butter(2, cutoff, btype="high", fs=RATE, output="sos"), audio)


def lowpass(audio, cutoff):
    return signal.sosfilt(signal.butter(2, cutoff, btype="low", fs=RATE, output="sos"), audio)


def whisper(audio):
    """Keeps the spectral envelope and throws away the pitch: the same words, breathed."""
    _, _, spec = signal.stft(audio, RATE, nperseg=1024, noverlap=768)
    phase = np.exp(2j * np.pi * rng.random(spec.shape))
    _, out = signal.istft(np.abs(spec) * phase, RATE, nperseg=1024, noverlap=768)
    return highpass(out[: len(audio)], 500)


def reverb(audio, seconds, wet, predelay=0.02, brightness=4000):
    n = int(seconds * RATE)
    t = np.arange(n) / RATE
    ir = rng.standard_normal(n) * np.exp(-6.9 * t / seconds)
    ir = lowpass(ir, brightness)
    ir[: int(predelay * RATE)] = 0
    ir /= np.sqrt(np.sum(ir ** 2)) + 1e-9
    tail = signal.fftconvolve(audio, ir)[: len(audio) + n]
    dry = np.pad(audio, (0, len(tail) - len(audio)))
    return dry * (1 - wet) + tail * wet * 1.4


def ring(audio, hz, amount):
    t = np.arange(len(audio)) / RATE
    return audio * (1 - amount) + audio * np.sin(2 * np.pi * hz * t) * amount


def drive(audio, gain):
    return np.tanh(audio * gain) / np.tanh(gain)


def dropouts(audio, rate=2.5, depth=0.85):
    """A bad signal: short random gaps in the transmission."""
    env = np.ones(len(audio))
    for _ in range(int(len(audio) / RATE * rate)):
        start = rng.integers(0, max(1, len(audio) - 4000))
        length = rng.integers(600, 3500)
        env[start:start + length] *= 1 - depth * rng.random()
    return audio * lowpass(env, 60)


def hiss(length, level):
    return band(rng.standard_normal(length), 1500, 7000) * level


def squelch(length=1800, level=0.25):
    burst = band(rng.standard_normal(length), 800, 5000) * np.linspace(1, 0, length) * level
    return burst


def radio(audio, far=False):
    out = band(audio, 320, 3200)
    out = drive(out * 1.6, 2.2 if not far else 3.5)
    if far:
        out = dropouts(reverb(out, 1.6, 0.35))
    out = out + hiss(len(out), 0.012 if not far else 0.03)
    return np.concatenate([squelch(), out, squelch(1400, 0.18)])


def chain(name, audio):
    if name == "crane":
        voice = ring(audio, 38, 0.35) + whisper(audio) * 0.35
        voice = voice + lowpass(audio, 300) * 0.6
        return reverb(voice, 2.6, 0.42, predelay=0.03, brightness=3000)
    if name == "crane_loud":
        voice = drive(ring(audio, 34, 0.4) * 1.5, 2.0) + whisper(audio) * 0.4 + lowpass(audio, 260) * 0.8
        return reverb(voice, 3.2, 0.45, predelay=0.03, brightness=3500)
    if name == "crane_scream":
        voice = drive(ring(audio, 29, 0.5) * 2.5, 4.0) + whisper(audio) * 0.7 + lowpass(audio, 220)
        return reverb(voice, 3.8, 0.5, predelay=0.02, brightness=5000)
    if name == "lily":
        voice = highpass(audio, 220) * 0.75 + whisper(audio) * 0.55
        echo = np.pad(voice, (int(0.23 * RATE), 0))[: len(voice)] * 0.3
        return reverb(voice + echo, 3.0, 0.55, predelay=0.05, brightness=6000)
    if name == "room":
        return reverb(highpass(audio, 90), 0.5, 0.14, predelay=0.008, brightness=6000)
    if name == "radio":
        return radio(audio)
    if name == "radio_far":
        return radio(audio, far=True)
    if name == "scream":
        return reverb(drive(highpass(audio, 150) * 2.2, 3.0), 0.9, 0.25, brightness=7000)
    if name == "ghost":
        voice = whisper(audio) * 0.85 + highpass(audio, 300) * 0.35
        return reverb(voice, 3.5, 0.6, predelay=0.06, brightness=5000)
    if name == "whisper":
        return reverb(whisper(audio) * 1.2 + highpass(audio, 1200) * 0.12, 0.6, 0.2, brightness=8000)
    raise ValueError(name)


def master(audio, target_rms=0.11, peak=0.89):
    audio = highpass(audio, 60)
    rms = np.sqrt(np.mean(audio[np.abs(audio) > 1e-4] ** 2)) + 1e-9
    audio = audio * (target_rms / rms)
    over = np.max(np.abs(audio))
    if over > peak:
        audio = np.tanh(audio / over * 1.2) / np.tanh(1.2) * peak
    fade = int(0.01 * RATE)
    audio[:fade] *= np.linspace(0, 1, fade)
    audio[-fade:] *= np.linspace(1, 0, fade)
    return np.concatenate([np.zeros(int(0.04 * RATE)), audio])


def write_ogg(path, audio):
    with tempfile.NamedTemporaryFile(suffix=".wav") as tmp:
        with wave.open(tmp.name, "wb") as out:
            out.setnchannels(1)
            out.setsampwidth(2)
            out.setframerate(RATE)
            out.writeframes((np.clip(audio, -1, 1) * 32767).astype(np.int16).tobytes())
        subprocess.run(["ffmpeg", "-loglevel", "error", "-y", "-i", tmp.name, "-c:a", "libvorbis", "-q:a", "5",
                        str(path)], check=True)


def main():
    from piper import PiperVoice
    voice = PiperVoice.load(sys.argv[1])
    out_dir = Path(sys.argv[2])
    only = set(sys.argv[3:])
    out_dir.mkdir(parents=True, exist_ok=True)
    for line_id, _speaker, profile_name, text in read_lines():
        if only and line_id not in only:
            continue
        profile = PROFILES[profile_name]
        audio = pitch_shift(synthesize(voice, text, profile), profile["pitch"])
        loud = profile_name in ("crane_scream", "mara_scream", "crane_loud")
        audio = master(chain(profile["chain"], audio), target_rms=0.15 if loud else 0.11)
        write_ogg(out_dir / f"{line_id}.ogg", audio)
        print(f"{line_id}: {len(audio) / RATE:.2f}s")


if __name__ == "__main__":
    main()
