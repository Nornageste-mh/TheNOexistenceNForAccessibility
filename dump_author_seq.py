# -*- coding: utf-8 -*-
"""Full raw CharacterNames + ordered author-label sequence recovered from Script bytecode."""
import re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUT = r"D:\DSHWorkBase\noexistence_a11y\script_zh\_author_seq.txt"

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

# ---------- 1. full raw CharacterNames (copy that is zh source) ----------
f.write("=" * 78 + "\n### FULL RAW CharacterNames copy #0 ###\n" + "=" * 78 + "\n")
body0 = by_name["CharacterNames"][0]
for i, ln in enumerate(body0.splitlines()):
    f.write("%4d| %s\n" % (i, ln))

# ---------- 2. ordered length-prefixed strings in Script bytecode ----------
f.write("\n\n" + "=" * 78 + "\n### ordered length-prefixed strings per Script asset ###\n" + "=" * 78 + "\n")

# pattern: <len:u32 LE><utf8 bytes><0x00>
def seq(raw):
    out = []
    i = 0
    n = len(raw)
    while i < n - 5:
        L = int.from_bytes(raw[i:i+4], "little")
        if 1 <= L <= 40 and i + 4 + L < n and raw[i+4+L] == 0:
            chunk = raw[i+4:i+4+L]
            try:
                s = chunk.decode("utf-8")
            except UnicodeDecodeError:
                i += 1; continue
            if all(0x20 <= ord(c) < 0x7f for c in s):
                out.append((i, s))
                i += 4 + L + 1
                continue
        i += 1
    return out

for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        d = o.read(check_read=False)
        if d.m_Script.read().m_ClassName != "Script":
            continue
    except Exception:
        continue
    try:
        nm = d.m_Name
    except Exception:
        nm = "?"
    raw = o.get_raw_data()
    s = seq(raw)
    f.write("\n--- [%s] raw=%d  len-prefixed-ascii strings=%d ---\n" % (nm, len(raw), len(s)))
    # only show plausible short labels (no dots, no path chars)
    for off, t in s:
        if re.fullmatch(r"[A-Za-z_?\u4e00-\u9fff][A-Za-z0-9_?\u4e00-\u9fff]{0,23}", t):
            f.write("   %8d  %s\n" % (off, t))

f.close()
print("written", OUT)
