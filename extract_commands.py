# -*- coding: utf-8 -*-
"""Recover Naninovel script commands from the localization source-comment lines, index custom/QTE ones."""
import sys, io, re, collections, os
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUT = r"D:\DSHWorkBase\noexistence_a11y\script_zh\_commands.txt"
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

cmd_count = collections.Counter()
cmd_lines = collections.defaultdict(list)
for name in sorted(by_name):
    body = by_name[name][0]
    cur = None
    for ln in body.splitlines():
        m = re.match(r"^#\s*(\S+)", ln)
        if m:
            cur = m.group(1); continue
        if not ln.startswith(";"):
            continue
        src = ln[1:]
        for cm in re.finditer(r"@([A-Za-z_][A-Za-z0-9_]*)", src):
            c = cm.group(1)
            cmd_count[c] += 1
            if len(cmd_lines[c]) < 12:
                cmd_lines[c].append((name, cur, src.strip()[:190]))

with open(OUT, "w", encoding="utf-8") as f:
    f.write("=== distinct @commands referenced in script source lines (count) ===\n")
    for c, n in cmd_count.most_common():
        f.write("%6d  @%s\n" % (n, c))
    f.write("\n\n=== sample lines per command ===\n")
    for c, _ in cmd_count.most_common():
        f.write("\n--- @%s ---\n" % c)
        for name, cid, s in cmd_lines[c]:
            f.write("  [%s %s] %s\n" % (name, cid, s))

print("distinct commands:", len(cmd_count))
for c, n in cmd_count.most_common(60):
    print("%6d  @%s" % (n, c))
print("\nwritten:", OUT)
