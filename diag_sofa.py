# -*- coding: utf-8 -*-
"""Where do the missing speakers (Sofa, King, Nurse, CG_*, Lilith_1...) live in the bytecode?"""
import re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

def all_strings(buf):
    out, i, n = [], 0, len(buf)
    while i < n - 5:
        L = int.from_bytes(buf[i:i+4], "little")
        if 1 <= L <= 80 and i + 4 + L < n and buf[i+4+L] == 0:
            try:
                s = buf[i+4:i+4+L].decode("utf-8")
            except UnicodeDecodeError:
                i += 1; continue
            if all(ord(c) >= 0x20 for c in s):
                out.append((i, s)); i += 4 + L + 1; continue
        i += 1
    return out

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

S = all_strings(raw)
vals = [s for _, s in S]
print("Prologue1_6 strings=%d" % len(vals))

# every position of Sofa and its neighbours
idx = [i for i, v in enumerate(vals) if v == "Sofa"]
print("'Sofa' 出现 %d 次" % len(idx))
for i in idx[:6]:
    print("   ... %s" % vals[max(0, i-6):i+4])

print()
# anchors and payloads
an = [i for i in range(len(vals) - 1)
      if vals[i] == "Naninovel.Commands" and vals[i+1] == "Elringus.Naninovel.Runtime"]
print("anchors=%d" % len(an))
cmds = collections.Counter(vals[i-1] for i in an if i >= 1)
print("command names:", cmds.most_common(12))

pt = [i for i in an if i >= 1 and vals[i-1] == "PrintText"]
print("\nPrintText payloads (%d):" % len(pt))
for k, i in enumerate(pt[:12]):
    end = an[an.index(i)+1] - 1 if an.index(i) + 1 < len(an) else len(vals)
    print("   %2d %s" % (k, vals[i+2:end][:8]))

# do ANY payloads contain Sofa?
hits = 0
for k, i in enumerate(pt):
    end = an[an.index(i)+1] - 1 if an.index(i) + 1 < len(an) else len(vals)
    pl = vals[i+2:end]
    if "Sofa" in pl:
        hits += 1
        if hits <= 5:
            print("   SOFA-PAYLOAD:", pl[:8])
print("含 Sofa 的 payload: %d" % hits)
print("\n含 Sofa 的 window（Sofa 后 2 个字符串）:")
for i in idx[:5]:
    print("   ", vals[i:i+3])
