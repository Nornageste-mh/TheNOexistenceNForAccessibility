# -*- coding: utf-8 -*-
"""Decode all Naninovel Script assets; harvest custom command/type names referenced from Assembly-CSharp."""
import sys, io, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)
out = open(r"D:\DSHWorkBase\noexistence_a11y\script_zh\_script_decode.txt", "w", encoding="utf-8")
def P(*a):
    out.write(" ".join(str(x) for x in a) + "\n")

TYPE_RE = re.compile(rb"[A-Za-z_][A-Za-z0-9_.+`]{3,60}")
custom = collections.Counter()
readable = []
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
        raw = o.get_raw_data()
    except Exception:
        continue
    name = "?"
    try:
        name = d.m_Name
    except Exception:
        pass
    dec = raw.decode("utf-8", "replace")
    bad = dec.count("\ufffd")
    ratio = bad / max(1, len(dec))
    P("=" * 68)
    P("Script %-16s raw=%-8d bad_utf8=%-7d ratio=%.3f" % (name, len(raw), bad, ratio))
    if ratio < 0.02:
        readable.append(name)
        P("  --- readable head ---")
        P("  " + dec[:700].replace("\n", " "))
    # harvest type names that are NOT Naninovel/Unity framework types
    for m in set(TYPE_RE.findall(raw)):
        t = m.decode("ascii", "replace")
        if any(t.startswith(p) for p in ("Elringus", "Naninovel", "Unity", "System", "TMPro",
                                         "DOTween", "Sirenix", "UnityEngine", "UnityEditor")):
            continue
        if t.startswith(("LabelScriptLine", "CommandScriptLine", "GenericTextScriptLine")):
            continue
        custom[t] += 1

P("")
P("=" * 68)
P("READABLE script assets (%d/%d): %s" % (len(readable), 20, ", ".join(readable)))
P("=" * 68)
P("")
P("Custom (Assembly-CSharp / game-side) identifiers referenced inside Script assets:")
P("(these are the game's own commands & UI classes wired into the script flow)")
for t, n in custom.most_common(80):
    P("   %5d  %s" % (n, t))
out.close()
print("done; readable:", len(readable))
