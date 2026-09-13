# -*- coding: utf-8 -*-
"""Locate ProcessInput / interactive commands and their line indices."""
import io, sys, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

TYPES = ["LabelScriptLine", "CommandScriptLine", "GenericTextScriptLine",
         "CommentScriptLine", "EmptyScriptLine"]
FW = {"Naninovel.Commands", "Elringus.Naninovel.Runtime", "Naninovel", "true", "false"}
SN = re.compile(r"^(Prologue|ED|FakeED|Title|StartGame|ExitGame|VocalConcert|Test|Script|Chars)")
WANT = re.compile(sys.argv[1] if len(sys.argv) > 1 else r"ProcessInput|ResetText|SpawnShake")

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

for name in sorted(scripts):
    raw = scripts[name]
    S = all_strings(raw)
    offs = [o for o, _ in S]
    vals = [s for _, s in S]
    an = [i for i in range(len(vals)-1)
          if vals[i] == "Naninovel.Commands" and vals[i+1] == "Elringus.Naninovel.Runtime"]
    if not an: continue
    first_payload = offs[an[0]]
    arr = [t for _, t in ((m.start(), m.group(1).decode("ascii"))
            for m in re.finditer(rb"([A-Za-z]+ScriptLine)\x00", raw))
           if t in TYPES and _ < first_payload] if False else []
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

    dl = doc_lines(name)
    id2text = dict(dl)

    hits = []
    bi = 0
    for idx, t in enumerate(arr):
        if t in ("GenericTextScriptLine", "CommandScriptLine"):
            if bi < len(blocks):
                cmd, pl = blocks[bi]; bi += 1
                lid = next((x for x in pl if x in id2text), None)
                if WANT.search(cmd):
                    # 前后最近的一句台词，用来看上下文
                    ctx = ""
                    for j in range(idx, min(idx+14, len(arr))):
                        pass
                    hits.append((idx, cmd, lid, id2text.get(lid, "")))
            else:
                bi += 0
        # 对齐漂移保护：如果命令行数已经超过载荷块，停止
    if hits:
        print("=== %s（行数组 %d 条）===" % (name, len(arr)))
        for idx, cmd, lid, txt in hits:
            print("   line %-5d %-16s %-12s %s" % (idx, cmd, lid or "", txt[:48]))
        print()
