# -*- coding: utf-8 -*-
"""Verify the voicing detector against real AudioClip names, and debug the failed script."""
import re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

clips = []
for o in env.objects:
    if o.type.name == "AudioClip":
        try:
            clips.append(o.read().m_Name)
        except Exception:
            pass
id_clips = sorted(c for c in clips if re.fullmatch(r"~[0-9a-f]{5,}", c))
other = sorted(c for c in clips if c not in set(id_clips))

print("AudioClip 总数 %d | 行号式 %d | 其它 %d" % (len(clips), len(id_clips), len(other)))
print("\n--- 行号式样本 ---")
for c in id_clips[:8]:
    print("   ", c)
print("\n--- 其它命名样本（前 60）---")
for c in other[:60]:
    print("   ", c)

print("\n--- 含中文的片段名（前 40）---")
zh = [c for c in other if re.search(r"[\u4e00-\u9fff]", c)]
print("   含中文的片段共 %d 个" % len(zh))
for c in zh[:40]:
    print("   ", c[:70])

print("\n--- 开场 Prologue0_0 每行命中的片段 ---")
def norm(s):
    s = re.sub(r"<[^>]+>", "", s)
    return re.sub(r"[\s\u3000]+", "", s)

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

body = by_name["Prologue0_0"][0]
cur = None
for ln in body.splitlines():
    m = re.match(r"^#\s*(\S+)", ln)
    if m:
        cur = m.group(1); continue
    if ln.startswith(";") and cur:
        t = ln[1:].strip()
        n = norm(t)
        hits = []
        if cur in set(id_clips):
            hits.append("<行号 %s>" % cur)
        if len(n) >= 4:
            for L in (14, 12, 10, 8, 6):
                if len(n) >= L:
                    h = [c for c in other if c.startswith(n[:L])]
                    if h:
                        hits.append(h[0][:60]); break
        print("   %-4s %-34s -> %s" % (cur, t[:32], hits or "无"))
        cur = None
