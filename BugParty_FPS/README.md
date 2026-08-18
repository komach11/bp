# BUG派对 · 第一人称密室搜刮（塔科夫 / 三角洲风格）

Unity 2022 完整工程。**核心循环：搜箱 → 翻找（视野被占的危险窗口）→ 网格取舍 → 撤离才算带走。**

---

## ⚠️ 先读这一条：第一人称否掉了本地同屏

第一人称视角下，每个玩家都需要**独立的视角和独立的屏幕**。这意味着：

| 多人形态 | 俯视版 | 第一人称版 |
| --- | --- | --- |
| 本地四人同屏 | ✓ 可行 | **✗ 不成立** |
| 1 真人 + 3 AI | ✓ 可行 | ✓ **本工程采用** |
| 联网四人 | 未做 | 需要额外开发（你文档里明确不做） |

**本工程按「1 真人（红方）+ 3 AI」实现。** 这是 FPS 下唯一能当天跑起来验证乐趣的形态。如果最终要做真人对抗，需要在这套架构上接 Netcode/Mirror——`PlayerRig` 的输入已经从逻辑里分离出去了，接联网时改动可控，但那是另一个量级的工作。

俯视版工程（`BugParty_SearchRoom`）建议保留。两套可以并行验证，最后选一个。

---

## 一、10 秒上手

1. Unity 2022.3 LTS，新建或打开项目（URP / Built-in 均可）
2. 把 `Assets/BugPartyFPS` 整个文件夹拖进 `Assets`
3. 等编译完成，新建空场景
4. 菜单 **BugPartyFPS ▸ Build Raid Scene**，确认弹窗
5. **按 Play**（鼠标会被锁定，按 `Esc` 解锁）

### 操作（塔科夫 / 三角洲键位）

| 按键 | 功能 |
| --- | --- |
| `W A S D` | 移动 |
| 鼠标 | 视角 |
| `Shift` | 疾跑（快，但噪音半径 16 米，AI 老远就听见） |
| `Ctrl` | 下蹲（慢，噪音半径 2 米，几乎无声） |
| `Space` | 跳 |
| `F` | 搜刮容器 / 打开容器 / 关闭面板 |
| `1`–`6` | 从容器里拿第 N 件 |
| `G` | 一键拿走所有装得下的 |
| `Tab` | 关闭面板 / 取消搜索 |
| `V` 或鼠标左键 | 肘击 |
| `R` | 重开一局 |
| `Esc` | 解锁 / 锁定鼠标 |

---

## 二、四个塔科夫式设计，逐个说明

### 2.1 ★两段式搜刮：真正危险的是「翻包」而不是「读条」

```
按 F  →  读条 3 秒（可移动打断）  →  ★打开搜刮面板  →  挑东西
                                      ↑
                            这里移动被锁死，你是活靶子
```

塔科夫最紧张的时刻不是交火，而是**低头翻包时听见脚步声**。第一人称把这个感觉放大了十倍——俯视版你能看见谁在靠近，FPS 版你只能听见。

面板上有一行闪烁的红字提示「⚠ 翻找时无法移动，注意背后」。这不是装饰，是在教玩家这个机制。

**关键实现**：`RaidConfig.lockMovementWhileLooting`（默认开）。关掉它整套紧张感就没了，但如果测试反馈太挫败可以关。**注意：即使锁了移动，仍然允许转头**——你必须能回头看有没有人来，否则就是纯粹的惩罚而不是博弈。

### 2.2 ★网格背包：体积取舍比数个数有意思

默认 4×2 = 8 格。道具占位不同：

| 道具 | 体积 | 价值 | 密度 |
| --- | --- | --- | --- |
| 大渔网 | 2×2（4格） | 420 | 105/格 |
| 小渔网 | 2×1（2格） | 200 | 100/格 |
| 小刀 | 1×1 | 260 | **260/格** |
| 金色Debug芯片 | 1×1 | **900** | **900/格** ★稀有 |
| 徒手手套 | 1×1 | 80 | 80/格 |

所以「拿一个大渔网还是四把小刀」是真正的决策，而不是随便填格子。**稀有道具（金色芯片、故障松露、空白逮捕令）只占 1 格但价值接近千分**——搜到一个就值得立刻撤离。

不想要这套复杂度？勾上 `RaidConfig.useSimpleCountMode`，立刻退回「最多 N 件」的计数模式，`simpleCapacity` 设成 2 就和俯视版一致。

### 2.3 ★撤离机制：搜到不算赢，带出去才算

时间轴：

```
0s ─── 搜刮阶段 45 秒 ─── 45s ─── ★撤离窗口 15 秒 ─── 60s ─── 结算
                                   传送门开启
                                   站在门里持续 2.5 秒才算成功
                                   被打断 → 进度清零
                                   超时未撤离 → 战利品全部作废
```

这是塔科夫的灵魂。它制造了三个新的博弈层：

1. **贪心 vs 保守**：还剩 20 秒，要不要再搜一个箱子
2. **撤离点埋伏**：AI 也要跑撤离点，两个点是对角布置的，撤离时必然遭遇
3. **打断撤离**：`ExtractionZone` 在玩家硬直期间会暂停进度——在门口把人打一下，他就撤不掉

`RaidConfig.extractHoldTime` 控制蓄力时长，`resetExtractOnLeave` 控制走出去是否清零。

### 2.4 ★背刺：鼓励趁人翻包时从背后阴他

`MeleeAction.IsBackstab()` 判断攻击者是否在目标身后半球（点积 < -0.15）。背刺的回报明显更高：

| | 普通命中 | 背刺 |
| --- | --- | --- |
| 硬直时长 | 1.1 秒 | **2.2 秒**（×2） |
| 打落件数 | 1 件 | **2 件** |
| 打落哪件 | 最新拿到的 | **最值钱的** ★ |
| 视角晃动 | 14 | 22 |

打落的东西变成 `DroppedLoot` 落在地上，**任何人走过去都能捡**。所以偷袭是真的能抢到东西，不只是骚扰。

AI 也会用这一套：`BotController` 发现你正在搜刮时会加 +0.4 攻击权重，而且**会切换成下蹲模式摸过来**（噪音半径 2 米，你听不见）。被 AI 背刺过一次你就明白为什么要时不时回头看。

---

## 三、噪音系统（潜行的基础）

所有行为都会发出噪音，AI 通过订阅 `RaidEvents.OnNoiseEmitted` 来"听"：

| 行为 | 噪音半径 |
| --- | --- |
| 疾跑 | 16 米 |
| 站立行走 | 8 米 |
| 搜刮容器 | 6 米（翻箱子是有声音的） |
| 开着面板翻找 | 4.2 米（持续发出） |
| 下蹲移动 | **2 米**（几乎无声） |

这意味着：**疾跑穿过房间等于向所有人广播你的位置**。老老实实走，或者蹲着摸。

AI 听到噪音后会记下位置（保留 3.5 秒），在没有搜刮目标时会过去查看。你可以利用这个——故意跑一段引开 AI，再蹲着绕回去。

---

## 四、文件清单（18 个文件，约 4600 行）

```
Assets/BugPartyFPS/
├── Scripts/
│   ├── Config/
│   │   ├── Enums.cs               姿态、阶段、撤离结果等枚举
│   │   ├── ItemDefinition.cs      道具（含网格体积、价值、稀有标记）
│   │   └── RaidConfig.cs          全部参数，按功能分了 10 组
│   ├── Core/
│   │   ├── RaidEvents.cs          14 个事件，接音效特效的唯一入口
│   │   └── RaidManager.cs         Intro→Looting→Extraction→Settlement
│   ├── Player/
│   │   ├── PlayerRig.cs           ★CharacterController 移动、姿态、噪音、受击
│   │   ├── FirstPersonLook.cs     ★鼠标视角、头部摆动、受击晃动、疾跑FOV
│   │   ├── GridInventory.cs       ★塔科夫式网格背包
│   │   ├── LootAction.cs          ★两段式搜刮（读条 + 面板）
│   │   ├── MeleeAction.cs         ★球形射线近战 + 背刺判定
│   │   ├── HumanController.cs     键盘鼠标输入
│   │   └── BotController.cs       ★带视锥 + 听觉的 AI
│   ├── World/
│   │   ├── LootContainer.cs       容器（延迟生成内容、已搜状态公开）
│   │   ├── DroppedLoot.cs         掉落物，可被任何人捡
│   │   ├── ExtractionZone.cs      ★撤离点
│   │   └── BugAmbience.cs         悬浮 + 故障闪烁
│   └── UI/
│       └── RaidHUD.cs             准星、交互提示、网格背包、搜刮面板、撤离进度
└── Editor/
    └── RaidSceneBuilder.cs        ★一键建场
```

---

## 五、关卡设计：为什么房间要放隔断

俯视版是 16×12 的空房间——那样在 FPS 下会非常无聊，因为**站在中间能看见所有人**，潜行和偷袭全都不成立。

FPS 版做了三处改动：

1. **放大到 24×18**，加了天花板（FPS 抬头不能是空的）
2. **★内部隔断墙**：中央十字隔断 + 四角小隔间 + 两道 1.5 米高的半墙。半墙的设计很关键——**站着挡视线，蹲下能看过去**，这让下蹲有了侦察价值而不只是消音
3. **10 个容器分三档布置**：
   - 四角隔间：稀有度加成 0.9–1.4，出 4 件，但位置最深最危险
   - 中层：正常收益
   - 中央开阔区：容易拿，但极其暴露

四个玩家的出生点在四角，**互相看不见**（有隔断挡着）。这样开局有几秒的安全期去搜第一个箱子。

---

## 六、常用调整

全部在 `Assets/BugPartyFPS/Config/RaidConfig.asset`：

| 想要的效果 | 改哪个 |
| --- | --- |
| 手感太飘 | `moveSmoothing` 调小（默认 0.09） |
| 视角太灵敏 | `mouseSensitivity` 调小（默认 2.2） |
| 晕 3D | `bobAmplitude` 调成 0，关掉头部摆动 |
| 搜刮太慢 | `searchTime` 调小（默认 3 秒） |
| 翻包不想被锁移动 | `lockMovementWhileLooting` 取消勾选 |
| 背包太紧 | `gridWidth`/`gridHeight` 调大 |
| 回到「最多2件」 | 勾 `useSimpleCountMode`，`simpleCapacity` = 2 |
| 撤离压力更大 | `extractionWindow` 调小、`extractHoldTime` 调大 |
| 潜行更重要 | `sprintNoiseRadius` 调大、`crouchNoiseRadius` 调成 0 |
| AI 太凶 | `aiAggressiveness` 调小、`aiReactionDelay` 调大 |
| AI 太傻 | `aiViewAngle`/`aiViewDistance` 调大 |
| 背刺更狠 | `backstabMultiplier`、`itemsKnockedOnBackstab` 调大 |

**切换三主题**：选中 `RaidManager`，改 `Theme` 为 `Fishing` / `Cooking` / `Police`。三套道具池已按你的文档配好，每套都含一件稀有道具。

---

## 七、接音效特效

不要改玩法脚本，订阅事件即可。FPS 版的音效比俯视版重要得多——**听声辨位是核心玩法**：

```csharp
void OnEnable()
{
    // 脚步声：必须做，这是玩家判断威胁的唯一手段
    RaidEvents.OnNoiseEmitted += (src, pos, radius) => {
        if (src == RaidManager.Instance.LocalPlayer) return;  // 别人的脚步
        float dist = Vector3.Distance(listenerPos, pos);
        if (dist > radius) return;
        // 音量按距离衰减，让玩家能判断远近
        AudioSource.PlayClipAtPoint(footstep, pos, 1f - dist / radius);
    };

    // 背刺：需要一个明显区别于普通命中的音效
    RaidEvents.OnMeleeHit += (atk, vic, isBack) => {
        AudioSource.PlayClipAtPoint(isBack ? backstabSfx : hitSfx, vic.transform.position);
    };

    // 撤离窗口开启：警报音 + 全局提示
    RaidEvents.OnPhaseChanged += p => {
        if (p == RoundPhase.Extraction) PlayAlarm();
    };
}
```

**14 个可订阅事件**：`OnPhaseChanged`、`OnTimerTick`、`OnLootTaken`、`OnLootDropped`、`OnMeleeHit`、`OnMeleeMiss`、`OnLootStarted`、`OnLootInterrupted`、`OnLootPanelToggled`、`OnInventoryChanged`、`OnNoiseEmitted`、`OnExtractProgress`、`OnExtracted`、`OnExtractFailed`。

---

## 八、换正式美术资源

**角色**：模型放进 `Visual` 节点，删掉里面的 Body/Head/Facing，把 `PlayerRig.bodyRenderer` 指向新的躯干渲染器。注意 `visualRoot` 要指向整个视觉根节点——本地玩家会隐藏它。

**第一人称手臂**：目前没有手部模型。加的方式是在相机下挂一个手臂模型，读 `MeleeAction.SwingProgress01`（0~1）驱动挥击动画。

**容器**：替换 Mesh，保留 `LootContainer` 组件和 `InteractPoint` 子物体。

**撤离点**：`ExtractionZone` 下的 `Visual` 节点可整体替换成传送门特效，把 `zoneRenderer` 指向要变色的渲染器。

**HUD**：现在是 `OnGUI` 应急实现。正式版换 UGUI，订阅同一套事件。**搜刮面板和网格背包是这个玩法的门面**，值得好好做。

---

## 九、已知取舍与限制

| 取舍 | 原因 |
| --- | --- |
| 只有 1 真人 + 3 AI | 第一人称无法本地同屏，联网超出当前范围 |
| 没用 NavMesh，AI 直线走 | 房间有隔断，AI 可能卡墙角。加 NavMesh 是明确的下一步 |
| 近战没有手臂模型 | 需要美术资源，`SwingProgress01` 已预留接口 |
| 没有真正的伤害/血量 | 按你「避免写实暴力」的要求，只有硬直和掉落 |
| `Rigidbody.drag` | Unity 2022 API 名，升级 Unity 6 需改为 `linearDamping`（仅 `DroppedLoot.cs` 一处） |
| 蹲下用改 CharacterController 高度实现 | 已加头顶检测防止卡进天花板，但复杂几何下仍可能有边界情况 |

**最需要补的一件事：给 AI 加 NavMesh。** 现在 AI 走直线，遇到隔断墙会贴着墙磨。房间里有 8 道隔断，这个问题会比较明显。做法是给地板烘 NavMesh，把 `BotController.MoveTowards` 换成 `NavMeshAgent.SetDestination`，工作量不大。

---

## 十、验收清单

按 Play 之后应该能观察到：

- [ ] 鼠标锁定，视角跟随鼠标，走路时画面有轻微上下摆动
- [ ] 按 `Shift` 疾跑时 FOV 微微变大，有速度感
- [ ] 按 `Ctrl` 视角降低到 0.95 米，移速明显变慢
- [ ] 看向容器时准星变绿，出现「[F] 搜索 档案柜」提示
- [ ] **穿墙看不到容器提示**（视线被遮挡时不显示）
- [ ] 按 `F` 出现读条，走开会中断
- [ ] 读条完成弹出搜刮面板，**此时按 WASD 无法移动，但可以转头**
- [ ] 面板里显示每件的体积（如 `2×2`）和价值，装不下的显示红色「空间不足」
- [ ] 左下角网格背包能看出大渔网占 4 格、小刀占 1 格
- [ ] 看向 AI 时准星变红，按 `V` 能把他打飞并掉出东西
- [ ] **从背后打 AI，日志显示「背刺」，掉落 2 件且是最值钱的**
- [ ] 走过去自动捡起地上的掉落物（背包装不下则捡不起来）
- [ ] AI 会自己搜箱子，且**会趁你开着面板时摸过来偷袭**
- [ ] 45 秒后传送门亮起，HUD 显示「★ 立刻撤离 ★」和距离指引
- [ ] 站进传送门出现撤离进度条，**被打断时进度清零**
- [ ] 撤离成功显示带走的分数；超时未撤离显示「战利品作废」
- [ ] 结算界面按得分排名
- [ ] 按 `R` 一切重置

**如果第 7、11、15 条成立**（翻包锁移动、背刺抢货、撤离被打断），塔科夫的核心体验就验证成功了。

---

## 十一、和 PV 分镜的关系

这套 FPS 玩法会让 PV 里的搜索段镜头语言发生变化。原分镜（`BUG派对_3分钟PV_AI视频提示词全案_V2.0.docx` 第 7/9/11 章）是斜俯视广角，展示四个人同时抢东西。

改成 FPS 后，建议增加两类镜头：

1. **第一人称主观镜头**：手伸进抽屉翻找、面板挡住视野、突然听到脚步声急转身
2. **背刺瞬间的双视角**：先给受害者的第一人称（正在翻包，画面被面板占住），再切第三人称广角（一个身影正从他背后蹲着靠近）

这两类镜头的紧张感是俯视视角给不了的。如果 FPS 方案确定下来，我可以把 PV 分镜的搜索段重写一版。
