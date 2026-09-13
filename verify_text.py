# -*- coding: utf-8 -*-
"""Focused verification: dialogue structure, per-line voicing config, runtime hook surface."""
import sys, io, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

OUT = open(r"D:\DSHWorkBase\noexistence_a11y\verify_out.txt", "w", encoding="utf-8")
def P(*a):
    s = " ".join(str(x) for x in a)
    OUT.write(s + "\n")

def txt(b):
    return b if isinstance(b, str) else bytes(b).decode("utf-8", "replace")

text_assets = collections.defaultdict(list)
mono = collections.defaultdict(list)
for obj in env.objects:
    if obj.type.name == "TextAsset":
        try:
            d = obj.read()
            text_assets[getattr(d, "m_Name", "?")].append(txt(getattr(d, "m_Script", b"")))
        except Exception:
            pass
    elif obj.type.name == "MonoBehaviour":
        try:
            d = obj.read(check_read=False)
            cls = ""
            try:
                cls = d.m_Script.read().m_ClassName
            except Exception:
                pass
            mono[cls].append(obj)
        except Exception:
            pass

P("=" * 70)
P("A) DIALOGUE STRUCTURE — source vs localized copies of one chapter")
P("=" * 70)
copies = text_assets.get("Prologue2_1", [])
P("copies:", len(copies), "sizes:", [len(c) for c in copies])
for i, c in enumerate(copies):
    head = c[:60].replace("\n", " | ")
    P(f"  [{i}] len={len(c):<7} head={head!r}")
# show a real dialogue slice from the largest and the source-looking one
big = max(copies, key=len)
P("\n--- slice of largest copy (first 1800 chars) ---")
P(big[:1800])
small = min(copies, key=len)
P("\n--- slice of smallest copy (first 900 chars) ---")
P(small[:900])

P("\n" + "=" * 70)
P("B) Script asset (compiled Naninovel script) sample")
P("=" * 70)
for obj in mono.get("Script", [])[:2]:
    try:
        d = obj.read()
        P("name:", getattr(d, "m_Name", "?"), "path_id:", obj.path_id)
        for k, v in list(getattr(d, "__dict__", {}).items()):
            if k.startswith("_") or k in ("m_GameObject", "m_Script"):
                continue
            sv = repr(v)
            P(f"   {k} = {sv[:600]}")
    except Exception as e:
        P("  err:", e)

P("\n" + "=" * 70)
P("C) Key Naninovel configurations (scripted values)")
P("=" * 70)
for cls in ("AudioConfiguration", "TextPrintersConfiguration", "ScriptPlayerConfiguration",
            "LocalizationConfiguration", "ManagedTextConfiguration", "ScriptsConfiguration",
            "InputConfiguration", "CharactersConfiguration", "ChoiceHandlersConfiguration"):
    objs = mono.get(cls, [])
    P(f"\n--- {cls}  ({len(objs)} object(s)) ---")
    for obj in objs[:1]:
        try:
            d = obj.read()
            for k, v in list(getattr(d, "__dict__", {}).items()):
                if k.startswith("_") or k in ("m_GameObject", "m_Script"):
                    continue
                sv = repr(v)
                P(f"   {k} = {sv[:400]}")
        except Exception as e:
            P("   err:", e)

P("\n" + "=" * 70)
P("D) Does any MonoBehaviour look like a screen-reader / TTS bridge?")
P("=" * 70)
interesting = [c for c in mono if any(w in c for w in ("Access", "Speak", "TTS", "Voice", "Narrat", "Reader"))]
P("classes matching Access/Speak/TTS/Voice/Narrat/Reader:", sorted(interesting))

P("\n" + "=" * 70)
P("E) Chars / Script / Test text assets (small ones, full)")
P("=" * 70)
for nm in ("Script", "Test"):
    for i, c in enumerate(text_assets.get(nm, [])[:2]):
        P(f"\n--- {nm}[{i}] len={len(c)} ---")
        P(c[:1500])
P("\n--- Chars: first 600 chars (of %d) ---" % len(text_assets.get("Chars", [""])[0]))
P(text_assets.get("Chars", [""])[0][:600])

P("\n=== ALL distinct MonoBehaviour class names ===")
P(", ".join(sorted(c for c in mono if c)))
OUT.close()
print("written")
