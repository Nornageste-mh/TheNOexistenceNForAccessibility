# -*- coding: utf-8 -*-
"""Verify text layer: dump sample script/managed-text/localization content and key Naninovel configs."""
import sys, io, collections, json
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

def txt(b):
    if isinstance(b, str):
        return b
    return bytes(b).decode("utf-8", "replace")

# ---- group TextAssets by name, with container path ----
groups = collections.defaultdict(list)
mono_by_class = collections.defaultdict(list)

for obj in env.objects:
    if obj.type.name == "TextAsset":
        try:
            d = obj.read()
            groups[getattr(d, "m_Name", "?")].append((obj.container, txt(getattr(d, "m_Script", b""))))
        except Exception as e:
            groups["<err>"].append((obj.container, str(e)))
    elif obj.type.name == "MonoBehaviour":
        try:
            d = obj.read(check_read=False)
            cls = ""
            try:
                cls = d.m_Script.read().m_ClassName
            except Exception:
                pass
            mono_by_class[cls].append(obj)
        except Exception:
            pass

print("=== distinct TextAsset names: %d ; total objects: %d ===" % (len(groups), sum(len(v) for v in groups.values())))
for name in sorted(groups, key=lambda n: (-len(groups[n]), n)):
    paths = sorted({(c or "").split("/")[0] for c, _ in groups[name]})
    print(f"{name:<28} copies={len(groups[name]):<3} size={len(groups[name][0][1]):<7} containers={paths[:4]}")

def dump(name, idx=0, n=900):
    print(f"\n----- TextAsset {name!r} (copy {idx}) -----")
    if name not in groups:
        print("  NOT FOUND"); return
    c, s = groups[name][idx]
    print("  container:", c)
    print("  len:", len(s))
    print(s[:n])

for nm in ("Locales", "DefaultUI", "CharacterNames", "Script", "Title", "StartGame", "ThanksList"):
    dump(nm, 0, 1200)

# biggest script-ish text asset sample
for nm in ("Prologue2_1", "Prologue3_10", "DisplayName_Cate_7"):
    dump(nm, 0, 1500)

print("\n=== Naninovel config MonoBehaviours of interest ===")
for cls in ("AudioConfiguration", "LocalizationConfiguration", "ScriptPlayerConfiguration",
            "ScriptsConfiguration", "ManagedTextConfiguration", "TextPrintersConfiguration",
            "EngineConfiguration", "Script"):
    objs = mono_by_class.get(cls, [])
    print(f"\n--- class {cls}: {len(objs)} objects ---")
    for obj in objs[:2]:
        try:
            d = obj.read()
            print("  name:", getattr(d, "m_Name", "?"), "path_id:", obj.path_id)
            tree = getattr(d, "__dict__", {})
            keys = [k for k in tree.keys() if not k.startswith("_") and k not in ("m_GameObject", "m_Script")]
            for k in keys[:40]:
                v = tree[k]
                sv = repr(v)
                if len(sv) > 160:
                    sv = sv[:160] + "..."
                print(f"     {k} = {sv}")
        except Exception as e:
            print("  read error:", e)
