# 从 UABEA dump 的 .glb 里提取预制体层级（glTF 的 JSON chunk 只含 nodes，不含组件字段）
import json, struct, sys, io, os, re
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')

D = r"D:\DSHWorkBase\noexistence_a11y\ext_ref"

def nodes_of(path):
    with open(path, 'rb') as f:
        data = f.read()
    if data[:4] != b'glTF':
        return None
    off = 12
    while off < len(data):
        clen, ctype = struct.unpack_from('<II', data, off)
        chunk = data[off + 8: off + 8 + clen]
        if ctype == 0x4E4F534A:           # 'JSON'
            return json.loads(chunk.decode('utf-8', 'replace'))
        off += 8 + clen
    return None

def walk(g, idx, depth, out, maxdepth=6):
    if depth > maxdepth:
        return
    n = g['nodes'][idx]
    out.append('  ' * depth + (n.get('name') or '(unnamed)'))
    for c in n.get('children', []):
        walk(g, c, depth + 1, out, maxdepth)

for fn in sorted(os.listdir(D)):
    if not fn.endswith('.glb'):
        continue
    g = nodes_of(os.path.join(D, fn))
    print('=' * 66)
    print(fn)
    print('=' * 66)
    if not g:
        print('  (解析失败)')
        continue
    roots = g.get('scenes', [{}])[0].get('nodes', [])
    if not roots:
        roots = [i for i in range(len(g.get('nodes', []))) if i not in
                 {c for n in g['nodes'] for c in n.get('children', [])}]
    lines = []
    for r in roots:
        walk(g, r, 0, lines)
    print('\n'.join(lines) if lines else '  (无节点)')
    print()
