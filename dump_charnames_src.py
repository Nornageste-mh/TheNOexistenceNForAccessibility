# -*- coding: utf-8 -*-
"""Dump the two odd CharacterNames copies (CSV form + source form) verbatim."""
import collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUT = r"D:\DSHWorkBase\noexistence_a11y\script_zh\_charnames_src.txt"
env = UnityPy.load(DATA)

copies = []
for o in env.objects:
    if o.type.name != "TextAsset":
        continue
    try:
        d = o.read()
        s = getattr(d, "m_Script", b"")
        if getattr(d, "m_Name", "?") == "CharacterNames":
            copies.append(s if isinstance(s, str) else bytes(s).decode("utf-8", "replace"))
    except Exception:
        pass

f = open(OUT, "w", encoding="utf-8")
for idx in (12, 13):
    body = copies[idx]
    f.write("=" * 72 + "\n### copy #%d  chars=%d ###\n" % (idx, len(body)) + "=" * 72 + "\n")
    for i, ln in enumerate(body.splitlines()):
        f.write("%4d| %r\n" % (i, ln))
    f.write("\n\n")
f.close()
print("written", OUT)
