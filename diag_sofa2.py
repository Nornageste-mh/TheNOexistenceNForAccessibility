# -*- coding: utf-8 -*-
"""Exact byte context around a bare 'Sofa' run."""
import re
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

raw = None
for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        d = o.read(check_read=False)
        if d.m_Script.read().m_ClassName == "Script" and d.m_Name == "Prologue1_6":
            raw = o.get_raw_data(); break
    except Exception:
        pass

print("raw len", len(raw))
for m in re.finditer(rb"Sofa", raw):
    h = m.start()
    after = raw[h+4:h+5]
    if after in (b".",):
        continue
    print("\noff=%d  after=%r" % (h, after))
    print("   before=%r" % raw[max(0, h-40):h])
    print("   after =%r" % raw[h:h+48])
    if h > 400000:
        break
