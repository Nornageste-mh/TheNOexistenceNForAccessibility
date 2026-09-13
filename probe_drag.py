# -*- coding: utf-8 -*-
"""Pin down claim #4: which GameObjects carry drag-capable EventTriggers, and what UnlockableTriggers exist."""
import sys, io, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

EV = {0: "PointerEnter", 1: "PointerExit", 2: "PointerDown", 3: "PointerUp", 4: "PointerClick",
      5: "DRAG", 6: "Drop", 7: "Scroll", 8: "UpdateSelected", 9: "Select", 10: "Deselect",
      11: "Move", 12: "InitializePotentialDrag", 13: "BEGIN_DRAG", 14: "END_DRAG",
      15: "Submit", 16: "Cancel"}

go_name = {}
for o in env.objects:
    if o.type.name == "GameObject":
        try:
            d = o.read()
            go_name[o.path_id] = d.m_Name
        except Exception:
            pass

def owner(mono):
    try:
        go = mono.m_GameObject
        return go_name.get(go.path_id if hasattr(go, "path_id") else go, "?")
    except Exception:
        return "?"

hits = collections.defaultdict(list)
for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        d = o.read(check_read=False)
        cls = d.m_Script.read().m_ClassName
    except Exception:
        continue
    if cls not in ("EventTrigger", "UnlockableTrigger", "PingPongInputIndicator", "QTEUI",
                   "QTEScoreComponent", "VanishingChoiceHandlerPanel", "ClickThroughPanel",
                   "VariableInputPanel", "TMP_InputField", "ContinueInputUI"):
        continue
    hits[cls].append(o)

for cls in sorted(hits):
    print("=" * 66)
    print("%s  (%d objects)" % (cls, len(hits[cls])))
    print("=" * 66)
    for o in hits[cls]:
        name = owner(o)
        detail = ""
        if cls == "EventTrigger":
            try:
                dd = o.read()
                ents = []
                for e in dd.m_Delegates:
                    eid = getattr(e, "eventID", None)
                    ncalls = len(getattr(e, "callback", []) or [])
                    ents.append("%s(%s)x%d" % (eid, EV.get(eid, "?"), ncalls))
                detail = " | ".join(ents)
            except Exception as ex:
                detail = "read-err: %s" % ex
        else:
            try:
                dd = o.read()
                fields = {k: v for k, v in vars(dd).items()
                          if not k.startswith("_") and k not in ("m_GameObject", "m_Script")}
                detail = ", ".join("%s=%r" % (k, str(v)[:60]) for k, v in list(fields.items())[:12])
            except Exception as ex:
                detail = "read-err: %s" % ex
        print("  GO=%-28s %s" % (name, detail))
