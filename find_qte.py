# -*- coding: utf-8 -*-
"""List every distinct real command name per script (to locate the oven QTE)."""
import io, sys, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

def all_strings(buf):
    out, n = [], len(buf)
    for i in range(n - 5):
        L = int.from_bytes(buf[i:i+4], "little")
        if not (1 <= L <= 200) or i + 4 + L > n:
            continue
        try:
            s = buf[i+4:i+4+L].decode("utf-8")
        except UnicodeDecodeError:
            continue
        if all(ord(c) >= 0x20 for c in s):
            out.append(s)
    return out

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

glob = collections.Counter()
per = {}
QTEISH = re.compile(r"qte|oven|button|score|rhythm|tap|hit|timing|press|game", re.I)

for nm, raw in scripts.items():
    vals = all_strings(raw)
    an = [i for i in range(len(vals) - 1)
          if vals[i] == "Naninovel.Commands" and vals[i+1] == "Elringus.Naninovel.Runtime"]
    c = collections.Counter()
    for i in an:
        if i < 1:
            continue
        v = vals[i-1]
        if v in FW or SCRIPTNAME.match(v):
            continue
        c[v] += 1
        glob[v] += 1
    per[nm] = c

print("=== 全剧本命令名（出现 >=2）===")
for k, n in glob.most_common():
    mark = "   <== QTE?" if QTEISH.search(k) else ""
    if n >= 2 or mark:
        print("  %5d  %s%s" % (n, k, mark))

print()
print("=== 疑似 QTE / 互动命令 出现在哪些剧本 ===")
for k in glob:
    if QTEISH.search(k):
        where = ["%s×%d" % (s, per[s][k]) for s in sorted(per) if per[s][k]]
        print("  %-24s %s" % (k, ", ".join(where)))
