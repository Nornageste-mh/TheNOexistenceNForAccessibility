# -*- coding: utf-8 -*-
"""Inspect the raw PrintText command block layout in a Script asset."""
import re
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUT = r"D:\DSHWorkBase\noexistence_a11y\script_zh\_printtext_layout.txt"
env = UnityPy.load(DATA)

target = None
for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        d = o.read(check_read=False)
        if d.m_Script.read().m_ClassName != "Script":
            continue
        if d.m_Name == "Prologue0_0":
            target = o.get_raw_data()
            break
    except Exception:
        pass

raw = target
f = open(OUT, "w", encoding="utf-8")
f.write("Prologue0_0 raw=%d\n" % len(raw))

# region of the first two PrintText commands (offset 3900..5000)
lo, hi = 3900, 5000
f.write("\n### strings in [%d,%d) ###\n" % (lo, hi))
i = lo
while i < hi - 5:
    L = int.from_bytes(raw[i:i+4], "little")
    if 1 <= L <= 60 and i + 4 + L < len(raw) and raw[i+4+L] == 0:
        try:
            s = raw[i+4:i+4+L].decode("utf-8")
        except UnicodeDecodeError:
            i += 1; continue
        if all(ord(c) >= 0x20 for c in s):
            f.write("  %6d L=%-3d %r\n" % (i, L, s))
            i += 4 + L + 1
            continue
    i += 1

f.write("\n### all PrintText offsets ###\n")
for m in re.finditer(rb"PrintText\x00", raw):
    f.write("  %d\n" % m.start())

# how many GenericTextScriptLine in the line array
f.write("\n### GenericTextScriptLine count: %d ###\n" % len(re.findall(rb"GenericTextScriptLine", raw)))
f.write("### CommandScriptLine  count: %d ###\n" % len(re.findall(rb"CommandScriptLine", raw)))
f.write("### LabelScriptLine   count: %d ###\n" % len(re.findall(rb"LabelScriptLine", raw)))

# search for line ids like 1a 2b appearing as len-prefixed strings
f.write("\n### every L<=4 short string anywhere (candidate line ids) ###\n")
for m in re.finditer(rb"[\x01-\x04]\x00\x00\x00([\x20-\x7e]{1,4})\x00", raw):
    f.write("  %6d  %r\n" % (m.start(), m.group(1).decode("ascii", "replace")))
f.close()
print("written", OUT, "| raw len", len(raw))
