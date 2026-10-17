#!/usr/bin/env python3
"""Synthesises AVAManicMiner's sound effects and tunes into ../src/AVAManicMiner/Sounds as 16-bit mono WAV.

Nothing is sampled from any game: every effect is built here from square, triangle and noise voices. The cavern
tune is Grieg's "In the Hall of the Mountain King" and the title tune Strauss's "The Blue Danube", both long in
the public domain, arranged here for two bleeping voices. The warning when the air runs low is spoken by the
formant voice in speech.py. Run it from anywhere:
    python3 tools/make_sounds.py
"""
import math
import os
import random
import struct
import wave

from speech import say



RATE = 22050
OUT = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "src", "AVAManicMiner", "Sounds")
RNG = random.Random(1983)
def write(name, samples, volume=0.7):
    peak = max(1e-9, max(abs(s) for s in samples))
    with wave.open(os.path.join(OUT, name), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(RATE)
        w.writeframes(b"".join(struct.pack("<h", int(s / peak * volume * 32767)) for s in samples))


def voice(kind, phase):
    p = phase % 1.0
    if kind == "square":
        return 1.0 if p < 0.5 else -1.0
    if kind == "pulse":
        return 1.0 if p < 0.25 else -1.0
    if kind == "tri":
        return 4 * p - 1 if p < 0.5 else 3 - 4 * p
    if kind == "saw":
        return 2 * p - 1
    if kind == "sine":
        return math.sin(2 * math.pi * p)
    return RNG.uniform(-1, 1)


def tone(seconds, f0, f1=None, kind="square", attack=0.004, release=1.0, vibrato=0.0, vib_rate=8.0, gain=1.0):
    """A note gliding from f0 to f1 (exponentially), with a short attack and a shaped decay."""
    n = int(RATE * seconds)
    f1 = f1 or f0
    out, phase, held = [], 0.0, 0.0
    for i in range(n):
        t = i / RATE
        f = f0 * (f1 / f0) ** (i / max(1, n - 1))
        if vibrato:
            f *= 1 + vibrato * math.sin(2 * math.pi * vib_rate * t)
        phase += f / RATE
        if kind == "noise":
            # Noise re-sampled at the voice's rate: a lower 'pitch' sounds rougher.
            if int(phase) != int(phase - f / RATE):
                held = RNG.uniform(-1, 1)
            s = held
        else:
            s = voice(kind, phase)
        env = min(1.0, t / attack) * (1 - i / n) ** release
        out.append(s * env * gain)
    return out


def silence(seconds):
    return [0.0] * int(RATE * seconds)


def cat(*parts):
    out = []
    for p in parts:
        out.extend(p)
    return out


def mix(*parts):
    n = max(len(p) for p in parts)
    out = [0.0] * n
    for p in parts:
        for i, v in enumerate(p):
            out[i] += v
    return out


NOTES = {"C": 0, "D": 2, "E": 4, "F": 5, "G": 7, "A": 9, "B": 11}


def hz(note):
    n = NOTES[note[0]]
    rest = note[1:]
    while rest and rest[0] in "#b":
        n += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((12 * (int(rest) + 1) + n - 69) / 12)


def tune(bpm, lead, bass, kind="pulse"):
    """Two voices: a lead and a bass line, as (note, beats) lists; '-' is a rest."""
    beat = 60.0 / bpm

    def line(notes, k, gain, release):
        out = []
        for note, beats in notes:
            d = beats * beat
            out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=k, release=release, gain=gain))
        return out

    return mix(line(lead, kind, 0.55, 0.6), line(bass, "tri", 0.6, 0.3))


def seq(bpm, notes, kind="pulse", gain=0.55, release=0.6):
    beat = 60.0 / bpm
    out = []
    for note, beats in notes:
        d = beats * beat
        out.extend(silence(d) if note == "-" else tone(d, hz(note), kind=kind, release=release, gain=gain))
    return out




def main():
    os.makedirs(OUT, exist_ok=True)
    for f in os.listdir(OUT):
        if f.endswith(".wav"):
            os.remove(os.path.join(OUT, f))
    # The miner.
    write("Jump.wav", cat(tone(0.16, 420, 900, "square", attack=0.002, release=0.3, gain=0.5), tone(0.16, 900, 420, "square", release=0.6, gain=0.5)), 0.12)
    write("Land.wav", tone(0.04, 180, 90, "square", attack=0.001, release=1.2, gain=0.5), 0.1)
    write("Key.wav", cat(*[tone(0.05, hz(n), kind="square", release=0.4) for n in ["E6", "G6", "C7"]]), 0.22)
    write("PortalOpen.wav", cat(*[tone(0.06, hz(n), kind="pulse", release=0.3) for n in ["C5", "E5", "G5", "C6", "E6", "G6", "C7"]], tone(0.3, hz("C7"), kind="pulse", vibrato=0.02, release=0.5)), 0.28)
    write("Portal.wav", cat(*[tone(0.04, 200 + i * 90, kind="square", release=0.2, gain=0.6) for i in range(24)]), 0.26)
    write("Crumble.wav", tone(0.25, 900, 200, "noise", attack=0.002, release=1.0, gain=0.6), 0.2)
    write("Die.wav", cat(*[tone(0.035, 1600 - i * 60, kind="square", release=0.2, gain=0.6) for i in range(24)]), 0.32)
    write("ExtraLife.wav", cat(*[tone(0.07, hz(n), kind="pulse", release=0.3) for n in ["G5", "B5", "D6", "G6", "D6", "G6"]]), 0.3)
    # The air counted into the score: a click a second's worth.
    write("Air.wav", tone(0.025, 1800, 1200, "square", attack=0.001, release=1.0, gain=0.5), 0.12)
    write("Stomp.wav", mix(tone(0.5, 90, 35, "sine", attack=0.002, release=0.9), tone(0.35, 700, 120, "noise", attack=0.001, release=1.0, gain=0.7)), 0.5)
    # "Air!" - when there's little left.
    words = "EH1 R !"
    write("SayAir.wav", mix(say(words, rate=RATE, pitch=120, speed=1.0, size=1.0), [s * 0.3 for s in say(words, rate=RATE, pitch=180, speed=1.0, size=1.0, seed=11)]), 0.5)

    # In the Hall of the Mountain King (Grieg, 1875): the theme, then the theme a fourth up, round and round.
    A = [("B3", 0.5), ("C#4", 0.5), ("D4", 0.5), ("E4", 0.5), ("F#4", 0.5), ("D4", 0.5), ("F#4", 1),
         ("F4", 0.5), ("C#4", 0.5), ("F4", 1), ("E4", 0.5), ("C4", 0.5), ("E4", 1)]
    A2 = A[:7] + [("B4", 0.5), ("F#4", 0.5), ("D4", 0.5), ("F#4", 0.5), ("B4", 2)]
    lead = [(n[:-1] + str(int(n[-1]) + 1), b) for n, b in A + A2 + A + A2]
    bass = [("B2", 1), ("F#2", 1)] * 4 + [("B2", 1), ("F#2", 1), ("F#2", 1), ("B2", 1)] + [("B2", 1), ("F#2", 1)] * 2 + \
           [("B2", 1), ("F#2", 1)] * 4 + [("B2", 1), ("F#2", 1), ("F#2", 1), ("B2", 1)] + [("B2", 1), ("F#2", 1)] * 2
    write("Music.wav", tune(176, lead, bass, kind="pulse"), 0.26)
    # The Blue Danube (Strauss, 1866), the opening waltz phrase.
    danube = [("D4", 1), ("D4", 1), ("F#4", 1), ("A4", 1), ("-", 1), ("A5", 1), ("A5", 1), ("-", 1), ("F#5", 1), ("F#5", 1), ("-", 1),
              ("D4", 1), ("D4", 1), ("F#4", 1), ("A4", 1), ("-", 1), ("A5", 1), ("A5", 1), ("-", 1), ("G5", 1), ("G5", 1), ("-", 1),
              ("C#4", 1), ("C#4", 1), ("E4", 1), ("B4", 1), ("-", 1), ("B5", 1), ("B5", 1), ("-", 1), ("G5", 1), ("G5", 1), ("-", 1),
              ("C#4", 1), ("C#4", 1), ("E4", 1), ("B4", 1), ("-", 1), ("B5", 1), ("B5", 1), ("-", 1), ("F#5", 1), ("F#5", 1), ("-", 1)]
    waltz = []
    for root in ["D3", "D3", "D3", "D3", "A2", "A2", "A2", "A2", "A2", "A2", "A2", "A2", "D3", "D3", "D3"]:
        waltz += [(root, 1), ("-", 2)]
    write("TitleTune.wav", cat(tune(200, danube, waltz, kind="pulse"), silence(0.8)), 0.36)
    write("Victory.wav", tune(150, [("C5", 0.5), ("E5", 0.5), ("G5", 0.5), ("C6", 1.5), ("B5", 0.5), ("C6", 0.5), ("D6", 0.5), ("E6", 3)],
                              [("C3", 1), ("G2", 1), ("A2", 1), ("F2", 1), ("G2", 1), ("C3", 2.5)], kind="pulse"), 0.4)
    write("GameOver.wav", tune(70, [("A4", 1), ("F4", 1), ("D4", 1), ("A3", 1), ("D4", 3)], [("D3", 2), ("A#2", 2), ("D2", 3)], kind="tri"), 0.42)


if __name__ == "__main__":
    main()
