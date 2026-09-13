# -*- coding: utf-8 -*-
"""List components attached to title-screen / special GameObjects to locate the drag-driven hidden achievement."""
import sys, io, re, collections
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

comp_class = {}      # path_id -> readable type name
go_name = {}
go_comp = {}         # go path_id -> [comp path_id]
go_parent = {}       # go path_id -> transform path_id

for o in env.objects:
    t = o.type.name
    if t == "MonoBehaviour":
        try:
            comp_class[o.path_id] = o.read(check_read=False).m_Script.read().m_ClassName
        except Exception:
            comp_class[o.path_id] = "MonoBehaviour?"
    else:
        comp_class[o.path_id] = t
    if t == "GameObject":
        try:
            d = o.read()
            go_name[o.path_id] = d.m_Name
            go_comp[o.path_id] = [(c.component.path_id if hasattr(c, "component") else c.path_id)
                                  for c in d.m_Component]
        except Exception:
            pass

# transform -> go, and parent links, so we can print hierarchy
tr_go = {}
tr_parent = {}
tr_father = {}
for o in env.objects:
    if o.type.name in ("Transform", "RectTransform"):
        try:
            d = o.read()
            gid = d.m_GameObject.path_id if hasattr(d.m_GameObject, "path_id") else d.m_GameObject
            tr_go[o.path_id] = gid
            f = getattr(d, "m_Father", None)
            tr_father[o.path_id] = (f.path_id if hasattr(f, "path_id") else f)
        except Exception:
            pass

go_tr = {g: t for t, g in tr_go.items()}

def path_of(gid, depth=0):
    tr = go_tr.get(gid)
    if tr is None or depth > 12:
        return go_name.get(gid, "?")
    f = tr_father.get(tr, 0)
    if not f:
        return go_name.get(gid, "?")
    pg = tr_go.get(f)
    if pg is None:
        return go_name.get(gid, "?")
    return path_of(pg, depth + 1) + "/" + go_name.get(gid, "?")

PAT = re.compile(r"^(eye|logo|title|drag|erase|qte|score|secret|hidden|clickthrough|waitingforinputindicator|inputfield|variableinput)", re.I)
print("=== GameObjects matching", PAT.pattern, "with their components ===")
for gid, nm in sorted(go_name.items(), key=lambda kv: kv[1] or ""):
    if not nm or not PAT.search(nm):
        continue
    comps = [comp_class.get(c, "?") for c in go_comp.get(gid, [])]
    print("  %-42s [%s]" % (path_of(gid), ", ".join(comps)))

print()
print("=== Every distinct MonoBehaviour class whose name suggests game-specific behaviour ===")
allc = collections.Counter(c for c in comp_class.values()
                           if c and not c.endswith("?") and c not in ("Image", "Button", "Text",
                           "TextMeshProUGUI", "RectTransform", "Transform", "CanvasRenderer"))
for c, n in sorted(allc.items()):
    if re.search(r"Drag|Eye|Secret|Hidden|Hand|Title|Erase|QTE|Score|Unlock|Achieve", c, re.I):
        print("   %-38s x%d" % (c, n))
