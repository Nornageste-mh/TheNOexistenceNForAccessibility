# -*- coding: utf-8 -*-
"""Final line<->author map with group-name fallback + per-character voicing."""
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
                out.append(s); i += 4 + L + 1; continue
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

def parse_cmds(raw):
    vals = all_strings(raw)
    anchors = [i for i in range(len(vals) - 1)
               if vals[i] == "Naninovel.Commands" and vals[i+1] == "Elringus.Naninovel.Runtime"]
    out = []
    for k, i in enumerate(anchors):
        cmd = vals[i-1] if i >= 1 else "?"
        end = anchors[k+1] - 1 if k + 1 < len(anchors) else len(vals)
        out.append((cmd, vals[i+2:end]))
    return out

def pairs_with(payload, grp):
    pr, p = [], 0
    while p < len(payload):
        if payload[p] == grp:
            pr.append(payload[p+1] if p + 1 < len(payload) else "")
            p += 2
        else:
            p += 1
    return pr

line_author = {}
order = collections.defaultdict(list)
stat = collections.Counter()
for nm, raw in scripts.items():
    dlids = {l for l, _ in doc_lines(nm)}
    for cmd, payload in parse_cmds(raw):
        if cmd != "PrintText" or not payload:
            continue
        stat["PrintText"] += 1
        cand = None
        for grp in (payload[0], max(set(payload), key=payload.count)):
            if grp in ("Naninovel.Commands", "Elringus.Naninovel.Runtime", "Naninovel"):
                continue
            pr = pairs_with(payload, grp)
            if len(pr) >= 3 and pr[1] and (not dlids or pr[1] in dlids):
                cand = (pr[1], pr[2])
                break
        if cand:
            line_author[(nm, cand[0])] = cand[1]
            order[nm].append(cand[0])
            stat["mapped"] += 1
        else:
            stat["unmapped"] += 1

print("=" * 78)
print("结构校验")
print("=" * 78)
tot_g = tot_u = 0
for nm in sorted(scripts):
    npt = sum(1 for c, _ in parse_cmds(scripts[nm]) if c == "PrintText")
    ngt = len(re.findall(rb"GenericTextScriptLine", scripts[nm]))
    print("  %-14s PrintText=%-4d GenericText=%-4d 映射=%-4d" % (nm, npt, ngt, len(order[nm])))
    tot_g += ngt; tot_u += len(order[nm])
print("\n映射覆盖 %d / %d 条通用文本行 (%.1f%%)" % (tot_u, tot_g, 100.0 * tot_u / max(1, tot_g)))
print("统计:", dict(stat))

# ---- voicing ----
clips = set()
for o in env.objects:
    if o.type.name == "AudioClip":
        try:
            clips.add(o.read().m_Name)
        except Exception:
            pass
id_clips = {c for c in clips if re.fullmatch(r"~[0-9a-f]{5,}", c)}
txt_clips = [c for c in clips if c not in id_clips]

def norm(s):
    s = re.sub(r"<[^>]+>", "", s)
    return re.sub(r"[\s\u3000]+", "", s)

def is_voiced(lid, zh):
    if lid in id_clips:
        return True
    n = norm(zh)
    if len(n) >= 4:
        for L in (14, 12, 10, 8, 6):
            if len(n) >= L and any(c.startswith(n[:L]) for c in txt_clips):
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

per = collections.defaultdict(lambda: [0, 0])
rows = []
for nm in sorted(scripts):
    dl = dict(doc_lines(nm))
    for lid in order[nm]:
        au = line_author[(nm, lid)]
        zh = dl.get(lid, "")
        v = is_voiced(lid, zh)
        a = au if au else "(旁白/无说话人)"
        per[a][0] += 1; per[a][1] += 1 if v else 0
        rows.append((nm, lid, a, zh_name.get(a, ""), "配音" if v else "TTS", zh))

with open(OUT, "w", encoding="utf-8") as f:
    f.write("台词与角色对照（Script 字节码 x 本地化文档 x 音频片段）\n")
    f.write("剧本\t行号\tAuthorId\t中文显示名\t语音\t台词\n" + "=" * 100 + "\n")
    for r in rows:
        f.write("%s\t%s\t%s\t%s\t%s\t%s\n" % r)
    f.write("\n\n=== 每个 AuthorId 的行数与配音率 ===\n")
    f.write("%-26s %-16s %6s %6s %8s %9s\n" % ("AuthorId", "中文显示名", "总行", "有配音", "需TTS", "配音率"))
    for a, (t, v) in sorted(per.items(), key=lambda x: -x[1][0]):
        f.write("%-26s %-16s %6d %6d %8d %8.1f%%\n" % (a, zh_name.get(a, ""), t, v, t - v, 100.0 * v / t))

print("\n" + "=" * 78)
print("每个 AuthorId 的行数 / 配音率")
print("=" * 78)
print("  %-26s %-16s %6s %6s %7s %9s" % ("AuthorId", "中文名", "总行", "配音", "TTS", "配音率"))
for a, (t, v) in sorted(per.items(), key=lambda x: -x[1][0]):
    print("  %-26s %-16s %6d %6d %7d %8.1f%%" % (a, zh_name.get(a, ""), t, v, t - v, 100.0 * v / t))
print("\n明细:", OUT)
