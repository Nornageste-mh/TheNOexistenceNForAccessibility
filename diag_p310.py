# -*- coding: utf-8 -*-
"""Debug Prologue3_10 payloads; classify clip names by script; dump Locales doc."""
import re, collections
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
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

raw = None
for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        d = o.read(check_read=False)
        if d.m_Script.read().m_ClassName == "Script" and d.m_Name == "Prologue3_10":
            raw = o.get_raw_data(); break
    except Exception:
        pass

vals = all_strings(raw)
anchors = [i for i in range(len(vals) - 1)
           if vals[i] == "Naninovel.Commands" and vals[i+1] == "Elringus.Naninovel.Runtime"]
print("Prologue3_10: strings=%d anchors=%d" % (len(vals), len(anchors)))
shown = 0
for k, i in enumerate(anchors):
    cmd = vals[i-1] if i >= 1 else "?"
    if cmd != "PrintText":
        continue
    end = anchors[k+1] - 1 if k + 1 < len(anchors) else len(vals)
    print("   payload =", vals[i+2:end][:14])
    shown += 1
    if shown >= 6:
        break

# clip name scripts
clips = collections.Counter()
for o in env.objects:
    if o.type.name == "AudioClip":
        try:
            clips[o.read().m_Name] += 1
        except Exception:
            pass
kana = [c for c in clips if re.search(r"[\u3040-\u30ff]", c)]
han = [c for c in clips if re.search(r"[\u4e00-\u9fff]", c)]
idc = [c for c in clips if re.fullmatch(r"~[0-9a-f]{5,}", c)]
print("\n片段去重 %d | 含假名 %d | 含汉字 %d | 行号式 %d" % (len(clips), len(kana), len(han), len(idc)))
print("含假名样本:", [c[:34] for c in kana[:8]])
print("含汉字样本:", [c[:34] for c in han[:8]])

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
print("\n=== Locales 文档 ===")
for ln in by_name.get("Locales", [""])[0].splitlines()[:60]:
    print("   ", ln)
