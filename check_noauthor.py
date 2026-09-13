# -*- coding: utf-8 -*-
"""(a) inspect the no-author lines  (b) find what the non-dialogue localization entries are (choices?)."""
import re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUT = r"D:\DSHWorkBase\noexistence_a11y\无说话人行与选项.txt"
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

rows = [l.rstrip("\n").split("\t") for l in
        open(r"D:\DSHWorkBase\noexistence_a11y\台词与角色对照.txt", encoding="utf-8")]
rows = [r for r in rows if len(r) == 6 and r[0].startswith("Prologue")]

noauth = [r for r in rows if r[2] == "(旁白/无说话人)"]
print("无说话人行 %d 条" % len(noauth))

# classify
ital = [r for r in noauth if r[5].startswith("<i>")]
plain = [r for r in noauth if not r[5].startswith("<i>")]
dots = [r for r in plain if re.fullmatch(r"[….。！？!\s]*", r[5])]
rest = [r for r in plain if r not in dots]
print("  <i>「…」</i> 引号体旁白 : %d" % len(ital))
print("  纯省略号/符号          : %d" % len(dots))
print("  其它（需人工看）        : %d" % len(rest))

f = open(OUT, "w", encoding="utf-8")
f.write("=== 无说话人行 · 非引号体（%d 条，重点看这些是不是主角台词）===\n" % len(rest))
for r in rest:
    f.write("[%s %s] %s\n" % (r[0], r[1], r[5]))

f.write("\n\n=== 无说话人行 · <i>引号体（抽 30）===\n")
for r in ital[:30]:
    f.write("[%s %s] %s\n" % (r[0], r[1], r[5]))

# ---- orphan localization entries: doc line ids never used by a PrintText ----
used = collections.defaultdict(set)
for r in rows:
    used[r[0]].add(r[1])

f.write("\n\n=== 本地化文档里有、但不是对话行的条目（疑似选项/参数）===\n")
tot = 0
for nm in sorted(used):
    dl = doc_lines(nm)
    if not dl:
        continue
    orphan = [(l, t) for l, t in dl if l not in used[nm]]
    if not orphan:
        continue
    tot += len(orphan)
    f.write("\n--- [%s] %d 条 ---\n" % (nm, len(orphan)))
    for l, t in orphan:
        f.write("    %-12s %s\n" % (l, t[:90]))
f.write("\n合计非对话条目: %d\n" % tot)
f.close()
print("非对话本地化条目: %d" % tot)
print("写入", OUT)
