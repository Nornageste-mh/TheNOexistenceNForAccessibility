# -*- coding: utf-8 -*-
"""Extract every author label (generic-text speaker) from the compiled Naninovel Script assets."""
import sys, io, re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUT = r"D:\DSHWorkBase\noexistence_a11y\script_zh\_authors_raw.txt"

env = UnityPy.load(DATA)

# ---- 1. registered characters, per locale copy of the CharacterNames doc ----
name_copies = collections.defaultdict(list)
for o in env.objects:
    if o.type.name != "TextAsset":
        continue
    try:
        d = o.read()
        s = getattr(d, "m_Script", b"")
        name_copies[getattr(d, "m_Name", "?")].append(
            s if isinstance(s, str) else bytes(s).decode("utf-8", "replace"))
    except Exception:
        pass

f = open(OUT, "w", encoding="utf-8")
f.write("### how many locale copies exist per doc name (top 20) ###\n")
for n, c in sorted(name_copies.items(), key=lambda x: -len(x[1]))[:20]:
    f.write("   %-34s copies=%d\n" % (n, len(c)))
f.write("   CharacterNames copies=%d\n\n" % len(name_copies.get("CharacterNames", [])))

f.write("=" * 78 + "\n### CharacterNames: keys per locale copy ###\n" + "=" * 78 + "\n")
allkeys = {}
for i, body in enumerate(name_copies.get("CharacterNames", [])):
    hdr = body.splitlines()[0][:110] if body.splitlines() else ""
    keys, zh, prev = {}, {}, None
    for ln in body.splitlines():
        s = ln.strip()
        if not s:
            prev = None; continue
        if s.startswith(";"):
            prev = None if "localization document" in s else s[1:].strip(); continue
        m = re.match(r"^([A-Za-z_][A-Za-z0-9_.]*)\s*:\s*(.*)$", s)
        if m:
            keys.setdefault(m.group(1), m.group(2))
            zh.setdefault(m.group(1), prev or "")
            prev = None
        else:
            prev = None
    f.write("\n--- copy #%d  (%d keys)  %s\n" % (i, len(keys), hdr))
    for k, v in keys.items():
        f.write("      %-24s zh=%-14s tr=%s\n" % (k, zh.get(k, ""), v))
        allkeys.setdefault(k, zh.get(k, ""))
f.write("\nTOTAL distinct character IDs across all locales: %d\n" % len(allkeys))

# ---- 2. author labels inside the compiled Script assets ----
f.write("\n" + "=" * 78 + "\n### author-label candidates inside compiled Script assets ###\n" + "=" * 78 + "\n")

IDENT = re.compile(rb"[A-Za-z_][A-Za-z0-9_]{1,28}")
KNOWN = set(allkeys)
cand = collections.Counter()
per_script = collections.defaultdict(collections.Counter)
qmark_hits = []
scripts = 0

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
    try:
        nm = d.m_Name
    except Exception:
        nm = "?"
    scripts += 1
    if b"???" in raw:
        for m in re.finditer(re.escape(b"???"), raw):
            qmark_hits.append((nm, raw[max(0, m.start()-40):m.start()+40]))
    for m in IDENT.findall(raw):
        t = m.decode("ascii")
        if t in KNOWN:
            cand[t] += 1
            per_script[nm][t] += 1

f.write("Script assets scanned: %d\n\n" % scripts)
f.write("### raw '???' byte hits: %d ###\n" % len(qmark_hits))
for nm, ctx in qmark_hits[:40]:
    f.write("   [%s] %r\n" % (nm, ctx))
f.write("\n### registered-character IDs found in script bytecode ###\n")
for t, n in cand.most_common():
    f.write("   %6d  %-24s zh=%s\n" % (n, t, allkeys.get(t, "")))
f.write("\n### which script uses which author ###\n")
for nm in sorted(per_script):
    f.write("\n  [%s]\n" % nm)
    for t, n in per_script[nm].most_common():
        f.write("      %5d  %-22s %s\n" % (n, t, allkeys.get(t, "")))

f.close()
print("written", OUT, "| scripts:", scripts, "| char ids:", len(allkeys), "| ??? hits:", len(qmark_hits))
