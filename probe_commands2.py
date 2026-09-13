# -*- coding: utf-8 -*-
"""Find the game's custom Naninovel commands + try to recover raw Script asset payload."""
import sys, io, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

ROOT = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me"
MD = ROOT + r"\TheNOexistenceNofyouANDme_Data\il2cpp_data\Metadata\global-metadata.dat"
DATA = ROOT + r"\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUT = open(r"D:\DSHWorkBase\noexistence_a11y\script_zh\_custom_commands.txt", "w", encoding="utf-8")
def P(*a):
    s = " ".join(str(x) for x in a)
    OUT.write(s + "\n"); print(s)

txt = open(MD, "rb").read().decode("latin-1", "replace")
ident = sorted(set(re.findall(r"[A-Za-z_][A-Za-z0-9_]{2,}", txt)))

P("=" * 70)
P("A) Naninovel command-related identifiers (custom command surface)")
P("=" * 70)
for i in ident:
    if re.search(r"Command|Alias|CommandParameter", i) and not re.search(
            r"^ISteam|^Steam|ICommand$|^System|Windows|Runtime\.", i):
        P("   ", i)

P("")
P("=" * 70)
P("B) Candidates for the game's own custom commands (short lowercase verbs)")
P("=" * 70)
verbs = sorted(i for i in ident if re.fullmatch(r"[a-z][a-z0-9]{2,18}", i))
interesting = [v for v in verbs if re.search(
    r"qte|vanish|timer|time|drag|erase|shake|glitch|choice|input|type|send|chat|click|press|wait|hide|show|unlock|end", v)]
P("   ", ", ".join(interesting))

P("")
P("=" * 70)
P("C) Raw payload of a Naninovel `Script` asset (looking for source text)")
P("=" * 70)
env = UnityPy.load(DATA)
n = 0
for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        d = o.read(check_read=False)
        if d.m_Script.read().m_ClassName != "Script":
            continue
    except Exception:
        continue
    n += 1
    if n > 3:
        break
    try:
        raw = o.get_raw_data()
    except Exception as e:
        P("  raw read failed:", e); continue
    name = "?"
    try:
        name = d.m_Name
    except Exception:
        pass
    P("  object name=%r path_id=%s raw_len=%d" % (name, o.path_id, len(raw)))
    P("  first 64 bytes hex:", raw[:64].hex())
    # printable runs
    runs = re.findall(rb"[\x20-\x7e\xe4-\xe9][\x20-\x7e\x80-\xbf]{8,}", raw)
    P("  printable runs: %d" % len(runs))
    for r in runs[:6]:
        P("     ", r[:150])

OUT.close()
