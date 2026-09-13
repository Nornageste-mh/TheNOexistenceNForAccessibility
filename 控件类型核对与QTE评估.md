# 控件类型核对 & 强制反应环节评估

> 输入：你作为玩家观察到的 5 类控件 + 通关后的实测回答
> 方法：包内资源枚举 + IL2CPP 元数据 + 编译后的 Naninovel 剧本原始还原 + 外部全成就攻略交叉验证
> 结论标记：✅ 证实　🟡 部分证实/需修正　⚠️ 新风险　🔴 硬阻断

---

## ⚡ 玩家实测确认（本节优先级最高，推翻了下面两处静态推断）

来自通关玩家的直接回答：

| # | 问题 | 玩家实测回答 | 对结论的影响 |
|---|---|---|---|
| 1 | 配音覆盖 | **只有女主角莉莉丝有配音**；旁白、主角 Player、其余角色**全都没有配音** | 🔴 **推翻**"自朗读基线"假设 —— 纯靠听**无法**跟上剧情，TTS 旁白从"锦上添花"变为**必需品** |
| 2 | QTE 操作形式 | 需要**在屏幕上找到散落的正确选项**，并且有类似节奏游戏的**「完美点击」/「普通节奏」判定** | 双重不可达：**空间视觉搜索 + 计时精度**，读屏/TTS 均无法代劳 |
| 3 | QTE 是否卡主线 | **不能，分数不达标会卡住或必须重来**；**修正值低于 60% 会无限循环该小游戏** | 🔴 **推翻**"QTE 不卡主线"的乐观推断 —— 这是**确证的硬阻断** |
| 4 | 纯听觉可玩性 | 除未配音环节与节奏小游戏外**完全可行**；少数仅存于画面的信息**不是关键信息**，多为玩梗内容（如"这个蛋糕上的草莓是不是太大了"） | 🟢 叙事层无障碍**无实质损失**，改造价值成立 |
| 5 | 键盘 / 手柄 | **能推进对话，但选项必须用鼠标点** | 🔴 **第二个硬阻断**：选项无法键盘选择 |
| 6 | "定时选项" | 玩家澄清：指的就是**烤箱节奏小游戏**；低于 60% 无限循环 | 与 `VanishingChoiceHandlerPanel` + `ButtonVanishingList` 相互印证，很可能**就是 QTE 自己的渐隐按钮**，而非独立的剧情限时选项 |
| 7 | 打字环节 | **没有打字环节，都是点选** | 🟢 排除一处顾虑（资产里那 1 个 `TMP_InputField` + `VariableInputPanel` 未被实际使用，或可用任意输入通过） |

### 由实测得出的最终判定

| 目标 | 判定 | 卡在哪 |
|---|---|---|
| **听懂全部剧情** | 🟡 需 TTS 旁白 | 只有莉莉丝有配音 → 其余全部台词必须靠 TTS 补 |
| **独立通关（不追求成就）** | 🔴 **当前不可行** | ① 选项必须鼠标点；② **烤箱 QTE 必须 ≥60% 否则无限循环** |
| **全成就 / 100%** | 🔴 需更多特化 | 上述两项 + 标题拖拽 + Konami 秘技 + 设置页点字 + 挂机 20~30 分钟 |

**关键结论：本作存在两处互相独立的硬阻断，其中烤箱 QTE 是主阻断。**
好消息是两处都在**同一个注入层**（BepInEx IL2CPP 插件）里可解，且 QTE 是自成一体的 `QTEUI`/`QTEScoreComponent` 子系统，**替代通道的实现面很窄**（改判定窗口或直接给分），不必重写游戏逻辑。

### 最终确认（玩家补答）

> **"只有烤箱那一段需要反应。"**

这条把整个风险评估收敛了：

- ✅ **全流程只有一处限时/反应要求**，就是烤箱 QTE；
- ✅ 普通剧情选项**不会**超时自动选 —— 印证了 `VanishingChoiceHandlerPanel` + `ButtonVanishingList` + `ForceAutoSelect` **只服务 QTE**（散落按钮渐隐 → 超时自动选 → 得分偏低 → 低于 60% 循环），并非另有独立的剧情限时选项；
- ✅ 因此**改造面很窄**：要单独攻克的只有 `QTEPanel` 一个子系统，其余全是"补朗读 + 补键盘导航"的常规工作。

| 目标 | 判定 | 卡在哪 | 阻断点数 |
|---|---|---|---|
| **听懂全部剧情** | 🟢 可达 | 只需 TTS 补 1221 句 / 2 万字 | 0 |
| **独立通关（不追求成就）** | 🟡 可达，需改两处 | ① 选项鼠标依赖；② 烤箱 QTE | **1 个反应类 + 1 个输入类** |
| **全成就 / 100%** | 🟡 可达，需更多特化 | 上述 + 标题拖拽 / Konami / 设置点字 / 挂机 20~30 分钟 | 多为"知识型"，不需改程序 |

---

## 逐条核对

### ① 普通按钮（含绑定文字）—— ✅ 证实

| 证据 | 内容 |
|---|---|
| `Button` ×44 | Unity 原生 UI Button |
| `LabeledButton` ×119 | 本作的主力按钮（带文字） |
| `LabeledTitleButton` ×10 / `ScriptableButton` ×11 / `ScriptableLabeledButton` ×3 | 变体 |
| 文字载体 | `TextMeshProUGUI` ×**764**、旧版 `Text` ×164 |
| 文字绑定 | `ManagedTextProvider` ×**873**（UI 文案与剧本解耦，走 Naninovel 托管文本） |
| 按钮动效状态 | 从按钮序列化数据里抽出的方法名：`Normal` / `Highlighted` / `Pressed` / `Disabled` / `Selected` |

**结论**：你的观察准确。补充一点——**873 个文字绑定意味着 UI 文案几乎全部可离线取出**（它们就在我导出的 `DefaultUI` / `Chars` 等文档里），这对无障碍是大利好。

### ② 选项按钮 —— ✅ 证实，且实际有**三种**

| 类 | 数量 | 说明 |
|---|---|---|
| `ChoiceHandlerPanel` | 6 | 标准选项面板 |
| `ChoiceHandlerButton` | 3 | 选项按钮（与普通按钮同族） |
| `AllSelectChoiceHandlePanel` | 1 | 全选式列表（`ButtonAllSelectList`） |
| `VanishingChoiceHandlerPanel` | 2 | **会消失的选项**（见 ⑤） |

`ChoiceButton` 上额外绑定了 `set_text`（文本由剧本动态注入）与 `Selected` 状态。你说"和普通按钮差不多"是对的，差别在**多一个 Selected 态**，以及文本框是运行时注入的。

### ③ 剧情文本 + 画布 —— ✅ 证实，但**实现方式要修正一处**

**本作没有用 Naninovel 默认的文本打印器**，而是自己写了一整套 Reveal（逐字显现）体系：

| 类 | 数量 |
|---|---|
| `RevealableTextPrinterPanel` | 9 |
| `RevealableText` | 13 |
| `RevealFader` | 11 |
| `RevealFadeAll` | 2 |
| `RevealClipper` | 7 |
| `RevealBroadcaster` | 9 |
| `RevealPaginator` | 1 |
| `CustomPreviewPrinter` | 4 |
| `ChatPrinterPanel` | 1 |
| `NaninovelTMProText` | 36 |
| `SubtitleUI` | 2 |

**对实现的影响（重要）**：挂钩点**不能**选 UI 面板类（`RevealableTextPrinterPanel` 是自定义的，版本一变就废），应选 Naninovel 的稳定接口 —— 这些接口在元数据中已确认存在：`ITextPrinterManager`、`ITextPrinterActor`、`ITextPrinter`、`PrintText`、`HidePrinter`、`ModifyTextPrinter`。

顺带：剧本里还原出了 `Chapter_Title` 这个打印器名，说明存在**独立于对话框的标题式打印器**（章节标题走另一套画布），旁白方案要覆盖它。

### ④ 可拖动图片（标题界面隐藏成就）—— ✅ 机制证实，且**实现方式是标准控件（此前结论已推翻）**

**外部攻略证实了机制**（两条独立来源一致）：
> 「摇摇欲坠的舞台」：在**主界面**移动「不」旁边的符号「/」，往左划**隐藏「不」字** —— 见 [3楼猫全成就攻略](https://game.3loumao.org/677977001?language=zh-tw)、[52PK 全成就攻略](https://m.52pk.com/pc/miji/7587136.shtml)

即：**游戏标题本身「不/存在的你，和我」就是那个可拖动控件**，把「/」划走让「不」消失，标题就变成「存在的你和我」。

> ### 🔄 更正：我此前判定它是"代码轮询式实现、无法靠控件类型挂钩"——**这是错的**
>
> 靠第三方资产 dump（[minhmc2007/The-noexistencen-of-you-and-me-assets](https://github.com/minhmc2007/The-noexistencen-of-you-and-me-assets)）拿到 `TitleUI` 的预制体层级后，真相是：
>
> ```
> TitleUI/LogoPanel/Image_Title/
>   Slider                     ← 标准 uGUI Slider
>     Fill Area/Fill
>     Handle Slide Area/Handle  ← 你拖的就是这个 Handle
> ```
>
> **它就是一个 Unity 标准 `Slider`**。`Slider` 内部自带 `IDragHandler` / `IInitializePotentialDragHandler` 实现，所以：
> - 不需要 `EventTrigger` 绑拖拽事件（我查 108 个 EventTrigger 没找到，是对的，但推断错了方向）；
> - 不需要任何含 "Drag" 的类名（`Slider` 不靠类名体现拖拽能力）。
>
> **为什么这是好消息**：`Slider` 是 `Selectable`，**能用键盘方向键驱动**。而 TransparentHer 那个模组的 `UiNav` 已经实现了「`←` `→` 调整滑条」——**这条隐藏成就几乎是白送的**，复用现成代码路径即可，不必去挂钩 `Input.mousePosition`。
>
> 教训：静态类名/事件扫描会漏掉"能力藏在标准组件里"的情况，查预制体层级比查类名可靠。

**同一区域还叠了两个隐藏交互**（攻略证实，都属于"玩家得先知道有这回事"的类型）：
- **Konami 秘技**：主菜单输入 `↑↑↓↓←→←→BABA` → 成就「视觉小说糕手」（这条对盲人反而**友好**，因为纯键盘）；
- **设置页点字**：在设置里点击「你」让它变成「我」→ 成就「你的世界，她的世界」（靠 `SettingsUI/CharacterView` 与 `PlayerView` 两套界面切换）。

**标题菜单本体是标准带标签按钮**（`ButtonsPanel` → `CustomEnterGameButton` / `SettingsButton` / `GalleryButton` / `ExtrasButton` / `ExitButton`，各含一个 `Label`），因此**数字键导航可以整体套用**。

### ⑤ 定时渐隐的选项按钮 —— ✅ 证实，且有一个**关键的缓解设计**

| 证据 | 内容 |
|---|---|
| `VanishingChoiceHandlerPanel` ×2 | 挂在 GameObject **`ButtonVanishingList`** 上 |
| 元数据方法 | `ForceAutoSelect`、`RemainingTime`、`RemoveAllChoiceButtonsDelayed`、`HandleChoiceVisibilityChanged`、`AddChoice`（剧本中调用 11 次） |
| 渐变效果 | DOTween + `RevealFader` / `FadeImage` |

**`ForceAutoSelect` 是这里最重要的一个词**：超时不是死锁，而是**系统替你自动选一个**。所以——

- ✅ 盲人玩家**不会被卡死**，流程能继续；
- ⚠️ 但会**静默失去选择权**，而且"时间正在流逝"这件事本身**没有任何听觉表征**（没有倒计时音效类，`PlaySfx` 是通用音效命令）。

**改造建议**：给渐隐选项加**听觉倒计时**（渐弱的滴答声或旁白提示"选项即将消失"），这是本项目里投入产出比最高的一处无障碍改动。

---

## ⚠️ 你没提到、但我发现的第 6 类控件：**计分 QTE**

这是本次核对里最重的发现，它也正好对应你说的"强制要求快速反应"。

**它是独立于选项系统的一套东西**：

| 证据 | 内容 |
|---|---|
| `QTEUI` ×2 | 挂在 GameObject **`QTEPanel`** 上（命名空间 `Naninovel.UI`，即作者把自定义 UI 挂进了 Naninovel 命名空间） |
| `QTEScoreComponent` ×1 | 挂在 `ScoreComponent` 上 |
| `QTEButton` | **用的是和普通按钮同一个 `LabeledButton` 类** |
| 其他类/方法 | `QTEStatus`、`QTEContainer`、`ScoreContainer`、`ScorePanel`、`TextScore`、`StartQTE`、`FinishQTE`、`CheckScore`、`AccumulateTimingValue`、`targetTimings`、`Perfect`、`spritePerfect`、`PushQQLilithQTEScoreQuantifier` |

**预制体层级（来自第三方资产 dump，补上了 UnityPy 读不出的那块）**：

```
QTEPanel                        ← QTEUI 挂在这
├── Image_Background → oven     ← 烤箱
├── ButtonContainer             ← 运行时生成 QTEButton 的容器（预制体里是空的）
├── ScoreContainer
├── ScorePanel → TextName, TextScore
└── TextNode → Text (TMP) ×5    ← 正好对上 QTEPanel.Text1~5

ScoreComponent                  ← QTEScoreComponent 挂在这
└── ScoreComponent → Miss / Good / Perfect   ← 各带一个 Mesh
```

两条重要推论：

1. **判定是三档 `Miss` / `Good` / `Perfect`** —— 与你说的「完美点击 / 普通节奏」完全吻合，也和元数据里的 `Perfect`、`spritePerfect`、`AccumulateTimingValue` 对上；
2. **`QTEButton` 的结构是 `QTEButton → Text`，与 `ChoiceButton`（`ChoiceButton → Text`）一模一样**，两者又都用同一个 `LabeledButton` 组件 —— 意味着 **QTE 的"点击输入"可以套用与普通选项完全相同的数字键方案**。难的不是"点哪里"，而是**计时与判定窗口**。这显著缩小了 QTE 替代通道的实现面。

**它的文案我直接取到了**（来自 `DefaultUI` 托管文本）：

```
QTEPanel.ScoreName: 修正值：
QTEPanel.Text1: 你不是时光机器
QTEPanel.Text2: 你是烤箱
QTEPanel.Text3: 你并非不是一个烤箱
QTEPanel.Text4: 你除了烤箱还能是什么呢
QTEPanel.Text5: 你从来都是烤箱
```

剧本里的操作提示是 `（按下发送键）`（`Prologue1_6`）—— 对应"烤箱/时间机器"那一章。

**外部攻略确认了它的判定与后果**：

| 成就 | 条件 |
|---|---|
| 掌握未来女神计划 | 修正值 **60% 以上** |
| 逃生技能 | 修正值 **80% 以上** |
| 打开新世界的大门 | 修正值 **60% 以下**（"修正烤箱时什么都不要做"） |

### 实测修正：这条的两个结论

> ⚠️ 本节原先写的"QTE 不卡主线"是**错误推断**。玩家实测确认：**修正值低于 60% 会无限循环该小游戏**，必须达标才能继续。

- 🔴 **QTE 确证卡主线**。我此前从攻略里"修正烤箱时什么都不要做 → 打开新世界的大门"推出"什么都不做也能过"，这是**误读** —— 那条成就只说明"低分状态本身有成就"，不代表低分能推进剧情。
- 🔴 **QTE 同时卡全成就**：`逃生技能`（80%+）这类成就直接绑在操作精度上。
- 🟡 操作形式经玩家确认：**在屏幕上找散落的正确选项 + 节奏判定**。这与资产完全吻合 —— `QTEPanel.Text1~5` 就是散落的 5 个候选陈述（你不是时光机器 / 你是烤箱 / …），`targetTimings` + `Perfect` + `spritePerfect` + `AccumulateTimingValue` 是节奏判定，`CheckScore` 判定 60%/80% 门槛。
- ✅ **可解性**：`QTEUI` 是自成一体的子系统（挂在 `QTEPanel` 上，配 `QTEScoreComponent`），替代通道只需在 `CheckScore` / `FinishQTE` 层面介入，**实现面很窄**。

---

## 修订后的结论

| 目标 | 可行性 | 说明 |
|---|---|---|
| **听懂全部剧情** | ✅ 可行，需 TTS | 9414 行中文剧本已导出；**但只有莉莉丝有配音**，其余台词必须靠 TTS 补全 |
| **独立通关（不走全成就）** | 🔴 **当前不可行，需改两处** | ① 选项必须鼠标点 → 需补键盘导航与旁白；② **烤箱 QTE 必须 ≥60%，否则无限循环** → 需替代通道 |
| **全成就 / 100%** | 🔴 需额外特化 | 上述两项 + QTE 80% 门槛（`逃生技能`）+ 标题拖拽 + Konami 秘技 + 设置页点字 + 挂机 20~30 分钟（成就「再见」） |

**优先级排序**（按投入产出比，已按实测结果重排）：

1. **L0 离线全剧本导出** —— ✅ 已完成（见下）
2. **挂钩 `ITextPrinterManager` 做全量 TTS 旁白** —— **最高优先级**。因为只有莉莉丝有配音，这条从"可选"变成"没有它游戏根本读不懂"
3. **烤箱 QTE 的替代通道** —— **主阻断**，性价比很高：QTE 是独立子系统，只需在 `CheckScore`/`FinishQTE` 介入，不必动游戏逻辑
4. **选项的键盘导航 + 旁白** —— **第二个硬阻断**，工程上不难（选项是标准 `LabeledButton` 同族），但必须做
5. **隐藏交互清单化**（标题拖拽 / Konami / 设置点字 / 挂机）—— "知识型"障碍，做一份可朗读提示即可
6. **渐隐选项的听觉倒计时** —— 若渐隐确实只出现在 QTE 内，此项可并入第 3 项

---

## 本次产出的可用数据

| 路径 | 内容 |
|---|---|
| `script_zh/_ALL_zh.tsv` | **9414 行中文剧本**（文档名 / 行号 ID / 原文），L0 成果 |
| `script_zh/*.zh.txt` | 按章节拆分的剧本，共 39 个文档 |
| `script_zh/_script_decode.txt` | 20 个编译后 Script 资源的原始还原 + 作者自定义指令集 |
| `script_zh/_qte_identifiers.txt` | QTE / 计时 / 计分相关标识符全表 |
| `controls_out.txt` / `drag2_out.txt` / `drag3_out.txt` / `title_out.txt` | 控件清单、EventTrigger 宿主与方法名、标题界面组件结构 |

**从剧本里还原出的作者自定义 Naninovel 指令集**（这是做旁白时要逐个处理的控制词汇）：

```
@PrintText  @Goto  @Stop  @Wait  @WaitForInput  @HidePrinter  @HideActors  @HideAllActors
@HideAllCharacters  @Spawn  @AddChoice  @AppendLineBreak
@ModifyTextPrinter  @SetControlPanelState  @ModifyBackground  @ModifyCharacter  @ModifyCamera
@SetCustomVariable  @Achievement  @Unlock
@PlayBgm  @StopBgm  @PlaySfx  @ShakeCharacter  @ShakePrinter
```

其中 `@Achievement`（9 次）与 `@Unlock`（7 次）说明**成就是剧本直接驱动的**——这对无障碍是好消息：可以精确知道"哪一行台词触发了哪个成就"，做旁白时顺带播报。

> 注：`UnlockableTrigger` ×8 的宿主是 `MovieSlot1~4`（影像鉴赏的 4 个槽位），与标题界面无关。

---

## 配音覆盖审计（离线算出，量化 TTS 工作量）

因为本作的语音片段**以剧本行号命名**（`~122765bd` 等 865 个），"哪句有配音"可以**完全离线算出来**，不必运行时反查：

| 指标 | 数值 |
|---|---|
| 剧本去重后行数 | **2062** 行（此前说的 9414 是含 14 语言副本的重复计数） |
| 按行号精确命中配音 | **825** 行（40.0%） |
| 按台词文本命中 | 16 行（0.8%） |
| **未命中 → 必须 TTS** | **1221 行（59.2%）** |
| 剧本总字数 | 35040 字 |
| **需 TTS 字数** | **20090 字（57.3%）** |

**这组数字与玩家回答完全吻合**。未命中的样本构成：

- 旁白与哲学引文：`<i>「某些蛋糕的存在不为人现实地知晓……」</i>`
- 主角（Player）台词：`又见面了，莉莉丝。` / `莉莉丝？` / `准备什么？` / `好啊。`
- 章节标题：`第一章：甜蜜的蛋糕时间`
- 选项文本：`是` / `不是`
- 擦除名字的占位符：`█，████。`

即：**莉莉丝之外的一切（约占 57% 的字数）都没有声音**。这既印证了"只有莉莉丝有配音"，也把 TTS 旁白的范围精确框定了 —— 明细逐行列在 `配音覆盖审计.txt`。

> 顺带说明为什么这条能离线算：TransparentHer 那个模组是**运行时反查** `DialogueScene.VoiceFilename` 是否为空来判断有无配音；本作更省事，行号 ↔ 音频文件名直接对应，**离线即可生成"哪句要读"的清单**，连反查逻辑都不用写。

---

## 可复用资产：TransparentHer 无障碍模组

你已有的 `D:\DSHWorkBase\transparenther_a11y`（= `github.com/Nornageste-mh/TransparentHerForAccessibility`）是一套**已实机验收**的读屏模组，本作要解决的问题与它**高度同构**：

| 本作的难点 | TransparentHer 的现成解法 |
|---|---|
| 选项必须鼠标点 | 数字键 `1`-`9` 选择，反射调 `OnReplyButtonClicked` / `OnSelectionButtonClicked` |
| QTE 选项"散落在屏幕上" | README 明确写了"游戏原本的选项按钮**按屏幕坐标散落摆放，读屏无法定位**" —— 同一类问题，同一套解法 |
| 限时选择 | 延长倒计时 + 「沉默」键（`0`）；**关键心得：限时倒计时是"一条剧情分支"而非计时器，设成永不超时等于删掉这个选项** |
| 只有部分角色有配音 | 判定有无配音后，有配音的只放语音并打断朗读 |
| 菜单/存读档/设置只能鼠标 | `UiNav.cs` 每帧扫 `Selectable` 的键盘导航模型 |
| 界面文字被画成图片 | `UiNav.NameAlias` 按 Unity 对象名做中文映射 |

**可直接搬的（与游戏无关）**：`Speech.cs`（Tolk → NVDA → SAPI 三级后端调度，331 行）、`Nvda.cs`、`Sapi.cs`（纯 P/Invoke 直调 `ISpVoice`）、打包脚本、以及 README 里那份踩坑清单（大多数是通用的 Unity/BepInEx 教训，比如"绝不对迭代器方法用 Prefix 返回 false"、"界面重扫不能定时否则原生崩溃"）。

### ⚠️ 但有一个决定性断层：**Mono → IL2CPP**

| | TransparentHer | 本作 |
|---|---|---|
| Unity | 2022.3.43f1c1 | **2022.3.62f2c1** |
| 脚本后端 | **Mono** | **IL2CPP** |
| BepInEx | **5.4.23.5**（Mono） | 需 **BepInEx 6 (IL2CPP)** + Il2CppInterop |

后果：

- `[HarmonyPatch(typeof(DialogueSceneManager), "StartTyping", new[]{typeof(string)})]` 这种**直接引用游戏类型**的写法，在 IL2CPP 下必须先由 Il2CppInterop 从 `global-metadata.dat` 生成代理程序集，再对这些生成类型挂 patch；
- 那边的反射调用要换成 IL2CPP 反射（或直接调 interop 方法）；
- `UiNav` 每帧扫 `Selectable` 的思路可沿用，但对象名映射要整个重做（两个游戏的对象树完全不同）。

好消息：**本作元数据未加密**，生成 interop 程序集没有障碍；而且 BepInEx 6 的 HarmonyX 在 API 层面与 5.x 基本一致，`Speech`/`Nvda`/`Sapi` 三个文件几乎可以原样搬。**所以这不是"从零开始"，而是"换一个加载器 + 重做挂载点映射"。**

---

## 实测已结清 / 仍待验证

**已由玩家实测结清：**

- ~~QTE 是否卡主线~~ → **卡**，<60% 无限循环
- ~~选项能否纯键盘选择~~ → **不能**，必须鼠标
- ~~打字环节~~ → **不存在**
- ~~渐隐选项在哪~~ → **只服务 QTE**（全流程仅烤箱一处需要反应，普通剧情选项不会超时）
- ~~纯听觉能否跟上剧情~~ → 除未配音部分与 QTE 外可以，仅存的画面信息均为玩梗
- ~~配音覆盖边界~~ → **离线审计已算出**：2062 行中 825 行有配音，**1221 行需 TTS**

**仍待验证（需要动手阶段才能查，均为实现细节，不影响可行性结论）：**

1. **QTE 60% 门槛的判定点** —— 落在 `CheckScore` 还是 `FinishQTE`？`targetTimings` 的判定窗口多宽？（决定替代通道怎么改）
2. **选项面板的键盘导航缺口在哪** —— `InputManager` 有 `Horizontal`/`Vertical`/`Submit`/`Cancel`，但选项面板没接上，需查 `ChoiceHandlerPanel` 的选中逻辑
3. **BepInEx 6 IL2CPP 能否顺利生成本作的 interop 程序集** —— 这是**唯一的高风险项**，详见 `外部先例与技术路线.md`

**已由第三方资产 dump 补齐（原列为待查）：**

- ~~标题界面拖拽由哪个脚本处理~~ → **是标准 uGUI `Slider`**（`TitleUI/LogoPanel/Image_Title/Slider`），可用方向键驱动，`UiNav` 现成支持
- ~~`QTEPanel` 的内部结构~~ → 5 个 TMP 文本 + 运行时按钮容器 + 计分面板；判定三档 `Miss/Good/Perfect`
- ~~`ErasePanel` 的擦除机制~~ → `ErasePanel → Image / Controller / Image_Frame / Image_Mask`，是**遮罩涂抹式**交互（非计时，按你的说法不影响主线）
- ~~`FakeED` 的演职员表是不是图片文字~~ → 是**结构化文本 UI**（`SubtitleUI/FakeED/node/CAST/1..N/{left,middle,right}`），可朗读；`SubtitleUI/VocalConcert` 下另有 `01~13` 段字幕位
- ~~`BacklogFakeUI`（假回想面板）结构~~ → 含 `CustomMessage1~13`，共 13 条假消息

---
*本文档只读分析游戏文件，未做任何写入或修改。*
