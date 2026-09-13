# -*- coding: utf-8 -*-
"""Dump raw localization docs verbatim so we can see the true format (author labels etc.)."""
import sys, io, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUT = r"D:\DSHWorkBase\noexistence_a11y\script_zh\_docs_raw.txt"

env = UnityPy.load(DATA)
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

f = open(OUT, "w", encoding="utf-8")
f.write("### all TextAsset doc names (%d) ###\n" % len(by_name))
for n in sorted(by_name):
    f.write("   %-40s %d bytes\n" % (n, len(by_name[n][0])))
f.write("\n\n")

for target in ["CharacterNames", "Prologue0_0", "Prologue2_1"]:
    if target not in by_name:
        f.write("### %s NOT FOUND ###\n\n" % target)
        continue
    body = by_name[target][0]
    f.write("=" * 70 + "\n### RAW %s (len=%d) — first 90 lines ###\n" % (target, len(body)) + "=" * 70 + "\n")
    for i, ln in enumerate(body.splitlines()[:90]):
        f.write("%4d| %s\n" % (i, ln))
    f.write("\n\n")

f.close()
print("written", OUT)
