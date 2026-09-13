# -*- coding: utf-8 -*-
"""L0: export the full zh-CN source script from Naninovel localization docs, then index QTE-ish lines."""
import sys, io, re, collections, os
sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding='utf-8', errors='replace')
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
OUTDIR = r"D:\DSHWorkBase\noexistence_a11y\script_zh"
os.makedirs(OUTDIR, exist_ok=True)

env = UnityPy.load(DATA)
by_name = collections.defaultdict(list)
for o in env.objects:
    if o.type.name == "TextAsset":
        try:
            d = o.read()
            n = getattr(d, "m_Name", "?")
            s = getattr(d, "m_Script", b"")
            s = s if isinstance(s, str) else bytes(s).decode("utf-8", "replace")
            by_name[n].append(s)
        except Exception:
            pass

print("distinct docs:", len(by_name))

SCRIPT_NAMES = [n for n in by_name if re.match(r"^(Prologue|ED|FakeED|Title|StartGame|ExitGame|VocalConcert|Test|DisplayName_Cate)", n)]
print("script-like docs:", len(SCRIPT_NAMES))

zh_lines = []      # (doc, lineid, zh)
for name in sorted(by_name):
    body = by_name[name][0]
    doc_lines = []
    if body.lstrip().startswith("\ufeff#,") or body.lstrip().startswith("#,"):
        # CSV form: id,?,?,zh-CN,en,ja
        for row in body.splitlines()[1:]:
            parts = row.split(",")
            if len(parts) >= 4 and parts[3].strip():
                doc_lines.append((parts[0].strip(), parts[3].strip()))
    else:
        cur = None
        for ln in body.splitlines():
            m = re.match(r"^#\s*(\S+)", ln)
            if m:
                cur = m.group(1)
                continue
            if ln.startswith(";") and cur:
                zh_lines.append((name, cur, ln[1:].strip()))
                doc_lines.append((cur, ln[1:].strip()))
    if doc_lines:
        with open(os.path.join(OUTDIR, name + ".zh.txt"), "w", encoding="utf-8") as f:
            for i, t in doc_lines:
                f.write("%s\t%s\n" % (i, t))
    for i, t in doc_lines:
        pass  # already appended for txt form

# rebuild zh_lines properly for both forms
zh_lines = []
for name in sorted(by_name):
    body = by_name[name][0]
    if body.lstrip().startswith("\ufeff#,") or body.lstrip().startswith("#,"):
        for row in body.splitlines()[1:]:
            parts = row.split(",")
            if len(parts) >= 4 and parts[3].strip():
                zh_lines.append((name, parts[0].strip(), parts[3].strip()))
    else:
        cur = None
        for ln in body.splitlines():
            m = re.match(r"^#\s*(\S+)", ln)
            if m:
                cur = m.group(1); continue
            if ln.startswith(";") and cur:
                zh_lines.append((name, cur, ln[1:].strip()))

with open(os.path.join(OUTDIR, "_ALL_zh.tsv"), "w", encoding="utf-8") as f:
    for n, i, t in zh_lines:
        f.write("%s\t%s\t%s\n" % (n, i, t))

print("zh source lines exported:", len(zh_lines))
print("files written to", OUTDIR)

# ---- index lines mentioning reaction / timing / drag / QTE ----
KW = re.compile(r"点击|点一下|按|键盘|鼠标|拖动|拖拽|时间|秒|快|来不及|反应|限时|倒计时|消失|选|屏幕|按钮|擦|涂|划|输入|打字|名字")
hits = [(n, i, t) for n, i, t in zh_lines if KW.search(t)]
print("\nlines matching control/timing keywords:", len(hits))
with open(os.path.join(OUTDIR, "_control_keyword_hits.txt"), "w", encoding="utf-8") as f:
    for n, i, t in hits:
        f.write("[%s %s] %s\n" % (n, i, t))

# QTE-specific: scan ALL metadata-ish text for QTE context
MD = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\il2cpp_data\Metadata\global-metadata.dat"
txt = open(MD, "rb").read().decode("latin-1", "replace")
ident = set(re.findall(r"[A-Za-z_][A-Za-z0-9_]{2,}", txt))
qte = sorted(i for i in ident if re.search(r"QTE|QuickTime|Rhythm|Combo|Perfect|Miss|Hit|Beat|Press|Timing|Score|Fail|Retry|GameOver|Countdown", i))
print("\nQTE-ish identifiers in metadata:", len(qte))
with open(os.path.join(OUTDIR, "_qte_identifiers.txt"), "w", encoding="utf-8") as f:
    for q in qte:
        f.write(q + "\n")
for q in qte[:80]:
    print("   ", q)
