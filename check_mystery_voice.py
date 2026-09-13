# -*- coding: utf-8 -*-
"""Per-line voicing for the mystery scene, plus a full author<->line map attempt."""
import re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

# audio clips
clips = set()
for o in env.objects:
    if o.type.name == "AudioClip":
        try:
            clips.add(o.read().m_Name)
        except Exception:
            pass
id_clips = {c for c in clips if re.fullmatch(r"~[0-9a-f]{5,}", c)}

# localization docs
by_name = collections.defaultdict(list)
for o in env.objects:
    if o.type.name == "TextAsset":
        try:
            d = o.read()
            s = getattr(d, "m_Script", b"")
            by_name[getattr(d, "m_Name", "?")].append(
                s if isinstance(s, str) else bytes(s).decode("utf-8", "replace"))
        except Exception:
            pass


def doc_lines(name):
    body = by_name[name][0]
    out, cur = [], None
    for ln in body.splitlines():
        m = re.match(r"^#\s*(\S+)", ln)
        if m:
            cur = m.group(1); continue
        if ln.startswith(";") and cur:
            out.append((cur, ln[1:].strip())); cur = None
    return out


def norm(s):
    s = re.sub(r"<[^>]+>", "", s)
    return re.sub(r"[\s\u3000]+", "", s)


def voiced(lid, zh):
    if lid in id_clips:
        return "id"
    n = norm(zh)
    if len(n) >= 4:
        for L in (14, 12, 10, 8, 6):
            if len(n) >= L and any(c.startswith(n[:L]) for c in clips):
                return "text"
    return "-"


print("=" * 70)
print("Prologue0_0  ——  ??? 的唯一出场（开场 11 行）")
print("=" * 70)
rows = doc_lines("Prologue0_0")
for lid, zh in rows:
    print("   %-6s vo=%-5s %s" % (lid, voiced(lid, zh), zh[:60]))
print("   小计 %d 行" % len(rows))

print()
print("=" * 70)
print("Prologue0_1  —— 神秘少女 / 莉莉丝 / 家具 混场")
print("=" * 70)
rows = doc_lines("Prologue0_1")
for lid, zh in rows[:40]:
    print("   %-12s vo=%-5s %s" % (lid, voiced(lid, zh), zh[:58]))
print("   ... 该场共 %d 行" % len(rows))

print()
print("=" * 70)
print("全局统计：有配音 vs 需TTS")
print("=" * 70)
tot = v = 0
for name in sorted(by_name):
    rows = doc_lines(name)
    if not rows:
        continue
    for lid, zh in rows:
        tot += 1
        if voiced(lid, zh) != "-":
            v += 1
print("   去重前总行 %d，有配音 %d (%.1f%%)" % (tot, v, 100.0 * v / max(1, tot)))
