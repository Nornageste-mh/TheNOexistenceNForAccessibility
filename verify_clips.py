# -*- coding: utf-8 -*-
"""Prove/disprove voice clips for PrincessLilith / DemonKingLilith / Mysterious_Girl lines."""
import re
import UnityPy

DATA = r"D:\Steam\steamapps\common\The NOexistenceN of you AND me\TheNOexistenceNofyouANDme_Data\data.unity3d"
env = UnityPy.load(DATA)

clips = []
for o in env.objects:
    if o.type.name == "AudioClip":
        try:
            clips.append(o.read().m_Name)
        except Exception:
            pass
uniq = sorted(set(clips))
print("AudioClip 去重 %d" % len(uniq))
idc = {c for c in uniq if re.fullmatch(r"~[0-9a-f]{5,}", c)}

CASES = [
    ("PrincessLilith", "~c35379b", "终于见到你了……"),
    ("PrincessLilith", "~e0638bfd", "我每天都在这里等待着你，思念着你，对着天空祈愿你的平安。"),
    ("DemonKingLilith", "~2aa3aac2", "逐梦者，费尽心思把你引诱到这里来，就是为了杀掉你……你中了我的圈套。"),
    ("DemonKingLilith", "~fe9a2a2a", "请不要自责，毕竟这是为了让你开心才做的游戏呀。"),
    ("Mysterious_Girl", "~3438c72b", "嗯，我听见了。"),
    ("Mysterious_Girl", "~8d8700e8", "被人盯着的感觉如何？"),
    ("Lilith", "~2f1a8e2c", "喂，快起床，要迟到了哦。"),
    ("Sofa", "-", "(沙发，仅作对照)"),
    ("Chair", "-", "<i>「她没有说什么，只是侧头看着你，仿佛你是她房间里的某样物品。」</i>"),
]
for who, lid, text in CASES:
    n = re.sub(r"<[^>]+>", "", text)
    n = re.sub(r"[\s\u3000]+", "", n)
    byid = lid in idc
    bytext = [c for c in uniq if c not in idc and len(n) >= 6 and c.startswith(n[:6])]
    print("\n%-16s %-12s %s" % (who, lid, text[:40]))
    print("     行号片段存在: %s" % byid)
    print("     文本片段     : %s" % (bytext[:3] if bytext else "无"))

# how many clips look like a Chinese voice line vs a Japanese one
kana = [c for c in uniq if re.search(r"[\u3040-\u30ff]", c)]
han = [c for c in uniq if re.search(r"[\u4e00-\u9fff]", c) and c not in kana]
print("\n去重片段：含假名 %d | 纯中文(无假名) %d | 行号式 %d" % (len(kana), len(han), len(idc)))
print("\n纯中文片段样本(前 12):")
for c in han[:12]:
    print("   ", c[:56])
