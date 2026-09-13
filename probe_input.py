# -*- coding: utf-8 -*-
import sys, io
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy
env = UnityPy.load(r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d")

def fields(o):
    out = {}
    for k in dir(o):
        if k.startswith("_"):
            continue
        try:
            v = getattr(o, k)
        except Exception:
            continue
        if callable(v):
            continue
        out[k] = v
    return out

for o in env.objects:
    t = o.type.name
    if t == "InputManager":
        d = o.read()
        ax = d.m_Axes
        print("InputManager axes:", len(ax))
        for a in ax:
            f = fields(a)
            keep = {k: f.get(k) for k in ("m_Name", "m_DescriptiveName", "m_PositiveButton",
                                          "m_NegativeButton", "m_AltPositiveButton", "m_AltNegativeButton",
                                          "m_Type", "m_Axis", "m_JoyNum", "m_JoyAxis", "m_JoyButton") if k in f}
            print("  ", keep)
    elif t == "GameObject":
        try:
            n = o.read().m_Name
        except Exception:
            continue
        if n and any(k in n for k in ("EventSystem", "Choice", "Input", "Standalone")):
            print("GO:", n)
