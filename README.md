# BUG派对 · 密室搜刮

Unity **2022.3.62f3** 工程。仓库根 = Unity 工程根，可直接用 Unity Hub 打开本目录。

## 目录结构

```
密室/
├── Assets/
│   ├── BugParty2D/          ← 主玩法：2D 俯视密室搜刮
│   │   ├── Config/Items/    道具定义（ScriptableObject，22 项）
│   │   ├── Editor/          建场工具、资源导入后处理
│   │   └── Scripts/         Config / Core / Player / UI / World
│   ├── New Folder/          地形 fbx 素材（land_*.fbx）
│   └── Scenes/
├── ProjectSettings/         Unity 工程设置（入库，团队一致）
├── Packages/                包依赖清单（入库）
├── _Docs/                   策划与 PV 文档
└── _Archive/                早期原型，仅供参考，勿用于开发
    ├── BugParty_FPS/        第一人称版本
    └── BugParty_SearchRoom/ 初版密室搜索
```

## 快速上手

1. Unity Hub → Add project from disk → 选本目录
2. 菜单栏 `BugParty2D ▸ Build Room Scene` 一键建场
3. 若菜单未出现，用 `Tools ▸ BugParty2D ▸ 自检` 确认脚本已编译

## 操作

| 键位 | 行为 |
|---|---|
| WASD | 移动 |
| 空格 | 跳跃 |
| E / 长按 | 搜索容器 |
| 鼠标左键 | 肘击 |

## 核心设计

- `RoomManager` 单例驱动房间生命周期，通过 `RoomEvents` 静态事件广播状态
- `PlayerActor` 为行为载体，`HumanBrain` / `AIBrain` 分别注入玩家与 AI 决策
- `PlayerAnimatorBridge` 桥接动画层，读 `PlayerActor` 的速度/搜索状态驱动 Animator
- `RoomAudioVfx` 音效特效总线，槽位留空亦可运行，待美术资源到位再填

## 美术资源接入

见 `_Docs/美术资源接入指南.md`。要点：`RoomConfig` 上的 `characterPrefabs` 等槽位留空时回退到程序生成的占位体，填入后建场工具自动替换且挂点结构保持一致。

## 远端

| remote | 地址 | 分支 |
|---|---|---|
| `origin` | `git@github.com:komach11/bp.git` | `main` |
| `woa` | `https://git.woa.com/yitianchen/minigame.git` | `meetingroom` |

`woa` 与 `origin` 是两个独立仓库，无共同祖先。推 `woa` 需用 HTTPS（SSH 公钥未注册），提交者身份须为 `ilyayu`。
