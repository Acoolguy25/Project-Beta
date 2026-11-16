"""Synthesises Classic Horror's fear-loop sounds (heartbeat, breathing, radio static) as 44.1 kHz mono WAVs.

Usage: python3 -I synth_horror_audio.py <out_dir>
"""
import sys
import wave
from pathlib import Path

import numpy as np

RATE = 44100
rng = np.random.default_rng(1913)


def lowpass(signal, cutoff):
    """One-pole low-pass, applied twice for a gentler 12 dB/oct slope."""
    alpha = 1.0 - np.exp(-2.0 * np.pi * cutoff / RATE)
    out = signal
    for _ in range(2):
        y = np.empty_like(out)
        acc = 0.0
        for i, x in enumerate(out):
            acc += alpha * (x - acc)
            y[i] = acc
        out = y
    return out


def highpass(signal, cutoff):
    return signal - lowpass(signal, cutoff)


def bandpass(signal, low, high):
    return lowpass(highpass(signal, low), high)


def normalise(signal, peak=0.89):
    return signal / max(1e-9, np.max(np.abs(signal))) * peak


def save(path, signal):
    data = (np.clip(signal, -1, 1) * 32767).astype(np.int16)
    with wave.open(str(path), "wb") as f:
        f.setnchannels(1)
        f.setsampwidth(2)
        f.setframerate(RATE)
        f.writeframes(data.tobytes())


def thump(length, freq, decay, start_freq_ratio=1.6):
    t = np.arange(int(length * RATE)) / RATE
    # A falling pitch makes a body thump rather than a tone.
    f = freq * (1 + (start_freq_ratio - 1) * np.exp(-t * 30))
    phase = 2 * np.pi * np.cumsum(f) / RATE
    env = (1 - np.exp(-t * 400)) * np.exp(-t * decay)
    body = np.sin(phase) * env
    click = lowpass(rng.standard_normal(len(t)) * np.exp(-t * 160), 900) * 0.35
    return body + click


def heartbeat():
    """One 'lub-dub'. The client schedules beats, so the rate follows fear without pitch changes."""
    total = np.zeros(int(0.62 * RATE))
    lub = thump(0.30, 52, 15)
    dub = thump(0.26, 64, 19) * 0.72
    total[:len(lub)] += lub
    offset = int(0.24 * RATE)
    total[offset:offset + len(dub)] += dub
    return normalise(lowpass(total, 260))


def breath(length, inhale):
    n = int(length * RATE)
    t = np.linspace(0, 1, n)
    noise = rng.standard_normal(n)
    # Inhale is airier and rising; exhale warmer and falling.
    shaped = bandpass(noise, 380 if inhale else 260, 2600 if inhale else 1700)
    # A second low-pass pair keeps it a breath rather than a hiss.
    shaped = lowpass(shaped, 2000 if inhale else 1300)
    env = np.sin(np.pi * t) ** (1.6 if inhale else 1.2)
    env *= (0.75 + 0.25 * t) if inhale else (1.0 - 0.35 * t)
    flutter = 1 + 0.08 * np.sin(2 * np.pi * 7.5 * t * length)
    return shaped * env * flutter


def breathing():
    """A seamless in/out loop, about 2.9 s, for a nervous investigator."""
    parts = [breath(1.05, True), np.zeros(int(0.12 * RATE)), breath(1.35, False), np.zeros(int(0.38 * RATE))]
    loop = np.concatenate(parts)
    fade = int(0.02 * RATE)
    loop[:fade] *= np.linspace(0, 1, fade)
    loop[-fade:] *= np.linspace(1, 0, fade)
    return normalise(loop, 0.8)


def radio_static():
    """A 0.55 s squelch: a key-up click, band-limited hiss with crackle, and a short tail."""
    n = int(0.55 * RATE)
    t = np.arange(n) / RATE
    hiss = bandpass(rng.standard_normal(n), 900, 3800)
    crackle = (rng.random(n) > 0.9975) * rng.standard_normal(n) * 4.0
    crackle = bandpass(crackle, 1200, 5000)
    env = np.minimum(1, t / 0.008) * np.where(t < 0.38, 1.0, np.exp(-(t - 0.38) * 26))
    wobble = 1 + 0.25 * np.sin(2 * np.pi * 11 * t)
    click = np.zeros(n)
    click[:int(0.006 * RATE)] = np.hanning(int(0.006 * RATE)) * 1.6
    return normalise(lowpass((hiss * wobble * 0.6 + crackle) * env, 4200) + click, 0.7)


def main():
    out = Path(sys.argv[1])
    out.mkdir(parents=True, exist_ok=True)
    save(out / "heartbeat.wav", heartbeat())
    save(out / "breathing.wav", breathing())
    save(out / "radio-static.wav", radio_static())


if __name__ == "__main__":
    main()
