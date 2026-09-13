# -*- coding: utf-8 -*-
"""严格配对提取 角色ID -> 中文显示名（';' 注释必须紧邻键行），并统计各方言量。"""
import sys, io, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

ROOT = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me"
env = UnityPy.load(ROOT + r"\TheNOexistenceNofyouANDme_Data\data.unity3d")

by_name = collections.defaultdict(list)
script_raws = []
for o in env.objects:
    t = o.type.name
    if t == "TextAsset":
        try:
            d = o.read(); s = getattr(d, "m_Script", b"")
            by_name[getattr(d, "m_Name", "?")].append(
                s if isinstance(s, str) else bytes(s).decode("utf-8", "replace"))
        except Exception: pass
    elif t == "MonoBehaviour":
        try:
            if o.read(check_read=False).m_Script.read().m_ClassName == "Script":
                script_raws.append(o.get_raw_data())
        except Exception: pass

def strict_map(doc_name):
    """';' 源文行必须紧邻其后的 'key: value' 行，中间不能有空行/其他行。"""
    src = by_name.get(doc_name, [""])[0]
    out, prev = {}, None
    for ln in src.splitlines():
        s = ln.strip()
        if not s:
            prev = None; continue
        if s.startswith(";"):
            prev = None if s.startswith("; ") and "localization document" in s else s[1:].strip()
            continue
        m = re.match(r"^([A-Za-z_][A-Za-z0-9_.]*)\s*:\s*(.*)$", s)
        if m:
            if m.group(1) not in out:
                out[m.group(1)] = prev or ""
            prev = None
        else:
            prev = None
    return out

id2zh = strict_map("CharacterNames")
blob = b"".join(script_raws)

rows = []
for cid, zh in id2zh.items():
    n = len(re.findall(re.escape(cid).encode(), blob))
    if n: rows.append((n, cid, zh))

LIL = re.compile(r"lilith|cg_\d|nurse|princess", re.I)
print("=" * 78)
print("角色 ID -> 中文显示名 -> 剧本引用次数")
print("=" * 78)
print("\n【莉莉丝系 —— 全程配音，无需 TTS】")
for n, cid, zh in sorted(rows, reverse=True):
    if LIL.search(cid):
        print("   %5d  %-26s %s" % (n, cid, zh or "(无名)"))
print("\n【其它角色 —— 需要 TTS，且需要播报说话人】")
for n, cid, zh in sorted(rows, reverse=True):
    if not LIL.search(cid):
        print("   %5d  %-26s %s" % (n, cid, zh or "(无名)"))

need = [r for r in rows if not LIL.search(r[1])]
print()
print("非莉莉丝：%d 个角色，剧本引用 %d 次" % (len(need), sum(r[0] for r in need)))
print("莉莉丝系：%d 个角色，剧本引用 %d 次" % (len(rows)-len(need), sum(r[0] for r in rows if LIL.search(r[1]))))

print()
print("=" * 78)
print("ruby 的语义分类（看注解到底装什么）")
print("=" * 78)
lines = {}
for name, copies in by_name.items():
    body = copies[0]
    if body.lstrip().startswith(("\ufeff#,", "#,")):
        for row in body.splitlines()[1:]:
            p = row.split(",")
            if len(p) >= 4 and p[3].strip():
                lines.setdefault(p[0].strip(), (name, p[3].strip()))
        continue
    cur = None
    for ln in body.splitlines():
        m = re.match(r"^#\s*(\S+)", ln)
        if m: cur = m.group(1); continue
        if ln.startswith(";") and cur:
            lines.setdefault(cur, (name, ln[1:].strip()))

for lid, (doc, t) in sorted(lines.items(), key=lambda x: x[1][0]):
    for m in re.finditer(r'<ruby="([^"]*)">([^<]*)</ruby>', t):
        ann, base = m.group(1), m.group(2)
        print("   显示【%s】  旁注【%s】   <- %s / %s" % (base, ann, doc, lid))
