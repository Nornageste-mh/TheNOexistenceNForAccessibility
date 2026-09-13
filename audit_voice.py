# -*- coding: utf-8 -*-
"""Offline voice-coverage audit: match every script line against the game's audio clips."""
import sys, io, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

# ---- 1. audio clip names ----
clip_names = []
for o in env.objects:
    if o.type.name == "AudioClip":
        try:
            clip_names.append(o.read().m_Name)
        except Exception:
            pass
clips = set(clip_names)
id_clips = {c for c in clips if re.fullmatch(r"~[0-9a-f]{5,}", c)}
print("音频片段总数:", len(clip_names), " 去重:", len(clips), " 行号命名:", len(id_clips))

# ---- 2. script lines (zh source, dedup by line id) ----
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

lines = {}   # line_id -> (doc, zh)
for name, copies in by_name.items():
    body = copies[0]
    cur = None
    for ln in body.splitlines():
        m = re.match(r"^#\s*(\S+)", ln)
        if m:
            cur = m.group(1); continue
        if ln.startswith(";") and cur:
            t = ln[1:].strip()
            if cur not in lines and t:
                lines[cur] = (name, t)

print("剧本去重后行数:", len(lines))

# ---- 3. match ----
def norm(s):
    s = re.sub(r"<[^>]+>", "", s)          # strip rich text tags
    s = re.sub(r"[\s\u3000]+", "", s)      # strip whitespace
    return s

by_id, by_text = 0, 0
unmatched = []
matched = collections.Counter()
for lid, (doc, zh) in lines.items():
    if lid in id_clips:
        by_id += 1; matched["by_line_id"] += 1
        continue
    n = norm(zh)
    hit = False
    if len(n) >= 4:
        for L in (14, 12, 10, 8, 6):
            if len(n) >= L and any(c.startswith(n[:L]) for c in clips):
                hit = True; break
    if hit:
        by_text += 1; matched["by_text_prefix"] += 1
    else:
        unmatched.append((lid, doc, zh))

total = len(lines)
print()
print("=== 配音覆盖（离线匹配） ===")
print("  按行号精确命中 : %5d  (%.1f%%)" % (by_id, 100.0 * by_id / total))
print("  按台词文本命中 : %5d  (%.1f%%)" % (by_text, 100.0 * by_text / total))
print("  未命中（需TTS） : %5d  (%.1f%%)" % (len(unmatched), 100.0 * len(unmatched) / total))

chars_all = sum(len(norm(t)) for _, t in lines.values())
chars_un = sum(len(norm(t)) for _, _, t in unmatched)
print()
print("=== 工作量估算 ===")
print("  剧本总字数      : %d" % chars_all)
print("  需 TTS 的字数   : %d  (%.1f%%)" % (chars_un, 100.0 * chars_un / chars_all))
print("  需 TTS 的句子   : %d" % len(unmatched))

print()
print("=== 未命中样本（前 25 条）===")
for lid, doc, zh in unmatched[:25]:
    print("  [%-16s %-12s] %s" % (doc, lid, zh[:70]))

print()
print("=== 命中样本（前 10 条，验证匹配是否可信）===")
shown = 0
for lid, (doc, zh) in lines.items():
    if lid in id_clips:
        print("  [行号命中 %-12s] %s" % (lid, zh[:70])); shown += 1
    if shown >= 10:
        break

out = open(r"D:\DSHWorkBase\noexistence_a11y\配音覆盖审计.txt", "w", encoding="utf-8")
out.write("去重后剧本行数: %d\n" % total)
out.write("按行号命中: %d\n按文本命中: %d\n需TTS: %d\n" % (by_id, by_text, len(unmatched)))
out.write("需TTS字数: %d / 总字数 %d\n\n" % (chars_un, chars_all))
out.write("=== 需 TTS 的行 ===\n")
for lid, doc, zh in unmatched:
    out.write("%s\t%s\t%s\n" % (doc, lid, zh))
out.close()
print("\n明细已写入 配音覆盖审计.txt")
