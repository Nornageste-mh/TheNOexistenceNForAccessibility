# TheNOexistenceNForAccessibility

本仓库是**专门针对 Steam 游戏《不/存在的你，和我》（The NOexistenceN of you AND me，
AppID 2873080）** 的屏幕阅读器辅助补丁 —— 一个游戏内 BepInEx 6 插件，
通过 NVDA / Tolk / Windows SAPI 朗读屏幕上的文字。

> ## 🎮 关于本作
>
> | | |
> |---|---|
> | 中文名 | **不/存在的你，和我** |
> | 英文名 | The NOexistenceN of you AND me |
> | 开发 | **0x0Real Studio** |
> | 发行 | **Nino Games** |
> | Steam | [AppID 2873080](https://store.steampowered.com/app/2873080/) |
> | 发售 | 2024 年 12 月 15 日 |
>
> 这是一款互动视觉小说（Elringus Naninovel 框架），
> 主角**全程没有一句文本台词** —— 她/他的所有话都在选项框里。
>
> **这个补丁是玩家自制的第三方工具，与 0x0Real Studio 和 Nino Games
> 没有任何关系，也没有得到他们的授权或背书。**
> 游戏的著作权属于他们，本仓库只发布自己的补丁代码，不含任何游戏资源。

---

> ## 致 0x0Real Studio 与 Nino Games
>
> 我们欠两位一句道歉。
>
> 做这个补丁的过程中，我们反编译了你们的游戏、解出了剧本与资源，
> 还把一部分分析笔记放进了公开仓库 —— 那些笔记里带着剧情细节和台词原文。
> **这些都没有事先问过你们。**
>
> 技术上这些步骤是必要的（IL2CPP 的游戏没有别的入手点），
> 但**"必要"不等于"可以不打招呼"**。我们做这件事的出发点不是不尊重这部作品，
> 恰恰相反 —— 是因为觉得它值得被更多人完整地玩到，
> 才想让读屏玩家也能走完一遍。可方式确实越界了：
> **一个讲"存在与不存在"的故事，它的情节不该由我们在公开场合替它讲。**
>
> 已经做的补救：
>
> - 仓库与发布包**不含任何游戏资源**，只有我们自己的补丁代码和文档
> - 发给玩家的文档里已经清掉了剧情内容（见 `CHANGELOG.md` 的「清了一遍文档里的剧透」）
> - **带剧情的分析笔记已从版本库里移除**，留在本地、每份加剧透警告；
>   数据表与提取产物同样不入库
> - **如果你们认为任何部分不妥，请联系我们 —— 我们会立刻调整，或者整体下架**
>
> 也请读到这里、并且用得上这个补丁的玩家：
> **它让你能玩上这款游戏，不是让你不必买这款游戏。请去买一份正版。**
> 这部作品是 0x0Real Studio 和 Nino Games 的。

> ## ⚠️ 郑重警告（请务必阅读）
>
> - 本项目是 **Vibe coding 产物**（AI 辅助生成），**非官方**，
>   与开发组 **0x0Real Studio** 和发行商 **Nino Games** **没有任何关系**。
> - **请务必支持正版：本辅助仅面向已在 Steam 购买《不/存在的你，和我》的玩家。**
>   **强烈要求每一位使用者通过 Steam 购买正版游戏。** 我们坚决反对任何形式的盗版、
>   破解、未授权传播；请勿将本工具用于协助获取或游玩盗版副本。
>   **0x0Real Studio 和 Nino Games 的劳动成果值得被正当地支持。**
> - 本项目**不保证可用、不保证稳定**，代码可能存在各种问题（兼容性、稳定性、安全性等），
>   **无任何维护承诺**。使用风险自负，仅供个人学习/研究参考，请勿用于商业或分发牟利。
> - **建议在完全理解代码的前提下再使用**，自行承担一切后果。
> - 本仓库**不包含任何游戏资源**，只发布运行时补丁代码与文档。
> - **如您是 0x0Real Studio 或 Nino Games 的成员**，或本作的版权方，
>   认为本仓库有任何不妥，请联系我们，我们会立即配合调整或移除。

---

## 说明

本辅助的作用：让使用读屏软件的玩家，也能完整通关《不/存在的你，和我》。

- 朗读**没有配音**的剧情文本（旁白、以及大量没有配音的配角），并**先报说话人名**
- 有配音的台词只放游戏语音、不重复念，避免两路声音打架
- 朗读**选项**，并用数字键 `1`-`9` 选择
- ruby 旁注双读：`正文（实为：旁注）`
- 主菜单 / 设置 / 回想（History）的键盘导航与朗读
- 自动通过 QTE（那段限时反应小游戏画面上没有任何可听线索）
- 加载时播报「正在加载」；`退格` 重读上一段；`0` 立刻闭嘴

**说话人的名字按游戏里显示的原样念。** 游戏故意把一个名字显示成 `???` 的时候，
补丁就念「未知」——**我们不去猜那是谁，也不会拿别的名字替上去**。
猜名等于替作者把包袱抖了，这是补丁里少数几条"宁可少给信息"的地方之一。

游戏版本：Unity 2022.3.62f2c1，**IL2CPP** 后端，视觉小说框架 **Elringus Naninovel**。
补丁跑在 **BepInEx 6.0.0-be.788** + **Il2CppInterop 1.5.3**（含一处上游补丁）之上。

> **为什么选项这么要紧**：本作主角**没有一句文本台词** ——
> 全部台词都以选项的形式出现，全剧 625 条以上、8000 多字。
> 对读屏玩家来说，选项框不是"分支选择"，是"主角的全部对白"。

---

## 安装（仅限正版玩家）

**最快的装法**：到 [Releases](../../releases) 下载补丁 zip，解压后把里面的东西
**整体**拷进游戏根目录（有 `TheNOexistenceNofyouANDme.exe` 的那一层），
提示"是否合并/替换"时选**是**。zip 内容就是下文的 `mod\package\`。

补丁由 BepInEx 在运行时挂载，**不修改任何游戏文件**，所以安装就是「拷文件」。

拷完游戏根目录应该多出这些（只列补丁相关的）：

```
TheNOexistenceNofyouANDme.exe
winhttp.dll                 ← 新增（UnityDoorstop）
doorstop_config.ini         ← 新增
.doorstop_version           ← 新增
changelog.txt               ← 新增（BepInEx 自带说明）
BepInEx\                    ← 新增，整个文件夹
  core\
  plugins\
    NoExistenceA11y.dll
    nvdaControllerClient.dll
dotnet\                     ← 新增（BepInEx 6 需要的 .NET 运行时）
licenses\                   ← 新增（第三方组件的许可证与声明，**别删**）
安装说明.md
常见问题.md
```

三件容易踩的事：

- **不要只拷一部分。** `BepInEx\core\` 里三十多个 DLL 一个都不能少，
  `dotnet\` 也是 —— BepInEx 6 靠它跑。
- **第一次启动会慢。** BepInEx 要按本机游戏生成适配层（`BepInEx\interop\`），
  30 MB 以上的补丁 zip 里**故意不带**这些，因为那是按各人游戏现生成的。
- **别装两个 BepInEx。** 已经装过别的 BepInEx 6 模组的话，只需要两个文件：
  `NoExistenceA11y.dll` → `BepInEx\plugins\`，`nvdaControllerClient.dll` → 游戏根目录。
  **不要**覆盖对方已有的 `winhttp.dll` 和 `BepInEx\core\`。

卸载：删掉上面"新增"的那些即可。只想去掉补丁、保留 BepInEx：删
`BepInEx\plugins\NoExistenceA11y.dll` 就行。
存档在 `%USERPROFILE%\AppData\LocalLow\Nino\TheNOexistenceNofyouANDme\`，不受影响。

> **这个游戏没有存档 / 读档界面**，主菜单只有「继续游戏」—— 引擎层的自动存档是存在的，
> 但游戏故意不给玩家入口。所以**没有"读回上一个状态"这回事**，装之前请先备份
> `NaninovelData\Saves\`（详细的实验注意事项见安装说明）。

详细步骤、按键表、配置逐项说明、故障排查见 [`mod/package/安装说明.md`](mod/package/安装说明.md)，
常见疑问见 [`mod/package/常见问题.md`](mod/package/常见问题.md)。

> 再次提醒：请通过 Steam 购买正版《不/存在的你，和我》后再使用本辅助。
> 游戏由 **0x0Real Studio** 开发、**Nino Games** 发行，著作权归他们所有。

---

## 主要快捷键

| 快捷键 | 功能 |
| --- | --- |
| `1` - `9` | 选项出现时直接选第 1-9 项（不必先用鼠标、也不必先进导航模式） |
| `退格` | 重读最近朗读过的那一段 |
| `0` | 立刻让朗读闭嘴 |
| `Tab` | 进入 / 退出界面导航模式 |
| `↑` `↓` | 上一个 / 下一个控件（会念出它是什么） |
| `←` `→` | 调整滑条 |
| `回车` / `空格` | 导航模式下激活控件；否则交给游戏推进剧情 |
| `Home` / `End` | 第一项 / 最后一项 |
| `PageUp` / `PageDown` | 切换面板组（比如设置页签） |
| `F3` | 开关 QTE 自动点击（切换时会念出状态） |
| `F4` | **默认关闭。** 诊断用剧本跳转，要排查问题才在配置里开 |

**「一段」而不是「一句」是有讲究的**：选项也算重读对象。补丁把整组选项拼成**一段**
念出去（`共 N 个选项。选项 1：…。选项 2：…。按数字键选择。`），
所以按 `退格` 重读的就是整组选项 —— 不用凭记忆回想有几项、分别是什么。
有配音的行也记进来：语音错过了，退格把文本念出来正合适。
记忆只在内存里，退出游戏即清空。

**游戏原生占用的键**（模组不会去抢）：`Ctrl` 按住快进（Naninovel 的跳过键）、
`回车` / `空格` 推进剧情、鼠标左键推进。`退格`、`0`、`F3`、`F4` 都能在配置里改或关掉；
`Tab` 与方向键是固定的 —— 它们只在导航模式里有意义。

---

## 已知问题 / 注意事项

- 通过读取游戏运行时信息工作，**游戏更新后可能失效**（补丁点是按游戏内方法名挂的）
- **朗读顺序可能滞后于画面。** 默认"新台词打断上一句"：读得激进、允许打断，
  长句没念完会被下一句砍断，按 `退格` 翻回来、按 `0` 闭嘴。
  想"一个字都不丢"就把这一项关掉改成排队，代价是朗读会落后于画面
- **有配音的台词不念文本。** 这是设计：TTS 和角色语音叠在一起两边都听不清。
  想听文本按 `退格`
- **加载提示会晚几秒。** 从你按下按钮到游戏把加载画面显示出来，中间有一段
  **屏幕上什么都没有的静默期**，补丁无从得知。详见常见问题
- **选项点不动分两种**：某一项点不动是游戏把它标成不可用了（补丁**故意不点**，
  按 `Tab` 用导航绕过）；整个选项框都不响应是游戏状态坏了 —— 本作没有存读档界面，
  只能重启
- **QTE 是"能过"而不是"满分"**：实测修正值 97%。按钮的渐隐不是靠 `CanvasGroup`
  或 `Image.color.a` 做的，补丁读不到 alpha 变化，97% 完全来自"等 1 秒再点"
- 未覆盖的内容：CG / Spine 画面口述、视频口述影像、未配音台词的 TTS 预生成、
  游戏原生历史回顾面板的朗读（改用导航模式的 `BacklogUI` 白名单实现）
- **字幕（`@subtitle`）走的不是打印器**（`0.1.1.0` 前压根没接上，见 CHANGELOG）。
  本作的片尾 / 唱歌 / 伪 ED 字幕是 `SubtitleUI` 播一段 Animation，
  文字写在预制体的 TMP 节点上、没有任何打印事件，所以补丁原来**一个字都不念**。
  现在由 `Subtitles.cs` 按画面节奏逐行朗读；终章与 ED 字幕**仍未实机跑完整遍**
- **设置界面里"整屏控件都不可用"时改为只读导航。** 例如彩蛋界面「莉莉丝的设置界面」：
  游戏把全部控件置成 `interactable = false`，补丁以前会报「没有可操作的项目」、
  还顺手退出导航模式。现在会把它们**当只读项念出来**（只念不点，按回车只会听到
  「该项当前不可用」），并在进入时先说一句「这一屏的控件当前都不可用」
- 语音朗读依赖所选后端（NVDA / Tolk / SAPI），用 SAPI 兜底时系统里要有中文语音

---

## 构建

```powershell
cd mod
.\release.ps1
```

会编译插件、把 BepInEx 6.0.0-be.788 官方包解开、覆盖打过补丁的
`Il2CppInterop.Runtime.dll`、放进插件与两份文档、拷进 `licenses\`、
跑一遍完整性检查，最后打出 `mod\dist\NoExistenceA11y-<版本>.zip`。
第三方二进制不入库，全靠这个脚本复现。
需要 .NET SDK（本项目用 10.0.301 验证过）。

编译插件本身需要**已经装好 BepInEx 的游戏目录** —— `BepInEx\interop\` 里那套
.Net 程序集是编译时的引用来源（`NoExistenceA11y.csproj` 顶部的 `GameDir`）。

### 构建是可复现的（`csproj` 里那三行别删）

```xml
<Deterministic>true</Deterministic>
<IncludeSourceRevisionInInformationalVersion>false</IncludeSourceRevisionInInformationalVersion>
<EnableSourceLink>false</EnableSourceLink>
```

不加这三行的话，**同一个提交内连续构建两次结果一样，换一个提交再构建产物立刻变** ——
等于没修好。有两条独立通路会把「当前 git 提交」写进产物：

1. `IncludeSourceRevisionInInformationalVersion`（SDK 默认 `true`）——
   把提交号写进 `AssemblyInformationalVersion`，DLL 里直接搜得到 `0.1.0.0+<40 位十六进制>`
2. `EnableSourceLink`（SDK 默认 `true`，藏在有效属性里，`csproj` 里根本看不见）——
   把提交写进 PDB，PDB 的内容哈希又会进 DLL 的 debug 目录（PDB GUID）和确定性 MVID

**只关第 1 条是不够的**，这正是上一个仓库（TransparentHer）踩的坑：
它以为自己修好了，一提交才发现产物还是变。我们又不发布 PDB，
Source Link 在这个项目里没有任何收益。

修完之后实测：同一目录、同一提交连续两次 `--no-incremental` 构建，
MD5 都是 `EE5E955912E72D2E83F8E076B10BEFFF`；
换个目录构建仍有 72 字节差异，但那 72 字节全落在 PDB 身份字段上
（`RSDS` 块与 GUID），代码、字符串常量、版本信息**逐字节相同**
（上一个仓库量到的也是 72 字节，同一个原因）。

> **中文注释的 `.ps1` 必须存成 UTF-8 带 BOM。** Windows PowerShell 5.1 对没有 BOM
> 的脚本按系统代码页(GBK)读取，中文注释会变乱码并直接语法错误。
> 同理 C# 源码是 UTF-8 **无** BOM，`csproj` 里因此显式写了 `<CodePage>65001</CodePage>`，
> 否则 Roslyn 会按 GBK 读，中文字面量全成乱码。

### 目录结构

```
.
├─ mod/
│  ├─ release.ps1               构建 + 组装发布包
│  ├─ src/NoExistenceA11y/
│  │  ├─ Plugin.cs              补丁、朗读管线、说话人、配音白名单、配置
│  │  ├─ UiNav.cs               界面键盘导航与朗读（含排序、可见性、激活）
│  │  ├─ Choices.cs             选项框：整组播报 + 数字键直选
│  │  ├─ Qte.cs                 限时反应小游戏（QTE）的自动点击
│  │  ├─ LoadingWatch.cs        加载播报
│  │  ├─ UiVis.cs               可见性统一判定（CanvasGroup）
│  │  ├─ TextProc.cs            ruby 双读、标签剥离、行键与指纹
│  │  ├─ Speech.cs              NVDA / Tolk / SAPI 后端调度
│  │  ├─ Nvda.cs                NVDA Controller Client 封装
│  │  ├─ Sapi.cs                Windows 内置语音（纯 P/Invoke 直调 ISpVoice）
│  │  ├─ VoiceLineIds.cs        849 个有配音的行键（生成物）
│  │  ├─ SpeakerNames.cs        50 个 AuthorId → 中文显示名（生成物）
│  │  └─ Diag.cs                诊断用剧本跳转
│  ├─ package/                  进发布包的东西（文档 + 两个 DLL）
│  │  ├─ 安装说明.md            ← 用户文档，先看这个
│  │  └─ 常见问题.md
│  ├─ licenses/                 第三方许可证全文 + NOTICES + Il2CppInterop 的修改
│  └─ evidence/                 实机日志原件（**不入库**，见「分析笔记」）
│
└─ （以下目录不入库，见 .gitignore）
   probe/         BepInEx / Il2CppInterop 的拆解、实机探针插件、资源探查脚本
   script_zh/     从游戏提取的剧本文本（版权内容）
   ext_ref/       从游戏资源里解出来的 prefab
   tools/ApiDump  自写的 IL2CPP API 转储小工具
```

### 模组做了什么

| 功能 | 挂载点 |
|---|---|
| 朗读剧情文本 | `Naninovel.UI.RevealableText.set_Text`（Harmony Postfix） |
| 判定"这一行到底该不该念" | `ITextPrinterManager.OnPrintTextStarted` 事件 |
| 朗读选项 | `Naninovel.UI.ChoiceHandlerPanel.AddChoiceButton` + 逐帧扫 `ButtonsContainer` |
| 数字键 1-9 选择选项 | 直接激活对应选项按钮 |
| 判定有无配音 | `VoiceLineIds.cs` 表，键 = 剧本/行号 |
| 说话人名 | `SpeakerNames.cs`，取自游戏 `CharacterNames` 文档 |
| QTE 自动点击 | `FindObjectOfType<Naninovel.UI.QTEUI>()`（按类型，不猜对象名） |
| 界面键盘导航 | 无挂载点，`UiNav` 逐帧扫描 `Selectable`；白名单界面里连 `TMP_Text` 一起收 |
| 朗读字幕（`@subtitle`） | 无挂载点：逐帧读 `SubtitleUI` 下**此刻在画面内**的 TMP 文字 |
| 朗读不经过打印器的正文 | 无挂载点：配置「不经过打印器也朗读的面板」，按层级路径放行 |
| 「整屏控件都不可用」时仍可听 | 无挂载点：`UiNav` 只读兜底（只念不点） |
| 加载播报 | `Naninovel.UI.LoadingPanel` 可见性 + 名字带 `load` 的 Unity 场景 |
| 导航时屏蔽游戏输入 | `Naninovel.IInputManager.ProcessInput`（引擎自带的总闸，可逆） |
| 隐藏黑色控制台 | `GetConsoleWindow` + `ShowWindow`（运行时，不动 `BepInEx.cfg`） |

### 几条踩过的坑（改之前请先读）

**IL2CPP 有三类"静默失败"** —— 全都不报错，只是行为不对。本项目每一类都踩过一次：

- **`s.transform is RectTransform` 恒为 `false`，`as Toggle` 恒为 `null`。**
  Il2CppInterop 给出的是包装对象，托管类型判断不成立。必须走 `TryCast<T>()`。
  这个坑让 Tab 导航一度完全失灵（扫到 0 个控件）。
- **`rt.GetWorldCorners(managedVector3[])` 写不回托管数组。**
  托管数组传给原生方法会被**拷贝**，原生写入的结果不回流，坐标恒为 0 —— 也不报错。
  必须用 `Il2CppStructArray<Vector3>`。
- **`GameObject.Find("QTEPanel")` 这类猜对象名。** 三轮实机日志里 QTE 一次都没被
  识别到（`QTE` 行数始终为 0）、却什么都不说。改成引用游戏自己的类型，用
  `FindObjectOfType` 定位。类型引用是惰性解析的：游戏改版删掉该类只会让这一个功能
  停用并记一行日志，不会拖垮整个插件。

同类的还有两条（`0.1.1.0` 查字幕时踩到的）：

- **代理对象上的 `GetType()` 只会报"声明类型"。** 想把剧本行表印出来，
  写 `c.GetType().Name` 得到的是**声明类型** `Command`（行则是 `ScriptLine`），
  所有 `@PrintText` / `@CustomSubtitle` 全变成同一个名字，等于什么都没说。
  要拿真实类名得直接问 IL2CPP：
  `IL2CPP.il2cpp_object_get_class(ptr)` + `IL2CPP.il2cpp_class_get_name_(cls)`。
- **`Script.Lines` 是 Il2Cpp 的 `IReadOnlyList` 代理，`.Count` 和 `foreach` 都点不出来**
  （接口代理上只暴露了那一个接口自己的成员，编译期就报 CS1061 / CS1579）。
  取行表要直接用内部的 `lines` 数组（`Il2CppReferenceArray<ScriptLine>`），
  它有 `Length` 也有索引器，行号就是下标。

另外几条：

- **`List.Sort` 的比较函数不能拿"近似相等"当分支。** 原来的写法是
  `if (Mathf.Abs(a.Left - b.Left) > 0.01f)`，不满足传递性；内省排序遇到不满足
  传递性的比较函数，结果是**任意的**（照样不报错）。改成严格字典序：
  行 → 左 → 渲染路径。
- **世界坐标不等于屏幕像素。** 本作这块画布的世界单位极小（顶≈4，屏幕上是 1000 上下），
  拿世界坐标按像素分档会把所有控件压进同一档，排序随即退化成按层级排 ——
  症状是"按下光标反而跳到画面上方"。先过
  `RectTransformUtility.WorldToScreenPoint` 换算再分档。
- **`RevealableText` 是复用的，文本会在面板显示之前就写进去**（载入存档 / 加载场景 /
  预载剧本都会）。只挂 `set_Text` 的话，玩家根本看不到的文本会被念出来 ——
  **实测在 load 场景念出过玩家当时完全不该听到的内容。**
  必须要求同时收到"开始打印"事件、并且面板确实可见。
- **Naninovel 的面板靠 `CanvasGroup` 显隐**：`alpha = 0` 时 `GameObject` 仍然 active、
  射线仍然命中。只看 `activeInHierarchy` 拦不住隐藏面板，所以有了 `UiVis`。
- **无障碍层绝不能扩大可达范围。** 曾经写过一条"自愈兜底"：严格筛选后如果为空，
  就回退成"全收"。结果隐藏面板重新变成可达 —— 实测暴露 93 个控件 / 18 组，
  包含被游戏藏起来的**整套存档界面**。这条兜底已删除。
  原则是：无障碍层只能让**本来就看得见、本来就能点**的东西变得可键盘操作。
- **`ScriptNavigatorUI` 是开发者工具，别碰。** 那是"直接跳转到任意剧本"面板，
  按下去会绕开正常流程把游戏扔进裸剧本 —— 实测结果是黑屏 + 有音乐 + 无任何响应，
  只能 Alt+F4。已列入默认排除清单。
- **不要试图改存档的 `playbackSpot` 跳到剧本中间。** 那会跳过
  `@char` / `@modifyCharacter` 的立绘分层装配命令，角色立绘直接错乱（实测踩过）。
  要跳就交给 `IScriptPlayer.PreloadAndPlayAsync` 从剧本**开头**播，配合 `Ctrl` 快进。
- **`AddChoiceButton` 那一刻选项文字还没写进去。** 挂在这一刻直接读会读到空字符串；
  改成逐帧扫 `ButtonsContainer`，并且容器第 0 个子物体是**未激活的占位符**，要滤掉。
- **别用 PowerShell 的文本 cmdlet 改源码。** `Get-Content -Raw` 在 Windows PowerShell 5.1
  下按 ANSI 代码页读取无 BOM 的 UTF-8，中文会变乱码。

---

## 如何复现分析

游戏是 IL2CPP 的，没有可直接反编译的 `Assembly-CSharp.dll`。实际走的两条路：

1. **离线读资源**：用 **UnityPy** 直接读 `global-metadata.dat` 与资源包，
   定位剧本、音频与 prefab。`gen_voicelist.py` 就是这么把两张表生成出来的。
2. **看运行时**：BepInEx 6 首次启动时用 Il2CppInterop 按本机游戏生成一整套托管程序集
   （落在 `BepInEx\interop\`），那套程序集既是我们编译时的引用来源，
   也是查游戏逻辑的入口；配 `tools/ApiDump` 可以把 API 转储成文本再翻。

```powershell
pip install UnityPy
python gen_voicelist.py      # 指向游戏安装目录 -> VoiceLineIds.cs / SpeakerNames.cs
```

`probe/` 下有实机探针脚本（一个单独的 BepInEx 插件，把运行时的类型、控件树、
打印事件转储成日志），本项目几乎所有"游戏里到底发生了什么"的结论都出自它。

生成的 `probe/`、`script_zh/`、`ext_ref/` 以及根目录几个 `*_out.txt`
都含游戏版权内容或体积很大的中间产物，**已被 .gitignore 排除，请勿提交或分发**。

### 分析笔记（**故意不入库**）

动手之前和动手途中的调研都留在**本地**，按时间顺序是这五份：

```
无障碍可行性验证 → 外部先例与技术路线 → 探针验证结果
                 → 控件类型核对与QTE评估 → 语音层设计
```

**它们在 `.gitignore` 里，不进版本库 —— 这是故意的。**

那几份笔记里有角色的真名与身份对应关系、游戏用来欺骗玩家的那套 UI 机制、
以及从运行时日志抄出来的台词原文。它们记录的是"这个游戏为什么能/不能做无障碍"
的推导过程，对将来维护的人有价值；但**没有任何理由让准备正常游玩的人读到**。
每份开头都有一段剧透警告，提醒别把里面的内容复制进发给玩家的文档。

同一类东西还有：`mod/evidence/` 下的实机日志（`run1_reading.log`、`qte_session.log`）、
根目录从游戏解出的四张数据表（`台词与角色对照.txt` 等）、
以及 `script_zh/` 与 `ext_ref/` —— 要么含游戏原文，要么含剧情，**一律不入库**。

> **这就是这个项目踩过的坑**：README 曾经为了让读者相信"补丁不剧透"，
> 先把包袱抖了。完整复盘见 `CHANGELOG.md` 的「清了一遍文档里的剧透」。

---

## 合规说明

**《不/存在的你，和我》的著作权属于 0x0Real Studio 与 Nino Games。**
本仓库不是他们的项目，也没有得到他们的授权或背书。
下面的说明只是"我们做了什么、没做什么"，不构成对他们的任何主张。

- 在绝大部分国家和地区，为自己使用而修改你**合法拥有**的软件通常是允许的；
  但把**修改后的软件提供给第三方**，以及**绕过技术保护措施**，
  在绝大部分国家和地区都是**不被允许的**。
  各国规定不尽相同，请以你所在地的法律为准。
- 本模组因此**不包含任何游戏资源**，只发布补丁代码；发布包（BepInEx 运行时 +
  补丁 + 说明）通过本仓库的 Releases 提供，其中**不含游戏本体、不含任何游戏资源**。
- 模组不绕过任何技术保护措施，不修改、不替换、不再分发游戏文件。
- 补丁里的两张数据表（`VoiceLineIds.cs` / `SpeakerNames.cs`）是从游戏资源里
  离线解出来的**行号与角色 ID**，不含任何剧本原文。提取脚本在本仓库里，
  但提取产物（`script_zh/`、`*_out.txt`）不入库。
- **再次请求：请通过 Steam 购买正版。** 这是一款独立游戏，
  0x0Real Studio 和 Nino Games 的劳动成果值得被正当地支持 ——
  本补丁存在的意义是让更多人**买得起、玩得上**，不是替代购买。

---

## 许可

补丁代码与文档：见仓库内说明。

### 发布包里的第三方组件

发布包里除了我们自己的 `NoExistenceA11y.dll`，还有二十来个第三方二进制 ——
BepInEx 官方 zip 里**一个许可证文件都不带**，所以这些得我们自己列。

**下表的版本号除了凭记忆，还另从随包分发的 DLL 的 `FileVersion` 里逐个核对了一遍**：

| 组件 | 上游 | 版本 | 许可证 |
|---|---|---|---|
| `BepInEx.Core` / `.Preloader.Core` / `.Unity.IL2CPP` / `.Unity.Common` | [BepInEx/BepInEx](https://github.com/BepInEx/BepInEx) `master` | 6.0.0-be.788 | **LGPL-2.1** |
| `Il2CppInterop.Runtime` / `.Common` / `.Generator` / `.HarmonySupport` | [BepInEx/Il2CppInterop](https://github.com/BepInEx/Il2CppInterop) | 1.5.3 **+ 上游 PR #277** | **LGPL-3.0** |
| `0Harmony.dll` | [BepInEx/HarmonyX](https://github.com/BepInEx/HarmonyX) | 2.10.2 | MIT |
| `Mono.Cecil` / `.Mdb` / `.Pdb` / `.Rocks` | [jbevain/cecil](https://github.com/jbevain/cecil) | 0.11.4 | MIT |
| `MonoMod.RuntimeDetour` / `.Utils` / `.ILHelpers` / `.Backports` | [MonoMod/MonoMod](https://github.com/MonoMod/MonoMod) | 22.7.31.1 / 1.1.x | MIT |
| `Cpp2IL.Core` / `LibCpp2IL` / `WasmDisassembler` / `StableNameDotNet` | [SamboyCoding/Cpp2IL](https://github.com/SamboyCoding/Cpp2IL) | 2022.1.0-dev | MIT |
| `AsmResolver` / `.DotNet` / `.PE` / `.PE.File` | [Washi1337/AsmResolver](https://github.com/Washi1337/AsmResolver) | 6.0.0-beta.5 | MIT |
| `AssetRipper.CIL` | [AssetRipper.CIL](https://github.com/AssetRipper/AssetRipper.CIL) | 1.2.2 | MIT |
| `AssetRipper.Primitives` | [AssetRipper.Primitives](https://github.com/AssetRipper/AssetRipper.Primitives) | 3.2.0 | MIT（含 uTinyRipper 一行） |
| `Disarm.dll` | [SamboyCoding/Disarm](https://github.com/SamboyCoding/Disarm) | 2022.1.0 | MIT |
| `Iced.dll` | [icedland/iced](https://github.com/icedland/iced) | 1.21.0 | MIT |
| `Gee.External.Capstone.dll` | [ds5678/Capstone.NET](https://github.com/ds5678/Capstone.NET) | 2.3.2 | MIT |
| `SemanticVersioning.dll` | [adamreeve/semver.net](https://github.com/adamreeve/semver.net) | 2.0.2 | MIT |
| `dobby.dll` | [BepInEx/Dobby](https://github.com/BepInEx/Dobby) | 1.0.5 | Apache-2.0 |
| `winhttp.dll` | [NeighTools/UnityDoorstop](https://github.com/NeighTools/UnityDoorstop) | 4.5.0 | **LGPL-2.1** |
| `nvdaControllerClient.dll` | NV Access Controller Client | API 2.0 | **LGPL-2.1** |
| `dotnet\`（约 180 个文件） | [BepInEx/dotnet-runtime](https://github.com/BepInEx/dotnet-runtime) | 6.0.7 mini-coreclr | MIT |

完整版权行与各许可证全文见 [`mod/licenses/`](mod/licenses/)。

### 两件必须说明的事

**一、`Il2CppInterop.Runtime.dll` 是被修改过的库，而它是 LGPL-3.0 的。**

v1.5.3 发行版里没有 Unity 2022.3 x64 的 `Class::Init` 签名匹配项，注入会失败，
而本作正好是 Unity 2022.3.62f2c1。所以要带上上游自己的一个未合并 PR
（[#277](https://github.com/BepInEx/Il2CppInterop/pull/277)，提交 `f0634ef`）。

LGPL-3.0 第 4 条要求：分发被修改过的库时，必须**显著声明该库已被修改**、
说明修改日期，并让接收者能拿到对应源码。修改内容、提交号、复现步骤、
以及从 `v1.5.3` 到该提交的**完整 diff**，都在
[`mod/licenses/il2cppinterop-pr277.patch`](mod/licenses/il2cppinterop-pr277.patch)。

改动小得有点意外：相对 v1.5.3 只有 **1 个文件、新增 7 行**，
而且是上游提交的，不是我们改的 —— 我们**没有对 Il2CppInterop 做过任何自己的源码改动**。

**二、BepInEx 6 是 LGPL-2.1，不是 MIT。**

这一条容易搞错，而且方向恰好相反：**BepInEx 5 才是 MIT**
（`Copyright (c) 2018 Bepis`），**BepInEx 6 是 LGPL-2.1**
（`BepInEx - Unity` / `Copyright (C) 2020 BepInEx Team`）。同一个仓库，
`master` 分支是 v6、`v5-lts` 分支是 v5。

（上一个仓库 TransparentHer 恰好在这里踩过：动手前以为 BepInEx 5 是 LGPL-2.1，
逐个版本标签去取 LICENSE 才发现反了。）

LGPL 组件都是以**未修改的独立 DLL** 分发的，走的是"共享库机制"那条路
（LGPL-2.1 §6b / LGPL-3.0 §4d1）：DLL 运行时加载、彼此可替换，
所以**不触发源码分发义务**，但必须附上许可证全文 + 显著声明。
唯一例外是上面第一个 —— 那个是被修改过的。

### 我们不分发的东西

- **不含游戏本体、不含任何游戏资源。** 补丁只读取游戏运行时信息。
- **不含 `BepInEx\interop\` 与 `unity-libs\`** —— 那是首次运行时按本机游戏生成的。
