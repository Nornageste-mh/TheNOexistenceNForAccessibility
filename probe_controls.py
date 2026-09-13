# -*- coding: utf-8 -*-
"""Cross-check the player-reported control inventory against shipped assets & IL2CPP metadata."""
import sys, io, collections, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

ROOT = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me"
DATA = ROOT + r"\TheNOexistenceNofyouANDme_Data\data.unity3d"
MD = ROOT + r"\TheNOexistenceNofyouANDme_Data\il2cpp_data\Metadata\global-metadata.dat"
OUT = open(r"D:\DSHWorkBase\noexistence_a11y\controls_out.txt", "w", encoding="utf-8")
def P(*a):
    OUT.write(" ".join(str(x) for x in a) + "\n")

# ---------- 1. full MonoBehaviour class inventory ----------
env = UnityPy.load(DATA)
classes = collections.Counter()
gameobjects = []
for o in env.objects:
    if o.type.name == "MonoBehaviour":
        try:
            c = o.read(check_read=False).m_Script.read().m_ClassName
            classes[c] += 1
        except Exception:
            pass
    elif o.type.name == "GameObject":
        try:
            gameobjects.append(o.read().m_Name)
        except Exception:
            pass

P("=" * 72)
P("1) FULL MonoBehaviour class inventory (%d distinct)" % len(classes))
P("=" * 72)
for c in sorted(classes):
    P("   %-46s x%d" % (c, classes[c]))

P("")
P("=" * 72)
P("2) GameObjects whose names hint at buttons / choices / drag / title / timed")
P("=" * 72)
kw = re.compile(r"button|btn|choice|select|option|drag|title|timer|countdown|time|fade|"
                r"canvas|printer|dialog|text|continue|input|hidden|secret|eye|logo|erase|click",
                re.I)
for n in sorted(set(gameobjects)):
    if n and kw.search(n):
        P("   ", n)

# ---------- 3. metadata identifier scan ----------
raw = open(MD, "rb").read()
txt = raw.decode("latin-1", "replace")
ident = set(re.findall(r"[A-Za-z_][A-Za-z0-9_]{3,}", txt))

def scan(title, pattern, limit=70):
    rx = re.compile(pattern)
    hits = sorted(i for i in ident if rx.search(i))
    P("")
    P("-" * 72)
    P("%s  (%d hits)" % (title, len(hits)))
    P("-" * 72)
    for h in hits[:limit]:
        P("   ", h)
    if len(hits) > limit:
        P("    ... +%d more" % (len(hits) - limit))

scan("A) drag / pointer handlers", r"^(I?)(Begin|On|End|Initialize)?Drag|DragHandler|IDrag|PointerDrag|OnMouseDrag|DragEvent|Draggable")
scan("B) timers / countdown / time-limited choice", r"Timer|Countdown|TimeLimit|TimeOut|Timeout|Elapsed|Deadline|AutoSelect|AutoChoice|ChoiceTime|RemainingTime")
scan("C) fade / dissolve / disappear", r"Fade|Dissolve|Disappear|Vanish|Alpha(Tween|Out)|DoFade")
scan("D) Unity event-system interfaces present", r"^(IPointer|IEventSystem|ISelect|IMove|ISubmit|ICancel|IDeselect|IUpdateSelected|IInitializePotential)")
scan("E) Steam achievements / stats", r"Achievement|SteamStats|SteamUser|SetAchievement|StoreStats|G_Achievement|UnlockAchievement")
scan("F) title-screen specific", r"^Title|TitleMenu|TitleUI|Eye|Logo")
scan("G) Naninovel choice / printer surface", r"ChoiceHandler|ChoiceButton|Choice|TextPrinter|Backlog|InputIndicator|ContinueInput")
scan("H) accessibility-ish (control)", r"Accessib|Narrat|ScreenReader|Speak|Tts|TTS|VoiceOver")

P("")
P("=" * 72)
P("3) Achievement-style string literals found in metadata")
P("=" * 72)
lits = sorted(set(re.findall(r"G_[A-Za-z0-9_]{2,}", txt)))
for l in lits:
    P("   ", l)

OUT.close()
print("written")
