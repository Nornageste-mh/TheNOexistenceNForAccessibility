# -*- coding: utf-8 -*-
"""Inspect a Naninovel save file: where is the script position?"""
import json, sys, io, os
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

p = sys.argv[1]
data = json.load(open(p, encoding='utf-8'))
print("top-level keys:", list(data.keys()))

m = data.get("objectJsonMap") or {}
keys = m.get("keys", [])
vals = m.get("values", [])
print("states: %d" % len(keys))
for i, k in enumerate(keys):
    short = k.split(",")[0]
    print("  [%d] %s" % (i, short))

for i, k in enumerate(keys):
    if "ScriptPlayer" in k:
        print("\n=== ScriptPlayer state ===")
        v = json.loads(vals[i])
        for kk, vv in v.items():
            s = json.dumps(vv, ensure_ascii=False)
            print("  %-22s %s" % (kk, s[:300]))
