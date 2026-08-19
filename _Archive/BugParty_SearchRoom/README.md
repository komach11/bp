# BUG派对 · 密室搜索环节 Unity 2022 完整实现

一套可直接运行的密室搜索环节。**核心机制：搜道具、上限 2 件、可以肘击对手打断搜索并打飞他已收集的道具。**

---

## 一、10 秒上手

1. 打开 Unity 2022.3 LTS，新建或打开项目（URP 或 Built-in 都支持）
2. 把 `Assets/BugParty` 整个文件夹拖进项目的 `Assets` 目录
3. 等编译完成（约 10 秒），新建一个空场景
4. 菜单栏点 **BugParty ▸ Build Search Room Scene**，确认弹窗
5. **按 Play，直接开始玩**

不需要拖任何引用、不需要建 Prefab、不需要美术资源。所有配置资产会自动生成到 `Assets/BugParty/Config`。

### 操作

| 按键 | 功能 |
| --- | --- |
| `W A S D` | 移动（按屏幕方向，符合俯视直觉） |
| `J`（按住） | 搜索最近的容器，松手取消 |
| `K` | 肘击。打断对手搜索 + 打飞他一件道具 |
| `R` | 立刻重开一轮 |

默认是 **1 真人（红方）+ 3 AI**。AI 会自己找容器搜、并且会主动来打你——尤其在你正在读条时。

---

## 二、文件清单（19 个文件）

```
Assets/BugParty/
├── Scripts/
│   ├── Config/
│   │   ├── Enums.cs                 阵营色、道具类别、房间主题、回合阶段
│   │   ├── ItemDefinition.cs        单个道具的 ScriptableObject
│   │   └── SearchRoomConfig.cs      全部可调参数 + 三主题道具池
│   ├── Core/
│   │   ├── SearchRoomEvents.cs      全局事件总线（接音效特效的入口）
│   │   └── SearchRoomManager.cs     阶段状态机、倒计时、结算、传送
│   ├── Player/
│   │   ├── PlayerActor.cs           角色主体：移动、受击、掉道具
│   │   ├── PlayerInventory.cs       背包，容量上限 2
│   │   ├── SearchAbility.cs         搜索读条 + 容器独占 + 打断回滚
│   │   ├── ElbowAbility.cs          肘击锥形判定 ★核心机制
│   │   ├── PlayerBrain.cs           控制器基类
│   │   ├── HumanBrain.cs            键盘输入（含四套本地按键预设）
│   │   └── AIBrain.cs               AI 状态机
│   ├── World/
│   │   ├── SearchContainer.cs       可搜容器：抽屉、饮水机等
│   │   ├── WorldItem.cs             被打飞落地的道具，可重新拾取
│   │   ├── FloatingBar.cs           世界空间读条（纯代码，无资源）
│   │   └── BugAmbience.cs           家具悬浮 + 故障闪烁
│   └── UI/
│       ├── SearchRoomHUD.cs         调试 HUD：倒计时、道具栏、事件日志
│       └── SearchRoomCamera.cs      斜俯视相机 + 自动取景
└── Editor/
    └── SearchRoomSceneBuilder.cs    ★一键搭场景
```

---

## 三、设计要点（为什么这么写）

### 3.1 肘击是核心，不是附加功能

`ElbowAbility` 做了三件事，缺一个笑点就成立不了：

1. **锥形判定**：只有朝着对手才能打中。所以玩家会有"转身对准"的操作感，而不是无脑按。
2. **打断搜索**：命中后立刻 `AbortSearch()`，容器进入 1.2 秒冷却——受害者不能马上重搜，这才是真的损失。
3. **打飞最新拿到的那件**：`PopLatest()` 而不是随机丢。**刚拿到就被抢走**的喜剧效果最强。

被打飞的道具变成 `WorldItem` 落在地上，**任何人走过去都能捡**。这就产生了三方混战：A 打 B，C 冲过来捡漏。

### 3.2 容器独占锁

`SearchContainer` 同一时间只允许一个人搜（`TryClaim` / `Release`）。这让"抢占位置"变成有意义的行为，也避免四个人同时读条同一个抽屉的荒谬画面。

每个容器只能被搜出 2 件道具后枯竭（变灰）。**6 个容器 × 2 = 12 件产出，4 人 × 2 格 = 8 个需求**——刻意做成略微稀缺，逼玩家去抢而不是各搜各的。

### 3.3 真人 / AI 可互换

`PlayerActor` 完全不知道自己被谁驱动，只读 `MoveInput` 属性。换控制器只需要换组件：

```
移除 AIBrain → 添加 HumanBrain → 设置 keys 为 InputScheme.Player2()
```

这直接解决了你文档里"Demo 采用真人本地多人还是 1 人＋AI，需要程序当天确认"这个待定项——**两种都支持，随时切**。

### 3.4 AI 的关键设定

`AIBrain` 在决策时会给"正在搜索的对手"额外 +0.35 攻击权重，给"身上有道具的对手"再 +0.15。所以 AI 会**专挑正在读条的人下手**，这正是最有戏的时机。

背包满了之后 AI 切换成纯攻击模式——它已经没有搜索需求，只剩下搞事，非常欠揍但很好玩。

四个 AI 有不同的 `aggressionBias`（红 +0.20 / 蓝 +0.10 / 黄 -0.10 / 绿 -0.18），对应你设定里的性格：红方最冲、绿方最阴。

---

## 四、常用调整

### 4.1 改数值

全部在 `Assets/BugParty/Config/SearchRoomConfig.asset`，Inspector 里分组清晰：

| 想要的效果 | 改哪个字段 |
| --- | --- |
| 搜索环节更长 | `searchDuration`（默认 25 秒，PV 分镜是 18 秒） |
| 肘击更容易命中 | `elbowAngle` 调大（默认 70°）、`elbowRange` 调大 |
| 被打得更惨 | `staggerDuration`、`elbowKnockback` 调大 |
| 道具飞得更远 | `itemPopForce` 调大 |
| 搜索更快 | `searchTime` 调小（默认 2.2 秒） |
| 打断惩罚更重 | `containerCooldown` 调大 |
| 更稀缺 | `containerYield` 改成 1 |
| AI 更凶 | `aiAggressiveness` 调大（默认 0.45） |
| **关掉打落道具** | `elbowKnocksOutItem` 取消勾选（只有硬直，没有掉落） |

### 4.2 切换三个主题

选中 `SearchRoomManager`，把 `Theme` 改成 `Fishing` / `Cooking` / `Police`。三套道具池已经按你的文档预设好了：

- **Fishing**：大渔网、小渔网、徒手手套、小刀、水雷
- **Cooking**：辣椒、洋葱、土豆、平底锅、番茄、鸡蛋、白萝卜、菜刀
- **Police**：手铐、扫描器、电击枪、警犬、路障

每个道具的 `effectSummary` 字段已经写好了它在下一关的作用，方便策划核对。

### 4.3 改成本地 4 人同屏

对 `Player_Blue` / `Player_Yellow` / `Player_Green` 三个物体：

1. 移除 `AIBrain` 组件
2. 添加 `HumanBrain` 组件
3. `keys` 字段展开，按 `InputScheme.Player2/3/4()` 的预设填（或直接在 Inspector 里自定义）

预设按键：
- P1：`WASD` + `J` + `K`
- P2：方向键 + 小键盘 `1` + `2`
- P3：`TFGH` + `V` + `B`
- P4：`IJKL` + `N` + `M`

### 4.4 接音效和特效

不要改玩法脚本，订阅事件即可：

```csharp
void OnEnable()
{
    SearchRoomEvents.OnElbowHit += (atk, vic) => {
        // 播放夸张的"砰"+弹簧音，生成卡通冲击线特效
        AudioSource.PlayClipAtPoint(elbowSfx, vic.transform.position);
        Instantiate(impactVfx, vic.transform.position + Vector3.up, Quaternion.identity);
    };

    SearchRoomEvents.OnItemKnockedOut += (victim, item) => {
        // 道具被打飞：播放失落音效 + 屏幕轻微抖动
    };

    SearchRoomEvents.OnPhaseChanged += phase => {
        if (phase == RoundPhase.Searching) StartCoroutine(TickingClock());
    };
}
```

可订阅的事件：`OnPhaseChanged`、`OnTimerTick`、`OnItemCollected`、`OnItemKnockedOut`、`OnElbowHit`、`OnSearchInterrupted`、`OnSearchStarted`、`OnInventoryChanged`。

### 4.5 把道具传给下一关

`PlayerInventory.ExportIds()` 返回 `List<string>`，跨场景时存到一个静态类或 `PlayerPrefs`：

```csharp
// 在 Teleport 阶段收集
foreach (var p in manager.players)
    CarryOver.Set(p.playerColor, p.Inventory.ExportIds());
```

---

## 五、换成正式美术资源

现在所有东西都是 Cube 和 Capsule 占位。替换路径：

**角色**：把角色模型拖进 `Player_Red` 下的 `Visual` 节点，删掉里面的 Body/Head/FacingIndicator，然后把 `PlayerActor.bodyRenderer` 指向新模型的躯干渲染器（它会被自动染成阵营色）。

**容器**：把家具模型替换掉 `Container_文件柜抽屉` 等物体的 Mesh，保留 `SearchContainer` 组件和 `InteractPoint` 子物体。

**道具**：在 `Item_xxx.asset` 里填 `worldPrefab` 字段，被打飞时就会生成真实模型而不是彩色方块。

**HUD**：现在的 `SearchRoomHUD` 用 `OnGUI` 绘制，只是为了让 Demo 立刻可验证。正式版建议换成 UGUI，订阅同一套事件即可，不用改玩法逻辑。

---

## 六、已知取舍

| 取舍 | 原因 |
| --- | --- |
| 挥肘会中断自己的搜索 | 防止"边搜边打"的无脑玩法，逼玩家做取舍 |
| 道具走过去自动捡，不用按键 | 派对游戏节奏优先，减少按键负担 |
| 没有用 NavMesh，AI 直线走 | 房间是空旷矩形，直线足够；如果后续加复杂障碍再接 NavMesh |
| 没有网络同步 | 按你文档"本阶段不做在线联机"的范围界定 |
| `Rigidbody.drag` 而非 `linearDamping` | Unity 2022 的 API 名。若升级到 Unity 6 需改这一处 |

---

## 七、验收标准

按 Play 之后应该能观察到：

- [ ] 开局 2.5 秒内门自动关闭，倒计时开始跳动
- [ ] 走到抽屉旁按住 `J`，容器上方出现你的阵营色读条
- [ ] 读条满后 HUD 左下角道具格亮起，事件日志打印"红方 搜到了 大渔网"
- [ ] 走到 AI 旁边按 `K`，AI 被撞飞、硬直、他的道具飞到地上打转
- [ ] 走过去踩那件道具，自动进你的背包
- [ ] 你正在读条时，AI 会主动过来肘击打断你
- [ ] 背包满 2 件后无法再搜，但仍可以肘击别人
- [ ] 25 秒到，HUD 显示"时间到"，Console 打印四人各自携带的道具清单
- [ ] 四人按红蓝黄绿顺序依次消失（传送）
- [ ] 按 `R` 一切重置，容器颜色恢复

如果第 4、5、6 条成立，**这个环节的核心乐趣就已经验证成功了**。
