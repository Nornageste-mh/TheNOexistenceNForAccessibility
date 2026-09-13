# -*- coding: utf-8 -*-
"""Quick reconnaissance: what object types / text carriers live inside the game's Unity bundle."""
import sys, io, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"

env = UnityPy.load(DATA)
counts = collections.Counter()
textassets = []
monobehaviours = []
for obj in env.objects:
    counts[obj.type.name] += 1
    if obj.type.name == "TextAsset":
        try:
            d = obj.read()
            textassets.append((getattr(d, "m_Name", "?"), getattr(d, "m_Script", b"") if hasattr(d, "m_Script") else b""))
        except Exception as e:
            textassets.append(("<read-error:%s>" % e, b""))
    elif obj.type.name == "MonoBehaviour":
        monobehaviours.append(obj)

print("=== OBJECT TYPE COUNTS ===")
for k, v in counts.most_common():
    print(f"{v:>7}  {k}")

print("\n=== TextAsset NAMES (first 120) ===")
print("total TextAssets:", len(textassets))
for i, (n, s) in enumerate(textassets[:120]):
    head = bytes(s[:70]).decode("utf-8", "replace").replace("\n", " | ") if isinstance(s, (bytes, bytearray)) else ""
    print(f"[{i}] {n!r}  len={len(s) if s else 0}  :: {head}")

print("\n=== MonoBehaviours: try class-name resolution (first 60 readable) ===")
shown = 0
for obj in monobehaviours:
    if shown >= 60:
        break
    try:
        d = obj.read(check_read=False)
        name = getattr(d, "m_Name", "?")
        script = getattr(d, "m_Script", None)
        cls = ""
        try:
            if script is not None:
                cls = script.read().m_ClassName
        except Exception:
            cls = "?"
        print(f"{obj.path_id}  name={name!r}  class={cls!r}")
        shown += 1
    except Exception as e:
        print("skip:", obj.path_id, e)
