# -*- coding: utf-8 -*-
"""Confirm: the non-dialogue localization entries are AddChoice options (the player's lines)."""
import re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUT = r"D:\DSHWorkBase\noexistence_a11y\选项台词.txt"
env = UnityPy.load(DATA)

def all_strings(buf):
    out, n = [], len(buf)
    for i in range(n - 5):
        L = int.from_bytes(buf[i:i+4], "little")
        if not (1 <= L <= 200) or i + 4 + L > n:
            continue
        try:
            s = buf[i+4:i+4+L].decode("utf-8")
        except UnicodeDecodeError:
            continue
        if all(ord(c) >= 0x20 for c in s):
            out.append(s)
    return out

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

scripts = {}
for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        d = o.read(check_read=False)
        if d.m_Script.read().m_ClassName == "Script":
            scripts[d.m_Name] = o.get_raw_data()
    except Exception:
        pass

# dialogue line ids (from the map we already built)
used = collections.defaultdict(set)
for l in open(r"D:\DSHWorkBase\noexistence_a11y\台词与角色对照.txt", encoding="utf-8"):
    p = l.rstrip("\n").split("\t")
    if len(p) == 6 and p[0].startswith("Prologue"):
        used[p[0]].add(p[1])

# choice ids per script from the bytecode: strings inside AddChoice payloads
choice_ids = collections.defaultdict(set)
for nm, raw in scripts.items():
    vs = all_strings(raw)
    for i, v in enumerate(vs):
        if v == "AddChoice":
            # grab the next stretch of strings, collect doc line ids
            dl = {l for l, _ in doc_lines(nm)}
            j = i
            while j < min(i + 60, len(vs)):
                if vs[j] in dl:
                    choice_ids[nm].add(vs[j])
                j += 1

print("AddChoice 命令引用的行号数（按剧本）：")
tot = 0
for nm in sorted(choice_ids):
    print("   %-14s %d" % (nm, len(choice_ids[nm])))
    tot += len(choice_ids[nm])
print("   合计 %d" % tot)

# overlap with dialogue ids
ov = sum(len(choice_ids[n] & used[n]) for n in choice_ids)
print("\n与对话行号的重叠: %d（应为 0 或极少）" % ov)

# write the choice text
f = open(OUT, "w", encoding="utf-8")
f.write("=== 玩家台词（选项框）—— 由本地化文档中非对话条目 + AddChoice 命令交叉确认 ===\n\n")
n_all = 0
chars = 0
for nm in sorted(used):
    dl = doc_lines(nm)
    if not dl:
        continue
    orph = [(l, t) for l, t in dl if l not in used[nm] and (not choice_ids[nm] or l in choice_ids[nm])]
    if not orph:
        continue
    f.write("--- [%s] %d 条 ---\n" % (nm, len(orph)))
    for l, t in orph:
        f.write("    %-12s %s\n" % (l, t))
        n_all += 1
        chars += len(re.sub(r"<[^>]+>", "", t))
    f.write("\n")
f.write("\n合计 %d 条，%d 字（全部需要 TTS，玩家无声）\n" % (n_all, chars))
f.close()
print("\n玩家选项台词: %d 条，%d 字" % (n_all, chars))
print("写入", OUT)
