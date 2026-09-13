# -*- coding: utf-8 -*-
"""Final probe: voicing coverage, video assets, and text-in-image risk."""
import sys, io, collections, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

audio, video, sprites, fonts = [], [], [], []
for obj in env.objects:
    t = obj.type.name
    if t == "AudioClip":
        try:
            d = obj.read()
            audio.append((getattr(d, "m_Name", "?"), int(getattr(d, "m_Length", 0) or 0), int(getattr(d, "m_Frequency", 0) or 0)))
        except Exception:
            audio.append(("<err>", 0, 0))
    elif t == "VideoClip":
        try:
            d = obj.read()
            video.append(getattr(d, "m_Name", "?"))
        except Exception:
            video.append("<err>")
    elif t == "Font":
        try:
            d = obj.read()
            fonts.append(getattr(d, "m_Name", "?"))
        except Exception:
            fonts.append("<err>")

print("AudioClips:", len(audio), " VideoClips:", len(video), " Fonts:", len(fonts))
print("\n=== Font names (Chinese glyph coverage source) ===")
for f in sorted(set(fonts)):
    print("  ", f)

print("\n=== VideoClip names ===")
for v in sorted(set(video)):
    print("  ", v)

print("\n=== AudioClip name analysis ===")
names = [a[0] for a in audio]
pref = collections.Counter()
for n in names:
    m = re.match(r"^([A-Za-z_]+?)[_\-/]?\d*$", n or "")
    pref[m.group(1) if m else (n or "?")[:14]] += 1
print("total:", len(names), " distinct:", len(set(names)))
print("\ntop 40 name-prefixes:")
for k, v in pref.most_common(40):
    print(f"  {v:>5}  {k}")

print("\n=== 40 sample clip names (sorted) ===")
for n in sorted(set(names))[:40]:
    print("  ", n)

print("\n=== 40 sample clip names (sorted, tail) ===")
for n in sorted(set(names))[-40:]:
    print("  ", n)

# durations: total voiced seconds
tot = sum(a[1] for a in audio) / 1000.0 if audio and audio[0][1] > 1000 else sum(a[1] for a in audio)
print("\nsum of clip lengths (raw m_Length):", sum(a[1] for a in audio), " -> approx seconds:", round(tot, 1))
print("clips longer than 3s:", sum(1 for a in audio if a[1] > (3000 if a[1] > 1000 else 3)))
