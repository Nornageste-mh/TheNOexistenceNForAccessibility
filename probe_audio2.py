# -*- coding: utf-8 -*-
import sys, io, collections, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy
DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)
names = []
for obj in env.objects:
    if obj.type.name == "AudioClip":
        try:
            names.append(obj.read().m_Name)
        except Exception:
            names.append("<err>")
distinct = sorted(set(names))
print("clips:", len(names), "distinct:", len(distinct))

buckets = collections.Counter()
for n in distinct:
    if re.match(r"^vo_cn", n, re.I): buckets["voice_CN"] += 1
    elif re.match(r"^vo_jp|^vo_ja", n, re.I): buckets["voice_JP"] += 1
    elif re.match(r"^vo_es", n, re.I): buckets["voice_ES"] += 1
    elif re.match(r"^vo_", n, re.I): buckets["voice_other"] += 1
    elif re.match(r"^~[0-9a-f]{5,}$", n): buckets["lineID_hex"] += 1
    elif re.match(r"^\d+$", n): buckets["numeric"] += 1
    elif re.match(r"^(se|sfx)", n, re.I): buckets["sfx"] += 1
    elif re.match(r"^(bgm|bg|music)", n, re.I): buckets["bgm/bg"] += 1
    else: buckets["other"] += 1
print("\n=== buckets (distinct names) ===")
for k, v in buckets.most_common():
    print(f"  {v:>5}  {k}")

print("\n=== all vo_* names (first 60) ===")
for n in [x for x in distinct if x.lower().startswith("vo_")][:60]:
    print("  ", n)
print("\n=== all vo_* names (last 30) ===")
for n in [x for x in distinct if x.lower().startswith("vo_")][-30:]:
    print("  ", n)

print("\n=== line-ID-hex names (first 40) ===")
for n in [x for x in distinct if re.match(r"^~[0-9a-f]{5,}$", x)][:40]:
    print("  ", n)

print("\n=== 'other' bucket sample (first 60) ===")
other = [x for x in distinct if not (re.match(r"^vo_|^~[0-9a-f]{5,}$|^\d+$|^(se|sfx|bgm|bg|music)", x, re.I))]
for n in other[:60]:
    print("  ", n)
print("other total:", len(other))
