# -*- coding: utf-8 -*-
"""Compare the CharacterNames annotations across every locale copy."""
import re
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
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

KEYS = ["CG_8", "Sofa", "Table", "Character", "Microwave", "Ferriswheel",
        "Oven", "Mysterious_Girl", "Lilith", "Nurse"]
print("copies:", len(copies))
for i, body in enumerate(copies):
    lines = body.splitlines()
    hdr = lines[0] if lines else ""
    print("\n=== copy #%d : %s" % (i, hdr[:110]))
    for j, ln in enumerate(lines):
        m = re.match(r"^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$", ln)
        if m and m.group(1) in KEYS:
            ann = lines[j - 1].strip() if j > 0 else ""
            print("    %-16s ann=%-14s val=%s" % (m.group(1), ann, m.group(2)))
