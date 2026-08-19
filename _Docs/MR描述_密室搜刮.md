## 变更内容

新增每轮玩法开始前的**密室搜刮环节**（2D 俯视角），独立命名空间 `BugParty.TopDown2D`，与现有 `PartyGame` 捕鱼玩法互不干扰。

**28 个文件，全部为新增，未修改或删除任何现有文件。**

```
Assets/BugParty2D/          24 个 C# 脚本
Assets/Docs/                4 份文档
```

## 如何验证

1. 切到本分支，等 Unity 编译完成
2. 菜单 **BugParty2D ▸ Build Room Scene**（或 **Tools ▸ BugParty2D ▸ Build Room Scene**）
3. 按 **Play**

无需拖引用、建 Prefab 或导入美术资源——房间、地板网格、容器、玩家、相机、灯光、HUD 与道具配置全部由建场工具自动生成。

**操作**：`WASD` 移动 / `Space` 跳跃 / `J` 按住搜索 / `K` 肘击 / `R` 重开。默认 1 名真人（红方）+ 3 名 AI。

> 若菜单栏找不到 `BugParty2D`，点 **Tools ▸ BugParty2D ▸ 自检（确认脚本已加载）** 确认脚本编译状态。

## 核心设计

**场景 34×26**，地板拆成 **17×13 = 221 块**独立可塌陷方格，块间留缝隙——玩家需要看清边界才能预判哪块在开裂。12 个搜索容器。

**正交投影 + 70° 俯角**。2D 感的来源是正交投影（远近同尺寸、无透视变形），不是俯角为 90°。降到 70° 后观感依然是 2D，但高度差清晰可读。落地阴影随离地高度缩小变淡，这是俯视角判断高度的核心线索。俯角在 `cameraPitch` 里 50–90 可调。

**三级高度地形**，跳跃高度经物理公式验证（v=7.2, g=22 → 最大跳高 1.178 m）：

| 层级 | 高度 | 结果 |
|---|---|---|
| 矮柜 / 椅子 | 0.55 | 轻松跳上 |
| 中央会议桌 | 0.95 | 需正常跳 |
| 四角高台 | 1.45 | **跳不上，必须两段跳** |

高台跳不上去是刻意设计——那里放着稀有度最高的容器，跳跃因此从一个动作变成一条收益路线。同时提供斜坡（4 级台阶靠 `stepOffset` 自动爬）作为退路。

**地板塌陷四态状态机**：`Solid → Cracking(1.8s 预警) → Collapsed → Falling`。预警阶段必须存在，否则玩家会觉得被阴；有预警才能形成「快跑离开」的正向操作。搜索中随机塌 5 块（避开容器与出生点 2.2 m 内）。

**掉洞采用「受罚」而非「隐形墙挡住」**：下坠 0.9 s → 弹回最近安全地板 → 硬直 1.2 s → 掉 1 件道具。隐形墙会让玩家撞得莫名其妙，改成受罚后绕路是玩家自己算出的决策。

**终局全塌陷**：以房间中心为震中波浪式塌陷，四人翻滚下坠，道具通过 `CarryOverData` 静态类传递到下一关。这段可直接作为三个玩法关卡之间的统一转场。

**故障氛围三层叠加**，随倒计时加剧：天花板碎片间隔 1.4s→0.35s、警报周期 2.4s→0.45s、画面抖动 6s→1.8s。

## 美术资源接入

所有槽位**留空即回退到程序生成的占位体**，可以逐个替换，不必等资源齐了才能跑。

- `RoomConfig.characterPrefabs[4]` — 角色模型。建场时自动移除模型自带 Collider（避免与 CharacterController 冲突）、取 `SkinnedMeshRenderer` 作染色目标、**检测到 Animator 时自动挂 `PlayerAnimatorBridge` 并完成接线**
- `PlayerAnimatorBridge` — 把玩法状态翻译成 Animator 参数（5 参数 + 5 Trigger）。用 `HasParameter` 缓存保护，**缺参数不报错**，可增量补全
- `RoomAudioVfx` — 音效与特效总线，订阅 13 个 `RoomEvents` 事件，13 个音效槽 + 5 个粒子槽
- 另有容器 / 碎片 / 地砖 / 阴影 / 地板两态材质槽位

详见 `Assets/Docs/密室搜刮_美术资源接入指南.md`（含模型规格要求、Animator 参数表、10 条验收清单）。

## 接音效特效无需改玩法脚本

```csharp
RoomEvents.OnElbowHit += (attacker, victim) => {
    AudioSource.PlayClipAtPoint(elbowSfx, victim.transform.position);
    Instantiate(impactVfx, victim.transform.position + Vector3.up, Quaternion.identity);
};
```

`RoomEvents` 暴露 17 个事件，覆盖搜索、肘击、跳跃落地、地板开裂/塌陷、阶段切换、倒计时等全部时机。

## 需要评审注意的点

1. **`Rigidbody.drag` 是 Unity 2022 的 API 名**。若后续升级 Unity 6 需改为 `linearDamping`，涉及 `PlayerActor.cs` 与 `WorldItem.cs` 两处。
2. **本分支未包含 NavMesh**。AI 绕洞目前是射线启发式（每帧探测前方 1.6 m，是洞则依次试 ±45°/±80°/±120°），在复杂地形下可能贴墙磨。建议后续改为 NavMesh + Off-Mesh Link。
3. **`RoomHUD.cs` 使用 `OnGUI()` 代码绘制**，仅为让 Demo 零依赖跑起来，不适合作为正式 UI。正式 UI 建议新建 Canvas + 新 HUD 脚本订阅 `RoomEvents`，再禁用 `RoomHUD` 组件。
4. **Unity 版本**：本分支工程为 `2022.3.62f3c1`。脚本未使用版本差异 API。

## 附带文档

| 文件 | 内容 |
|---|---|
| `Assets/Docs/密室搜刮_说明文档.md` | 架构说明、调参对照表、10 条验收清单 |
| `Assets/Docs/密室搜刮_美术资源接入指南.md` | 模型规格、Animator 参数表、常见问题 |
| `Assets/Docs/PV_3分钟提示词全案.md` | 3 分钟 PV 的 41 镜逐镜 AI 视频提示词 |
| `Assets/Docs/PV_场景四警察抓贼_赛博朋克第一人称30秒.md` | 警察关 30 秒提示词（赛博朋克 + 第一人称） |
