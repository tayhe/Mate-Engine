# Agent Bridge: AI Agent 状态同步到桌面宠物

## 背景与动机

Mate Engine 的桌面宠物目前能响应鼠标、音乐、窗口交互等本地事件。但用户越来越多地使用 AI Agent（如 OpenClaw、Hermes）进行日常工作。如果宠物能实时反映 Agent 的工作状态——正在思考时露出专注表情、完成任务时微笑、出错时皱眉——宠物就从"装饰品"变成了"工作伙伴"。

## 设计原则

1. **零侵入**: 不修改 OpenClaw 或 Hermes 源码，只写独立适配器脚本。这两个项目活跃更新，制造分支会带来维护负担。
2. **复用已有模式**: Mate Engine 已经有 JSON 文件总线（dance sync）和 UDP 接收（Minecraft messages）两种 IPC 模式，不需要发明新机制。
3. **统一协议**: 一个 JSON 格式同时适配两种 agent，宠物侧只需一套代码。

## 为什么选文件总线而不是其他 IPC

| 方案 | 优点 | 缺点 | 结论 |
|------|------|------|------|
| HTTP Server in Unity | 实时性好 | 需要管理员权限绑端口（Windows）；Unity 线程模型复杂 | 否决 |
| Named Pipe | 延迟低（ms 级） | 项目无先例；跨平台需额外处理 | 否决 |
| 文件总线（JSON 轮询） | 跨平台；无需权限；dance sync 已验证 | 延迟 100ms 级（对状态同步足够） | **采纳** |
| UDP | 实时性好 | 需要端口管理；Minecraft messages 已有但更复杂 | 备选 |

文件总线是最务实的选择。`AvatarDanceSync` 已经证明了这套模式可靠：原子写入（.tmp → rename）+ 版本号去重 + 50ms 轮询。Agent 状态变化频率远低于舞蹈同步，100ms 轮询完全够用。

## 架构概览

```
┌─────────────────┐     JSON 文件      ┌──────────────────────┐
│  Agent 适配器    │ ──────────────→    │  Mate Engine          │
│  (Python/TS)    │   agent_status.json │  AvatarAgentBridge.cs │
│                 │                     │                       │
│  监听/轮询       │                     │  轮询 → 解析 → 驱动    │
│  agent 状态      │                     │  动画 + 气泡 + 表情    │
└─────────────────┘                     └──────────────────────┘
```

## JSON 协议设计

```json
{
  "v": 1,            // 递增版本号，用于去重
  "agent": "openclaw", // agent 标识
  "state": "working",  // 状态枚举
  "message": "...",    // 显示文本
  "progress": 0.65,    // 进度 0-1
  "error": null,       // 错误信息
  "task_name": "...",  // 任务描述
  "writeUtc": 1716552000.123  // 写入时间
}
```

### 状态枚举

| state | 含义 | 宠物表现 |
|-------|------|----------|
| `idle` | Agent 空闲 | 微笑（Fun=0.3），无气泡 |
| `thinking` | Agent 正在思考 | 中性表情，显示 "..." |
| `working` | Agent 正在执行工具 | 轻微喜悦（Joy=0.2），显示 message |
| `streaming` | Agent 正在输出文本 | 喜悦（Joy=0.4），打字机显示 |
| `success` | 任务完成 | 开心（Joy=0.8, Fun=0.6），5s 后消失 |
| `error` | 任务出错 | 难过+生气（Sorrow=0.7, Angry=0.3），8s 后消失 |
| `disconnected` | 连接断开 | 表情归零，气泡隐藏 |

### 为什么需要 `v` 字段

参考 `AvatarDanceSync.cs` 的设计：多个写入者可能同时写入同一文件（比如多实例场景），版本号让读取者只处理最新的消息。Mate Engine 用 `v > lastSeenV` 判断是否有新消息。

### 为什么需要 `writeUtc` 超时检测

适配器可能崩溃或被关闭，但不会写入 "disconnected" 状态。通过检查 `now - writeUtc > 5s`，宠物能自动检测到 agent 已离线。

## 为什么不用 Webhook / SSE

OpenClaw 支持 Webhook 和 SSE，Hermes 有 Dashboard API。理论上可以用推送模式替代轮询。但：

1. **文件总线更简单**: 不需要在 Unity 侧实现 HTTP 服务器或 WebSocket 客户端
2. **更健壮**: 文件读写不会因为网络问题失败
3. **更易调试**: 直接查看 JSON 文件就能看到当前状态
4. **统一**: 两种 agent 用同一种机制，不需要在 Unity 侧做协议适配

轮询的代价（100ms CPU 开销）对桌面应用可以忽略。

## 宠物侧实现思路

### 核心脚本: AvatarAgentBridge.cs

放在 `Assets/MATE ENGINE - Scripts/Game APIs/` 目录下，与 `AvatarMinecraftMessages.cs` 同级——两者都是"接收外部事件驱动宠物行为"的模式。

**复用的已有代码**:

| 功能 | 复用来源 | 说明 |
|------|----------|------|
| 气泡显示 | `AvatarRandomMessages.cs` | `BubbleUI` 结构体 + `Bubble` 类 + `FakeStreamText` 协程 |
| 表情控制 | `UniversalBlendshapes.cs` | 直接写 Joy/Angry/Sorrow/Fun 字段（line 11） |
| 动画控制 | `AvatarRandomMessages.cs` | `animator.SetBool("isTalking", ...)` |
| 文件轮询 | `AvatarDanceSync.cs` | coroutine + WaitForSecondsRealtime 模式 |
| 版本去重 | `AvatarDanceSync.cs` | `v > lastSeenV` 逻辑 |
| 原子写入 | `AvatarDanceSync.cs` | .tmp → rename 模式（适配器侧） |

**不复用的理由**:

| 考虑的方案 | 为什么不用 |
|-----------|-----------|
| 用 UDP（仿 Minecraft） | 不需要实时性；文件总线更简单 |
| 用 NamedPipe | 项目无先例；跨平台复杂 |
| 修改 AvatarAnimatorController | 避免改动核心状态机；Agent 状态是独立关注点 |

### 表情自动恢复

`UniversalBlendshapes` 有内置的淡出机制：当 public 字段不再被外部设置时，值会在 `safeTimeout`（2s）后自动归零。这意味着：
- Agent 工作中持续设置 `Joy=0.2` → 表情保持
- Agent 完成后设置 `Joy=0.8` → 5s 内保持开心
- 之后不再设置 → 自动淡回中性

不需要额外的"恢复"逻辑。

### 与 AvatarRandomMessages 的气泡冲突

两者共用 `chatContainer`。`RemoveBubble()` 会销毁旧气泡，所以新气泡总是替换旧的。Agent 气泡优先级更高——如果 agent 正在活跃，随机消息的气泡会被替换掉。这是可接受的行为。

## 适配器设计

### 为什么适配器是独立脚本而不是 agent 插件

1. **不侵入 agent 源码**: OpenClaw 和 Hermes 都在活跃更新，制造分支会带来合并冲突
2. **语言灵活性**: OpenClaw 用 TypeScript，Hermes 用 Python，适配器可以用最适合的语言
3. **独立部署**: 适配器可以和 agent 分开启动/停止
4. **可测试**: 可以独立测试适配器的文件写入逻辑

### OpenClaw 适配器 (TypeScript)

连接 OpenClaw 的 WebSocket RPC，监听 `agent`/`chat`/`health` 事件。OpenClaw 的 Gateway 已经发出类型化事件，适配器只是做事件到 JSON 协议的翻译。

### Hermes 适配器 (Python)

轮询 Hermes Dashboard API（`/api/agent/status`）。Hermes 没有文档化的 webhook/event-hook 系统，轮询是最可靠的集成方式。

## 路径约定

Agent 适配器需要知道 Mate Engine 的 `Application.persistentDataPath` 对应的文件系统路径：

| 平台 | 路径 |
|------|------|
| Windows | `%USERPROFILE%/AppData/LocalLow/Shinymoon/MateEngineX/AgentBridge/` |
| Linux | `~/.local/share/unity3d/Shinymoon/MateEngineX/AgentBridge/` |

支持 `MATE_ENGINE_BUS_PATH` 环境变量覆盖，默认路径适用于标准 Unity 安装。

## 多 Agent 同时运行

如果用户同时运行 OpenClaw 和 Hermes，每个适配器写入独立文件：
- `agent_status_openclaw.json`
- `agent_status_hermes.json`

C# 端读取两个文件，取 `v` 值最大的那个。这样避免了写入冲突。

## 文件变更清单

| 文件 | 操作 | 说明 |
|------|------|------|
| `Assets/MATE ENGINE - Scripts/Game APIs/AvatarAgentBridge.cs` | 新建 | 核心桥接脚本 |
| `Assets/MATE ENGINE - Scripts/Settings/SaveLoadHandler.cs` | 修改 | SettingsData 加 1 行 `enableAgentBridge` |
| `adapters/openclaw_adapter.ts` | 新建 | OpenClaw 适配器 |
| `adapters/hermes_adapter.py` | 新建 | Hermes 适配器 |

---

## 实施状态

### 已完成（代码层面）

| 任务 | 文件 | 状态 |
|------|------|------|
| 核心桥接脚本 | `AvatarAgentBridge.cs` | ✅ 已提交 |
| 设置开关 | `SaveLoadHandler.cs` | ✅ 已提交 |
| OpenClaw 适配器 | `adapters/openclaw_adapter.ts` | ✅ 已提交 |
| Hermes 适配器 | `adapters/hermes_adapter.py` | ✅ 已提交 |
| 项目文档 | `CLAUDE.md` | ✅ 已提交 |
| 设计文档 | `docs/design-agent-bridge.md` | ✅ 已提交 |

相关 commit：
- `92077151` — Add CLAUDE.md and agent bridge design doc
- `5788ab99` — Add Agent Bridge: AI agent status → desktop pet integration

### 待完成（需要在 Unity Editor 中操作）

#### 1. 场景配置（必须）

在 Unity 6000.2.6f2 中打开项目后：

1. 打开场景 `Assets/MATE ENGINE - Scenes/Mate Engine Main.unity`
2. 在 Hierarchy 中找到 avatar 对象
3. Add Component → `AvatarAgentBridge`
4. 在 Inspector 中设置引用：
   - `chatContainer` — 从同对象的 `AvatarRandomMessages` 组件的 `chatContainer` 复制
   - `bubbleSprite` — 从 `AvatarRandomMessages` 复制
   - `font` — 从 `AvatarRandomMessages` 复制
   - `bubbleMaterial` — 从 `AvatarRandomMessages` 复制（可选）
5. 勾选 `enableAgentBridge`

#### 2. Settings UI 开关（可选）

如果需要在设置菜单中暴露 `enableAgentBridge` 开关：

1. 找到 Settings Menu 的 UI 预制体
2. 复制现有的 toggle 组件（如 `enableRandomMessages` 的 toggle）
3. 绑定到 `SaveLoadHandler.Instance.data.enableAgentBridge`
4. 在 `ApplyAllSettingsToAllAvatars()` 中添加同步逻辑（如需要）

#### 3. 适配器测试

**OpenClaw 适配器测试**：
```bash
cd adapters
npm install ws
npx ts-node openclaw_adapter.ts ws://localhost:8080
```
验证：检查 `agent_status.json` 文件是否被正确创建和更新。

**Hermes 适配器测试**：
```bash
cd adapters
pip install requests
python hermes_adapter.py --api http://localhost:8000
```
验证：检查 `agent_status.json` 文件是否被正确创建和更新。

#### 4. 端到端测试

1. 启动 Mate Engine（Unity Play Mode 或构建的 exe）
2. 启动一个适配器脚本
3. 手动写入测试 JSON 到总线文件：
```json
{"v":1,"agent":"test","state":"working","message":"Testing...","progress":0,"error":null,"task_name":"","writeUtc":1716552000.0}
```
4. 观察宠物反应：表情变化、气泡显示、动画状态切换

### 已知限制

1. **多 agent 冲突**：当前实现只读取单个 `agent_status.json` 文件。如果需要同时运行 OpenClaw 和 Hermes，需要修改 `AvatarAgentBridge.cs` 读取两个文件（`agent_status_openclaw.json` 和 `agent_status_hermes.json`）并取最新的。
2. **气泡冲突**：Agent 气泡与 `AvatarRandomMessages` 共用 `chatContainer`，会互相替换。这是设计上的权衡。
3. **适配器未验证**：两个适配器脚本是基于文档编写的，未实际连接 OpenClaw/Hermes 测试。事件映射可能需要根据实际 API 调整。
