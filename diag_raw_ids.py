# -*- coding: utf-8 -*-
"""Raw-byte inspection of the missing speaker IDs."""
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

for tok in (b"Sofa", b"King", b"Nurse", b"Lilith", b"CG_7"):
    hits = [m.start() for m in re.finditer(re.escape(tok) + b"\x00", raw)]
    print("=== %s\\0 : %d 次 ===" % (tok.decode(), len(hits)))
    for h in hits[:4]:
        pre = raw[max(0, h-10):h]
        print("    off=%-7d prefix=%r  ctx=%r" % (h, pre, raw[max(0, h-24):h+16]))

# what longer strings contain Sofa?
print()
runs = re.findall(rb"[\x20-\x7e]{3,60}", raw)
sofa = [r for r in runs if b"Sofa" in r]
print("包含 'Sofa' 的可打印串（去重前 %d）：" % len(sofa))
for r in sorted(set(sofa))[:25]:
    print("   ", r.decode("ascii", "replace"))

print()
lil = [r for r in runs if b"Lilith" in r]
print("包含 'Lilith' 的可打印串（去重前 %d）：" % len(lil))
for r in sorted(set(lil))[:25]:
    print("   ", r.decode("ascii", "replace"))
