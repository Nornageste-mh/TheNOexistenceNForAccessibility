# -*- coding: utf-8 -*-
"""Gold-standard line<->author map from Script bytecode + per-character voicing."""
import re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUT = r"D:\DSHWorkBase\noexistence_a11y\台词与角色对照.txt"
env = UnityPy.load(DATA)

# ---------- strings helper ----------
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
                out.append((i, s)); i += 4 + L + 1; continue
        i += 1
    return out

# ---------- scripts ----------
scripts = {}
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
        scripts[d.m_Name] = o.get_raw_data()
    except Exception:
        pass

blocks = {}
for nm, raw in scripts.items():
    offs = [m.start() for m in re.finditer(rb"PrintText\x00", raw)]
    recs = []
    for k, a in enumerate(offs):
        b = offs[k+1] if k + 1 < len(offs) else len(raw)
        ss = [s for _, s in lp_strings(raw, a, b)]
        if len(ss) < 5:
            recs.append((None, None, ss)); continue
        sn = ss[3]
        lid = ss[4]
        au = ss[8] if len(ss) > 8 else ""
        recs.append((sn, lid, au))
    blocks[nm] = (recs, len(re.findall(rb"GenericTextScriptLine", raw)))

# ---------- docs ----------
by_name = collections.defaultdict(list)
for o in env.objects:
    if o.type.name != "TextAsset":
        continue
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

# ---------- validation ----------
print("=" * 76)
print("校验：PrintText 块数 vs GenericTextScriptLine 数 vs 本地化行数")
print("=" * 76)
ok = True
for nm in sorted(blocks):
    recs, ngt = blocks[nm]
    dl = doc_lines(nm)
    lids = [r[1] for r in recs]
    good = (len(recs) == ngt) and ([l for l in lids] == [l for l, _ in dl])
    if not good and (ngt or dl):
        ok = False
    print("  %-14s PrintText=%-4d GenericText=%-4d docLines=%-4d  %s"
          % (nm, len(recs), ngt, len(dl), "OK" if good else "MISMATCH"))
print("\n全脚本结构校验:", "全部通过" if ok else "存在不一致")

# ---------- voicing ----------
clips = set()
for o in env.objects:
    if o.type.name == "AudioClip":
        try:
            clips.add(o.read().m_Name)
        except Exception:
            pass
id_clips = {c for c in clips if re.fullmatch(r"~[0-9a-f]{5,}", c)}
print("\nAudioClip 总数 %d | 按行号命名 %d | 其它命名 %d"
      % (len(clips), len(id_clips), len(clips) - len(id_clips)))

def norm(s):
    s = re.sub(r"<[^>]+>", "", s)
    return re.sub(r"[\s\u3000]+", "", s)

def is_voiced(lid, zh):
    if lid in id_clips:
        return True
    n = norm(zh)
    if len(n) >= 4:
        for L in (14, 12, 10, 8, 6):
            if len(n) >= L and any(c.startswith(n[:L]) for c in clips):
                return True
    return False

# ---------- character names ----------
zh_name = {}
for body in by_name.get("CharacterNames", []):
    if body.lstrip().startswith(("\ufeff#,", "#,")):
        continue
    if body.count(";") > 20:      # localization copy, skip
        continue
    for ln in body.splitlines():
        m = re.match(r"^([A-Za-z_][A-Za-z0-9_]*)\s*:\s*(.*)$", ln)
        if m:
            zh_name.setdefault(m.group(1), m.group(2).strip())
    if zh_name:
        break

# ---------- aggregate ----------
per_author = collections.defaultdict(lambda: [0, 0])          # author -> [total, voiced]
per_author_zh = collections.defaultdict(lambda: [0, 0])
rows_out = []
for nm in sorted(blocks):
    recs, _ = blocks[nm]
    dl = dict(doc_lines(nm))
    for sn, lid, au in recs:
        zh = dl.get(lid, "")
        v = is_voiced(lid, zh)
        a = au or "(旁白/无说话人)"
        per_author[a][0] += 1
        per_author[a][1] += 1 if v else 0
        key = (a, zh_name.get(a, ""))
        per_author_zh[key][0] += 1
        per_author_zh[key][1] += 1 if v else 0
        rows_out.append((nm, lid, a, zh_name.get(a, "(未登记标签)" if a not in zh_name else ""), "配音" if v else "TTS", zh))

f = open(OUT, "w", encoding="utf-8")
f.write("台词与角色对照（由 Script 字节码 + 本地化文档 交叉生成）\n")
f.write("格式： 剧本\t行号\tAuthorId\t中文显示名\t处理\t台词\n")
f.write("=" * 100 + "\n")
for r in rows_out:
    f.write("%s\t%s\t%s\t%s\t%s\t%s\n" % r)

f.write("\n\n=== 每个 AuthorId 的总行数与配音率 ===\n")
f.write("%-26s %-12s %6s %6s %8s\n" % ("AuthorId", "中文显示名", "总行", "有配音", "需TTS"))
for a, (t, v) in sorted(per_author.items(), key=lambda x: -x[1][0]):
    f.write("%-26s %-12s %6d %6d %8d\n" % (a, zh_name.get(a, ""), t, v, t - v))
f.close()

print("\n" + "=" * 76)
print("每个 AuthorId 的行数 / 配音率")
print("=" * 76)
print("  %-26s %-12s %6s %6s %7s  %s" % ("AuthorId", "中文名", "总行", "配音", "TTS", "配音率"))
for a, (t, v) in sorted(per_author.items(), key=lambda x: -x[1][0]):
    print("  %-26s %-12s %6d %6d %7d  %5.1f%%"
          % (a, zh_name.get(a, ""), t, v, t - v, 100.0 * v / t))
print("\n明细写入:", OUT)
