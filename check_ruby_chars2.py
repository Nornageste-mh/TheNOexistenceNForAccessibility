# -*- coding: utf-8 -*-
"""输出：全部 ruby 台词（含是否配音）+ 角色 ID 的中文显示名 + 非莉莉丝说话人排行。"""
import sys, io, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

ROOT = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me"
DATA = ROOT + r"\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

by_name = collections.defaultdict(list)
clip_names = set()
script_raws = []
for o in env.objects:
    t = o.type.name
    if t == "TextAsset":
        try:
            d = o.read(); s = getattr(d, "m_Script", b"")
            by_name[getattr(d, "m_Name", "?")].append(
                s if isinstance(s, str) else bytes(s).decode("utf-8", "replace"))
        except Exception: pass
    elif t == "AudioClip":
        try: clip_names.add(o.read().m_Name)
        except Exception: pass
    elif t == "MonoBehaviour":
        try:
            if o.read(check_read=False).m_Script.read().m_ClassName == "Script":
                script_raws.append(o.get_raw_data())
        except Exception: pass

voiced = {c for c in clip_names if re.fullmatch(r"~[0-9a-f]{5,}", c)}

# ---- 角色 ID -> 中文显示名（源文在 ';' 注释里）----
src = by_name.get("CharacterNames", [""])[0]
id2zh, pending = {}, None
for ln in src.splitlines():
    if ln.startswith(";"):
        pending = ln[1:].strip(); continue
    m = re.match(r"^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$", ln)
    if m:
        if m.group(1) not in id2zh:
            id2zh[m.group(1)] = pending or ""
        pending = None

# ---- 剧本行（去重）----
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

# ---- 1. 全部 ruby ----
ruby = [(lid, doc, t) for lid, (doc, t) in lines.items() if "<ruby" in t]
print("=" * 74)
print("1) 全部含 <ruby> 的台词：%d 行 / 去重剧本 %d 行" % (len(ruby), len(lines)))
print("=" * 74)
voiced_ruby = sum(1 for lid, _, _ in ruby if lid in voiced)
print("   其中【有配音】%d 行，无配音 %d 行\n" % (voiced_ruby, len(ruby) - voiced_ruby))
bydoc = collections.Counter(d for _, d, _ in ruby)
for doc, n in bydoc.most_common():
    print("   %-16s %d 行" % (doc, n))
print()
for lid, doc, t in sorted(ruby, key=lambda x: (x[1], x[0])):
    mark = "★配音" if lid in voiced else "  无  "
    print("   [%-13s %-12s %s] %s" % (doc, lid, mark, t[:110]))

# ---- 2. 角色中文名 + 说话量 ----
blob = b"".join(script_raws)
rows = []
for cid, zh in id2zh.items():
    n = len(re.findall(re.escape(cid).encode(), blob))
    if n: rows.append((n, cid, zh))
print()
print("=" * 74)
print("2) 说话人排行（非莉莉丝的高亮）")
print("=" * 74)
for n, cid, zh in sorted(rows, reverse=True):
    tag = "  ← 莉莉丝，全程配音，不用管" if "lilith" in cid.lower() else ""
    print("   %5d  %-24s %s%s" % (n, cid, zh or "(无名)", tag))

# ---- 3. 需 TTS 的主要说话人合计 ----
need = [(n, cid, zh) for n, cid, zh in rows if "lilith" not in cid.lower()]
print()
print("非莉莉丝说话人共 %d 个，剧本引用合计 %d 次" % (len(need), sum(n for n, _, _ in need)))
print("莉莉丝系共 %d 个 ID，合计 %d 次" %
      (len(rows) - len(need), sum(n for n, c, _ in rows if "lilith" in c.lower())))
