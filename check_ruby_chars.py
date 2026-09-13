# -*- coding: utf-8 -*-
"""核对玩家的两点提醒：ruby 是否真在用、以及非莉莉丝角色有哪些。"""
import sys, io, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

ROOT = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me"
DATA = ROOT + r"\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

# ---- 收集 TextAsset ----
by_name = collections.defaultdict(list)
clip_names = set()
script_raws = []
for o in env.objects:
    if o.type.name == "TextAsset":
        try:
            d = o.read()
            s = getattr(d, "m_Script", b"")
            by_name[getattr(d, "m_Name", "?")].append(
                s if isinstance(s, str) else bytes(s).decode("utf-8", "replace"))
        except Exception:
            pass
    elif o.type.name == "AudioClip":
        try:
            clip_names.add(o.read().m_Name)
        except Exception:
            pass
    elif o.type.name == "MonoBehaviour":
        try:
            dd = o.read(check_read=False)
            if dd.m_Script.read().m_ClassName == "Script":
                script_raws.append(o.get_raw_data())
        except Exception:
            pass

voiced_ids = {c for c in clip_names if re.fullmatch(r"~[0-9a-f]{5,}", c)}
print("音频中以行号命名的片段:", len(voiced_ids))

# ---- 1. 角色 ID 与显示名 ----
char_map = {}
cn_doc = by_name.get("CharacterNames", [""])[0]
for m in re.finditer(r"^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$", cn_doc, re.M):
    cid, disp = m.group(1), m.group(2).strip()
    if cid not in char_map:
        char_map[cid] = disp
print("\n=== CharacterNames 里的角色 ID -> 显示名（%d 个）===" % len(char_map))
for k, v in char_map.items():
    print("   %-18s %s" % (k, v if v else "(空)"))

# ---- 2. 谁在剧本里说话（从编译后 Script 资源的原始字节里抽 ID）----
raw_blob = b"".join(script_raws)
print("\n=== 各角色 ID 在编译后剧本里的出现次数 ===")
hits = []
for cid in char_map:
    n = len(re.findall(re.escape(cid).encode(), raw_blob))
    if n:
        hits.append((n, cid, char_map[cid]))
for n, cid, disp in sorted(hits, reverse=True):
    print("   %5d  %-18s %s" % (n, cid, disp))

# ---- 3. ruby 出现情况 ----
lines = {}   # lineId -> (doc, text)
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
        if m:
            cur = m.group(1); continue
        if ln.startswith(";") and cur:
            lines.setdefault(cur, (name, ln[1:].strip()))

ruby = [(lid, doc, t) for lid, (doc, t) in lines.items() if "<ruby" in t]
print("\n=== 含 <ruby> 的台词：共 %d 行（去重后剧本共 %d 行）===" % (len(ruby), len(lines)))
for lid, doc, t in ruby:
    v = "★有配音" if lid in voiced_ids else "  无配音"
    print("   [%-14s %-12s] %s" % (doc, lid, v))
    print("        %s" % t[:130])
