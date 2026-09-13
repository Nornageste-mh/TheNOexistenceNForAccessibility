# -*- coding: utf-8 -*-
"""Exact line-array <-> content alignment for a Naninovel Script asset.

The serialized line array lists one type string per line, in order.
The payload region lists one command block per *command* line, in the same order.
GenericTextScriptLine also produces a PrintText block.
So walking the array and consuming one payload block per (GenericText|Command) line
gives an exact lineIndex -> text mapping.
"""
import io, sys, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

TYPES = ["LabelScriptLine", "CommandScriptLine", "GenericTextScriptLine",
         "CommentScriptLine", "EmptyScriptLine"]
FW = {"Naninovel.Commands", "Elringus.Naninovel.Runtime", "Naninovel", "true", "false"}
SCRIPTNAME = re.compile(r"^(Prologue|ED|FakeED|Title|StartGame|ExitGame|VocalConcert|Test|Script|Chars)")

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
        if not (1 <= L <= 200) or i + 4 + L > n:
            continue
        try:
            s = buf[i+4:i+4+L].decode("utf-8")
        except UnicodeDecodeError:
            continue
        if all(ord(c) >= 0x20 for c in s):
            out.append((i, s))
    return out

def analyse(name):
    raw = scripts[name]
    S = all_strings(raw)
    offs = [o for o, _ in S]
    vals = [s for _, s in S]
    an = [i for i in range(len(vals) - 1)
          if vals[i] == "Naninovel.Commands" and vals[i+1] == "Elringus.Naninovel.Runtime"]
    first_payload_off = offs[an[0]] if an else len(raw)

    arr = [t for o, t in
           ((m.start(), m.group(1).decode("ascii"))
            for m in re.finditer(rb"([A-Za-z]+ScriptLine)\x00", raw))
           if t in TYPES and m_start_ok(o, first_payload_off)] if False else []
    arr = []
    for m in re.finditer(rb"([A-Za-z]+ScriptLine)\x00", raw):
        t = m.group(1).decode("ascii")
        if t in TYPES and m.start() < first_payload_off:
            arr.append(t)

    # payload blocks, in order
    blocks = []
    for k, i in enumerate(an):
        if i < 1:
            continue
        cmd = vals[i-1]
        if cmd in FW or SCRIPTNAME.match(cmd):
            continue
        end = an[k+1] - 1 if k + 1 < len(an) else len(vals)
        pl = vals[i+2:end]
        blocks.append((cmd, pl))

    dl = doc_lines(name)
    id2text = dict(dl)

    lines = []
    bi = 0
    mismatch = 0
    for idx, t in enumerate(arr):
        if t in ("GenericTextScriptLine", "CommandScriptLine"):
            if bi < len(blocks):
                cmd, pl = blocks[bi]
                bi += 1
                lid = None
                for x in pl:
                    if x in id2text:
                        lid = x
                        break
                if t == "GenericTextScriptLine" and cmd != "PrintText":
                    mismatch += 1
                lines.append((idx, t, cmd, lid))
            else:
                lines.append((idx, t, "?", None))
        else:
            lines.append((idx, t, "", None))
    return arr, blocks, lines, id2text, mismatch

def m_start_ok(a, b):
    return a < b

name = sys.argv[1] if len(sys.argv) > 1 else "Prologue1_1"
arr, blocks, lines, id2text, mismatch = analyse(name)
print("%s: 行数组 %d 条, 载荷块 %d, GenericText/Command 行 %d, 不匹配 %d"
      % (name, len(arr), len(blocks),
         sum(1 for t in arr if t in ("GenericTextScriptLine", "CommandScriptLine")), mismatch))

if len(sys.argv) > 2:
    lo = int(sys.argv[2]); hi = int(sys.argv[3])
    print("\n=== line %d .. %d ===" % (lo, hi))
    for idx, t, cmd, lid in lines:
        if lo <= idx <= hi:
            txt = id2text.get(lid, "")
            print("  %-5d %-22s %-14s %-12s %s" % (idx, t, cmd, lid or "", txt[:56]))
else:
    KEY = re.compile(r"烤箱|面糊|蛋糕|鸡蛋|面粉|戚风|烤")
    print("\n=== 与「烤」相关 ===")
    for idx, t, cmd, lid in lines:
        txt = id2text.get(lid, "")
        if txt and KEY.search(txt):
            print("  line %-5d %-22s %-12s %s" % (idx, t, lid or "", txt[:56]))
    print("\n=== 尾部 ===")
    for idx, t, cmd, lid in lines[-8:]:
        print("  %-5d %-22s %-14s %s" % (idx, t, cmd, id2text.get(lid, "")[:40]))
