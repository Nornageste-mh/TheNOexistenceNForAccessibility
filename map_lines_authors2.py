# -*- coding: utf-8 -*-
"""Definitive line<->author map: parse every command payload, read the (group,key) triples."""
import re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUT = r"D:\DSHWorkBase\noexistence_a11y\台词与角色对照.txt"
env = UnityPy.load(DATA)

def all_strings(buf):
    out, i, n = [], 0, len(buf)
    while i < n - 5:
        L = int.from_bytes(buf[i:i+4], "little")
        if 1 <= L <= 80 and i + 4 + L < n and buf[i+4+L] == 0:
            try:
                s = buf[i+4:i+4+L].decode("utf-8")
            except UnicodeDecodeError:
                i += 1; continue
            if all(ord(c) >= 0x20 for c in s):
                out.append((i, s)); i += 4 + L + 1; continue
        i += 1
    return out

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

def parse_script(raw):
    S = all_strings(raw)
    vals = [s for _, s in S]
    anchors = [i for i in range(len(vals) - 1)
               if vals[i] == "Naninovel.Commands" and vals[i+1] == "Elringus.Naninovel.Runtime"]
    recs = []
    for k, i in enumerate(anchors):
        cmd = vals[i-1] if i >= 1 else "?"
        end = anchors[k+1] - 1 if k + 1 < len(anchors) else len(vals)
        payload = vals[i+2:end]
        recs.append((cmd, payload))
    return recs

def triples(payload):
    """payload starts with group name; read (group,key) pairs."""
    if not payload:
        return []
    grp = max(set(payload), key=payload.count)
    pairs, p = [], 0
    while p < len(payload):
        if payload[p] == grp:
            pairs.append(payload[p+1] if p + 1 < len(payload) else "")
            p += 2
        else:
            p += 1
    return pairs

# ---------- build map ----------
line_author = {}     # (script, lineId) -> author
order = collections.defaultdict(list)
stats = collections.Counter()
for nm, raw in scripts.items():
    for cmd, payload in parse_script(raw):
        if cmd != "PrintText":
            continue
        tr = triples(payload)
        stats["PrintText"] += 1
        if len(tr) < 3:
            stats["skip_short"] += 1
            continue
        lid, au = tr[1], tr[2]
        if not lid:
            stats["no_lineid"] += 1
            continue
        line_author[(nm, lid)] = au
        order[nm].append(lid)

# ---------- validate against docs ----------
print("=" * 76)
print("结构校验（PrintText payload 数 vs GenericTextScriptLine vs 文档行数）")
print("=" * 76)
allok = True
for nm in sorted(scripts):
    recs = parse_script(scripts[nm])
    npt = sum(1 for c, _ in recs if c == "PrintText")
    ngt = len(re.findall(rb"GenericTextScriptLine", scripts[nm]))
    dl = doc_lines(nm)
    extracted = order[nm]
    dlids = [l for l, _ in dl]
    # every extracted id must exist in doc, and order must be increasing
    pos = {l: i for i, l in enumerate(dlids)}
    okorder = all(l in pos for l in extracted) and \
              all(pos[extracted[i]] < pos[extracted[i+1]] for i in range(len(extracted)-1))
    flag = "OK" if (npt == ngt and okorder) else "CHECK"
    if npt != ngt or not okorder:
        allok = False
    print("  %-14s PrintText=%-4d GenericText=%-4d docLines=%-4d used=%-4d  %s"
          % (nm, npt, ngt, len(dl), len(extracted), flag))
print("\n总体:", "全部一致" if allok else "有偏差（见上）")
print("统计:", dict(stats))

# ---------- voicing ----------
clips = set()
for o in env.objects:
    if o.type.name == "AudioClip":
        try:
            clips.add(o.read().m_Name)
        except Exception:
            pass
id_clips = {c for c in clips if re.fullmatch(r"~[0-9a-f]{5,}", c)}
clip_texts = [c for c in clips if c not in id_clips]

def norm(s):
    s = re.sub(r"<[^>]+>", "", s)
    return re.sub(r"[\s\u3000]+", "", s)

def is_voiced(lid, zh):
    if lid in id_clips:
        return True
    n = norm(zh)
    if len(n) >= 4:
        for L in (14, 12, 10, 8, 6):
            if len(n) >= L and any(c.startswith(n[:L]) for c in clip_texts):
                return True
    return False

zh_name = {}
for body in by_name.get("CharacterNames", []):
    if body.lstrip().startswith(("\ufeff#,", "#,")) or body.count(";") > 20:
        continue
    for ln in body.splitlines():
        m = re.match(r"^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$", ln)
        if m:
            zh_name.setdefault(m.group(1), m.group(2).strip())
    if zh_name:
        break

per_author = collections.defaultdict(lambda: [0, 0])
rows = []
for nm in sorted(scripts):
    dl = dict(doc_lines(nm))
    for lid in order[nm]:
        au = line_author[(nm, lid)]
        zh = dl.get(lid, "")
        v = is_voiced(lid, zh)
        a = au if au else "(旁白/无说话人)"
        per_author[a][0] += 1
        per_author[a][1] += 1 if v else 0
        rows.append((nm, lid, a, zh_name.get(a, "(未登记)") if a != "(旁白/无说话人)" else "", "配音" if v else "TTS", zh))

with open(OUT, "w", encoding="utf-8") as f:
    f.write("台词与角色对照（Script 字节码 x 本地化文档）\n")
    f.write("剧本\t行号\tAuthorId\t中文显示名\t语音\t台词\n" + "=" * 100 + "\n")
    for r in rows:
        f.write("%s\t%s\t%s\t%s\t%s\t%s\n" % r)
    f.write("\n\n=== 每个 AuthorId 的行数与配音率 ===\n")
    f.write("%-26s %-14s %6s %6s %8s\n" % ("AuthorId", "中文显示名", "总行", "有配音", "需TTS"))
    for a, (t, v) in sorted(per_author.items(), key=lambda x: -x[1][0]):
        f.write("%-26s %-14s %6d %6d %8d\n" % (a, zh_name.get(a, ""), t, v, t - v))

print("\n" + "=" * 76)
print("每个 AuthorId 的行数 / 配音率")
print("=" * 76)
print("  %-26s %-14s %6s %6s %7s %8s" % ("AuthorId", "中文名", "总行", "配音", "TTS", "配音率"))
for a, (t, v) in sorted(per_author.items(), key=lambda x: -x[1][0]):
    print("  %-26s %-14s %6d %6d %7d %7.1f%%"
          % (a, zh_name.get(a, ""), t, v, t - v, 100.0 * v / t))
print("\n明细写入:", OUT)
