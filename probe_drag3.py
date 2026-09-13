# -*- coding: utf-8 -*-
"""Decisive drag check: harvest the wired method names out of every EventTrigger/UnlockableTrigger payload."""
import sys, io, re, collections
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

def owner(raw):
    try:
        fid, pid = int.from_bytes(raw[0:4], "little", signed=True), int.from_bytes(raw[4:12], "little", signed=True)
        return go_name.get(pid, "<pid %d>" % pid) if fid == 0 else "<ext>"
    except Exception:
        return "?"

NOISE = re.compile(r"^(Assembly-CSharp|UnityEngine|Unity\.|Elringus|Naninovel|System|TMPro|"
                   r"m_|UnityEvent|EventTrigger|PersistentCall|Bool|Int|Float|String|Object|Void)$")
methods = collections.Counter()
per_owner = collections.defaultdict(set)

for o in env.objects:
    if o.type.name != "MonoBehaviour":
        continue
    try:
        cls = o.read(check_read=False).m_Script.read().m_ClassName
    except Exception:
        continue
    if cls not in ("EventTrigger", "UnlockableTrigger", "LabeledButton", "ChoiceHandlerButton",
                   "Button", "ScriptableButton", "ScriptableLabeledButton", "ClickThroughPanel"):
        continue
    try:
        raw = o.get_raw_data()
    except Exception:
        continue
    nm = owner(raw)
    for m in re.findall(rb"[\x20-\x7e]{3,60}", raw):
        s = m.decode("ascii")
        if NOISE.match(s) or s.startswith("UnityEngine") or s.startswith("Assembly-CSharp"):
            continue
        if re.fullmatch(r"[A-Za-z_][A-Za-z0-9_]*", s):
            methods[s] += 1
            per_owner[nm].add(s)

print("=== distinct method/field names wired in button & event components ===")
for s, n in methods.most_common(70):
    print("   %4d  %s" % (n, s))

print()
print("=== any drag-related symbol? ===")
drag = [s for s in methods if re.search(r"drag|Drag", s)]
print("   ", drag if drag else "NONE")

print()
print("=== per-owner wired names (sample) ===")
for nm, ss in list(per_owner.items())[:25]:
    print("   %-28s %s" % (nm, sorted(ss)[:8]))
