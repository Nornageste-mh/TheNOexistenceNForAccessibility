# -*- coding: utf-8 -*-
"""Manually resolve owner GameObjects of EventTrigger/UnlockableTrigger via raw PPtr, and hunt drag eventIDs."""
import sys, io, struct, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

go_name = {}
for o in env.objects:
    if o.type.name == "GameObject":
        try:
            go_name[o.path_id] = o.read().m_Name
        except Exception:
            pass

# also map Transform -> GameObject so we can name parents/children if needed
def owner_of(raw):
    """Raw MonoBehaviour payload starts with m_GameObject PPtr {int fileID; long pathID}."""
    if len(raw) < 12:
        return None
    fid, pid = struct.unpack_from("<iq", raw, 0)
    if fid == 0:
        return go_name.get(pid)
    return "<external fid=%d pid=%d>" % (fid, pid)

targets = ("EventTrigger", "UnlockableTrigger", "VanishingChoiceHandlerPanel", "QTEUI",
           "QTEScoreComponent", "ClickThroughPanel", "ContinueInputUI", "VariableInputPanel",
           "TMP_InputField", "PingPongInputIndicator", "EraseUI", "CustomEffectUI", "TitleMenu")

by_class = collections.defaultdict(list)
for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        cls = o.read(check_read=False).m_Script.read().m_ClassName
    except Exception:
        continue
    if cls in targets:
        try:
            raw = o.get_raw_data()
        except Exception:
            raw = b""
        by_class[cls].append((owner_of(raw), len(raw), raw))

for cls in sorted(by_class):
    print("=" * 70)
    print("%s : %d objects" % (cls, len(by_class[cls])))
    print("=" * 70)
    sizes = collections.Counter(r[1] for r in by_class[cls])
    print("   raw sizes:", dict(sizes))
    for name, ln, raw in by_class[cls][:14]:
        # scan for plausible EventTriggerType ids (0..16) preceded by vector length 1
        ids = []
        for i in range(24, max(24, min(len(raw) - 4, 200))):
            v = struct.unpack_from("<i", raw, i)[0]
            if 0 <= v <= 16 and struct.unpack_from("<i", raw, i - 4)[0] in (1, 2, 3):
                ids.append((i, v))
        print("   owner=%-26s size=%-5d candidateIDs=%s" % (name, ln, ids[:8]))

# hunt: does any raw payload mention drag event ids in a 1-entry vector right after a size field?
print()
print("=== drag check: EventTrigger payloads whose entry vector length is 1 and id in {5,13,14} ===")
cnt = collections.Counter()
for name, ln, raw in by_class.get("EventTrigger", []):
    found = False
    for i in range(24, max(24, min(len(raw) - 4, 240))):
        v = struct.unpack_from("<i", raw, i)[0]
        if v in (5, 13, 14) and struct.unpack_from("<i", raw, i - 4)[0] in (1, 2, 3):
            cnt[name] += 1
            found = True
    if found:
        pass
print("owners with drag-ish event ids:", dict(cnt))
