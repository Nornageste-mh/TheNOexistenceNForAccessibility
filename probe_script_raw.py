# -*- coding: utf-8 -*-
"""Try to recover the raw Naninovel Script asset payload (control flow + custom commands)."""
import sys, io, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

out = open(r"D:\DSHWorkBase\noexistence_a11y\script_zh\_script_raw.txt", "w", encoding="utf-8")
def P(*a):
    out.write(" ".join(str(x) for x in a) + "\n")

shown = 0
for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        d = o.read(check_read=False)
        if d.m_Script.read().m_ClassName != "Script":
            continue
    except Exception:
        continue
    shown += 1
    if shown > 4:
        break
    try:
        raw = o.get_raw_data()
    except Exception as e:
        P("raw failed:", e)
        continue
    name = "?"
    try:
        name = d.m_Name
    except Exception:
        pass
    P("=" * 70)
    P("Script asset name=%r path_id=%s raw_len=%d" % (name, o.path_id, len(raw)))
    P("head hex:", raw[:80].hex())
    try:
        P("as utf-8 (first 1200 chars):")
        P(raw.decode("utf-8", "replace")[:1200])
    except Exception as e:
        P("utf8 fail", e)
    runs = re.findall(rb"[\x20-\x7e]{12,}", raw)
    P("ascii runs: %d" % len(runs))
    for r in runs[:25]:
        P("   ", r.decode("ascii", "replace")[:160])

out.close()
print("done, objects matched:", shown)
