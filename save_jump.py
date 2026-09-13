# -*- coding: utf-8 -*-
"""Recover the compiled line array of a Naninovel Script asset:
   line index -> (type, lineId/text) so we can pick a playbackSpot to jump to."""
import io, sys, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

TYPES = ["LabelScriptLine", "CommandScriptLine", "GenericTextScriptLine",
         "CommentScriptLine", "EmptyScriptLine"]

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

# docs (zh source text)
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

TARGET = sys.argv[1] if len(sys.argv) > 1 else "Prologue1_1"
raw = scripts[TARGET]

# 1) line-type sequence, in order
seq = []
for m in re.finditer(rb"([A-Za-z]+ScriptLine)\x00", raw):
    t = m.group(1).decode("ascii")
    if t in TYPES:
        seq.append((m.start(), t))
print("%s: 行数组条目 %d" % (TARGET, len(seq)))

# 2) PrintText payload lineIds in order
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
            out.append((i, s))
    return out

S = all_strings(raw)
offs = [o for o, _ in S]
vals = [s for _, s in S]
an = [i for i in range(len(vals) - 1)
      if vals[i] == "Naninovel.Commands" and vals[i+1] == "Elringus.Naninovel.Runtime"]
# 行数组结束 = 第一个命令载荷锚点所在区域之前；简化：行数组条目都出现在第一段
first_payload = an[0] if an else len(vals)
arr = [(o, t) for o, t in seq if o < offs[first_payload]] if an else seq
print("  其中落在行数组区的: %d" % len(arr))

gt = [i for i, (_, t) in enumerate(arr) if t == "GenericTextScriptLine"]
print("  GenericText 行: %d" % len(gt))

dl = doc_lines(TARGET)
print("  文档行: %d" % len(dl))

id2text = dict(dl)
lines = []
if len(gt) == len(dl):
    for k, idx in enumerate(gt):
        lines.append((idx, dl[k][0], dl[k][1]))
    print("  ✓ 行号对齐成功")
else:
    print("  ✗ 数量不一致，无法一一对齐（只按 GenericText 序号对齐）")
    for k, idx in enumerate(gt):
        lid = dl[k][0] if k < len(dl) else "?"
        lines.append((idx, lid, id2text.get(lid, "")))

KEY = re.compile(r"烤箱|面糊|蛋糕|鸡蛋|牛奶|面粉|烘焙|烤")
print("\n=== 与「烤」相关且落在行数组里的行 ===")
for idx, lid, txt in lines:
    if KEY.search(txt):
        print("  line %-5d %-12s %s" % (idx, lid, txt[:60]))

print("\n=== 行数组尾部 10 条 ===")
for idx, t in arr[-10:]:
    print("  %-5d %s" % (idx, t))
print("\n总行数（=最后一个 index+1）: %d" % len(arr))
