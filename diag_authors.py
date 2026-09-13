# -*- coding: utf-8 -*-
"""Fixed parser: locate (group,key)(group,key)(group,author) triples inside each PrintText block."""
import re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

def lp_strings(buf, lo, hi):
    out, i = [], lo
    while i < hi - 5:
        L = int.from_bytes(buf[i:i+4], "little")
        if 1 <= L <= 60 and i + 4 + L < len(buf) and buf[i+4+L] == 0:
            try:
                s = buf[i+4:i+4+L].decode("utf-8")
            except UnicodeDecodeError:
                i += 1; continue
            if all(ord(c) >= 0x20 for c in s):
                out.append(s); i += 4 + L + 1; continue
        i += 1
    return out

FW = {"Naninovel.Commands", "Elringus.Naninovel.Runtime", "Naninovel"}

scripts = {}
for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        d = o.read(check_read=False)
        if d.m_Script.read().m_ClassName != "Script":
            continue
        scripts[d.m_Name] = o.get_raw_data()
    except Exception:
        pass

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

def doc_lines(name):
    for body in by_name.get(name, []):
        if body.lstrip().startswith(("\ufeff#,", "#,")):
            continue
        out, cur = [], None
        for ln in body.splitlines():
            m = re.match(r"^#\s*(\S+)", ln)
            if m:
                cur = m.group(1); continue
            if ln.startswith(";") and cur:
                out.append((cur, ln[1:].strip())); cur = None
        if out:
            return out
    return []

for nm in ["Prologue0_0", "Prologue0_1"]:
    raw = scripts[nm]
    offs = [m.start() for m in re.finditer(rb"PrintText\x00", raw)]
    print("=" * 78)
    print("%s  PrintText=%d  docLines=%d" % (nm, len(offs), len(doc_lines(nm))))
    print("=" * 78)
    hits = []
    for k, a in enumerate(offs):
        b = offs[k+1] if k + 1 < len(offs) else len(raw)
        ss = lp_strings(raw, a, b)
        found = None
        for j in range(len(ss) - 4):
            if ss[j] in FW or ss[j+1] in FW:
                continue
            if ss[j] == ss[j+2] == ss[j+4] and ss[j+1] != ss[j]:
                found = (ss[j+1], ss[j+5] if len(ss) > j + 5 else "")
                break
        hits.append((found, ss))
    print("  找到三元组 %d / %d" % (sum(1 for h, _ in hits if h), len(hits)))
    for i, (f, ss) in enumerate(hits[:14]):
        print("   [%2d] %s   ss=%s" % (i, f, ss[:11]))
    print("   ...")
    dl = [l for l, _ in doc_lines(nm)]
    got = [f[0] for f, _ in hits if f]
    print("  提取 lineId 数 %d | 文档 lineId 数 %d" % (len(got), len(dl)))
    print("  提取前 20: %s" % got[:20])
    print("  文档前 20: %s" % dl[:20])
    print("  文档有而提取无: %s" % [x for x in dl if x not in set(got)][:20])
    print("  提取有而文档无: %s" % [x for x in got if x not in set(dl)][:20])
    print()
