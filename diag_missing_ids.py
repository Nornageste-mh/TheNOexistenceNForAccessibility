# -*- coding: utf-8 -*-
"""Do the remaining IDs ever occupy an author slot?"""
import re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

def all_strings(buf):
    out, n = [], len(buf)
    for i in range(n - 5):
        L = int.from_bytes(buf[i:i+4], "little")
        if not (1 <= L <= 80) or i + 4 + L > n:
            continue
        try:
            s = buf[i+4:i+4+L].decode("utf-8")
        except UnicodeDecodeError:
            continue
        if all(ord(c) >= 0x20 for c in s):
            out.append((i, s))
    return out

TARGETS = ["Sofa", "Table", "Nurse", "CG_3", "CG_4", "CG_6", "CG_7", "CG_8",
           "QLilith", "Ferriswheel", "Sartre_1", "Microwave", "Character"]
FW = {"Naninovel.Commands", "Elringus.Naninovel.Runtime", "Naninovel", "true", "false"}
SCRIPTNAME = re.compile(r"^(Prologue|ED$|FakeED|Title|StartGame|ExitGame|VocalConcert|Test$|Script$|Chars$)")

scripts = {}
for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        d = o.read(check_read=False)
        if d.m_Script.read().m_ClassName == "Script":
            scripts[d.m_Name] = o.get_raw_data()
    except Exception:
        pass

total = collections.Counter()
inpayload = collections.Counter()
preceded_by_pt = collections.Counter()

for nm, raw in scripts.items():
    S = all_strings(raw)
    vals = [s for _, s in S]
    an = [i for i in range(len(vals) - 1)
          if vals[i] == "Naninovel.Commands" and vals[i+1] == "Elringus.Naninovel.Runtime"]
    real = [i for i in an if i >= 1 and vals[i-1] not in FW and not SCRIPTNAME.match(vals[i-1])]
    for i, v in enumerate(vals):
        if v in TARGETS:
            total[v] += 1
    # payload windows of PrintText
    for k, i in enumerate(real):
        if vals[i-1] != "PrintText":
            continue
        end = real[k+1] - 1 if k + 1 < len(real) else len(vals)
        pl = vals[i+2:end]
        for t in TARGETS:
            if t in pl:
                inpayload[t] += 1
                # where in the payload?
                j = pl.index(t)
                preceded_by_pt[t] += 1
                if preceded_by_pt[t] <= 3:
                    print("  [%s] payload[%d]=%s  full=%s" % (nm, j, t, pl[:10]))

print("\n%-14s %8s %10s" % ("ID", "字符串出现", "对话payload内"))
for t in TARGETS:
    print("%-14s %8d %10d" % (t, total[t], inpayload[t]))
