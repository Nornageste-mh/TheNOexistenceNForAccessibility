# -*- coding: utf-8 -*-
"""Find where the line-array <-> payload alignment breaks, so we can jump accurately."""
import io, sys, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

TYPES = ["LabelScriptLine", "CommandScriptLine", "GenericTextScriptLine",
         "CommentScriptLine", "EmptyScriptLine"]
FW = {"Naninovel.Commands", "Elringus.Naninovel.Runtime", "Naninovel", "true", "false"}
SN = re.compile(r"^(Prologue|ED|FakeED|Title|StartGame|ExitGame|VocalConcert|Test|Script|Chars)")

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

def all_strings(buf):
    out, n = [], len(buf)
    for i in range(n - 5):
        L = int.from_bytes(buf[i:i+4], "little")
        if not (1 <= L <= 200) or i + 4 + L > n: continue
        try: s = buf[i+4:i+4+L].decode("utf-8")
        except UnicodeDecodeError: continue
        if all(ord(c) >= 0x20 for c in s): out.append((i, s))
    return out

name = "Prologue1_6"
raw = scripts[name]
S = all_strings(raw)
offs = [o for o, _ in S]
vals = [s for _, s in S]
an = [i for i in range(len(vals)-1)
      if vals[i] == "Naninovel.Commands" and vals[i+1] == "Elringus.Naninovel.Runtime"]
first_payload = offs[an[0]]
arr = []
for m in re.finditer(rb"([A-Za-z]+ScriptLine)\x00", raw):
    t = m.group(1).decode("ascii")
    if t in TYPES and m.start() < first_payload:
        arr.append(t)

blocks = []
for k, i in enumerate(an):
    if i < 1: continue
    cmd = vals[i-1]
    if cmd in FW or SN.match(cmd): continue
    end = an[k+1] - 1 if k+1 < len(an) else len(vals)
    blocks.append((cmd, vals[i+2:end]))

print("%s: 行数组 %d, 载荷块 %d, GenericText=%d Command=%d"
      % (name, len(arr), len(blocks),
         arr.count("GenericTextScriptLine"), arr.count("CommandScriptLine")))

# 逐行配对，找第一个「GenericText 却配到非 PrintText」的位置
bi = 0
firstbad = None
rows = []
for idx, t in enumerate(arr):
    if t in ("GenericTextScriptLine", "CommandScriptLine"):
        if bi >= len(blocks):
            rows.append((idx, t, "(载荷耗尽)", None)); continue
        cmd, pl = blocks[bi]; bi += 1
        bad = (t == "GenericTextScriptLine" and cmd != "PrintText")
        if bad and firstbad is None: firstbad = idx
        rows.append((idx, t, cmd, bad))
    else:
        rows.append((idx, t, "", False))
print("首个错配行号:", firstbad)

print("\n=== 错配点前后 20 行 ===")
lo = max(0, (firstbad or 0) - 12)
for r in rows[lo:lo+24]:
    print("  %-5d %-22s %s %s" % (r[0], r[1], r[2], "<<< 错配" if r[3] else ""))

print("\n=== 关键词所在行（按当前对齐）===")
dl = doc_lines(name); id2text = dict(dl)
KEY = re.compile(r"烤箱|说服|时光机器|时间机器|清脆")
for r in rows:
    if not r[2] or r[2] in ("", "(载荷耗尽)"): continue
    # 该块里的 lineId 无法从这里直接拿，先跳
print("  (见下方修正版)")
