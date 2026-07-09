# Shared Initiative Coach Agent 交接说明

## 场景位置

目标场景：

`Assets/Game/Scenes/Level_SharedInitiativeOrchestration.unity`

主要脚本：

- `Assets/Game/Scripts/SharedInitiativeOrchestrationController.cs`
- `Assets/Game/Scripts/DebateCoachFeedbackGenerator.cs`
- `Assets/Game/Scripts/CoachFeedbackTypes.cs`
- `Assets/Game/Scripts/DebateCoachLogger.cs`
- `Assets/Game/Scripts/InteractiveDebateTranscriptBridge.cs`

当前场景中的角色绑定：

- `conversationNPC`: `Convai NPC Mike Carter`，角色名为 `leo`
- `coachNPC`: `Convai NPC Anna Reed`，角色名为 `Coach`

## 当前目标

这个场景用于测试一种 Shared Initiative 的英语辩论练习流程。

核心目标是：学习者先和 NPC 进行简短 debate，然后 Coach Agent 在学习者发言之后提供即时策略反馈。Coach 的作用不是替学习者完整作答，而是指出当前发言中最需要改进的一点，并给出下一步策略。

## 基本交互流程

1. 打开 `Level_SharedInitiativeOrchestration.unity`。
2. 进入 Play Mode。
3. 点击左侧面板中的 `Start Conversation`。
4. Mike Carter 先作为对手 NPC 发言。
5. 用户按住 `T` 进行语音回应。
6. Convai 将用户语音转写为文本。
7. 系统将用户发言、NPC 上一轮发言、辩题和学习者立场发送给 Coach feedback generator。
8. Coach Agent 生成反馈。
9. Anna Reed 用语音读出 Coach feedback，同时反馈文字显示在 Coach 面板和 Transcript UI 中。
10. 用户可以点击：
    - `Need an example?`: 让 Coach 给一个更具体的句式或表达框架。
    - `Continue`: 进入下一轮 NPC 发言。
    - `End Session`: 结束当前 debate，并让 Anna 直接输出最终 Coach feedback。

## 反馈逻辑

Coach feedback 当前分为几个层级：

- `Level2`: 默认反馈。给出一句诊断和一句下一步策略。
- `Level3`: 用户点击 `Need an example?` 后触发。给出一个 sentence frame 或短例子。
- `Summary`: 用户点击 `End Session` 或完成最大轮次后触发。给出最终总结反馈。

反馈分析使用两个框架：

- CREEI: `Claim`, `Reason`, `Evidence`, `Explanation`, `Impact`
- 修辞策略: `Logos`, `Ethos`, `Pathos`

当前 prompt 约束 Coach 不要替学生写完整答案，而是给短、具体、可执行的建议。

## End Session 当前逻辑

点击 `End Session` 后，系统不会再先说固定的 `Good work...`。

当前正确逻辑是：

1. 停止当前可能还在播放的 Coach 语音。
2. 丢弃旧的异步 Coach 请求。
3. 切换 active NPC 到 Anna Reed。
4. 生成 `Summary` 级别的最终 Coach feedback。
5. Anna 直接读出最终反馈。

代码中用 `_coachRequestVersion` 防止旧反馈延迟返回后突然插入当前对话。

## 语音说明

当前已开启 Coach 语音反馈：

- `speakCoachFeedback: true`
- `coachVoicePitch: 0.92`
- `coachSpeechStyleInstruction`: 温和、支持性的女性 Coach 语气

注意：`coachVoicePitch` 只是 Unity 本地播放层的 pitch 调整，可以缓解声音尖锐问题。如果需要彻底更换 Anna 的云端音色，需要在 Convai 后台修改 Anna/Coach 角色的 voice 设置或替换对应 character。

## Transcript UI

场景中使用现有 Convai Transcript UI。

`InteractiveDebateTranscriptBridge` 会将以下内容写入当前 Convai chat UI：

- 用户语音转写
- Coach feedback
- NPC 输出

如果 Transcript UI 没有显示 Coach 内容，请优先检查：

- `ConvaiChatUIHandler.Instance` 是否存在
- 当前 active chat UI 是否启用
- Console 中是否有 `InteractiveDebateTranscriptBridge could not find an active Convai chat UI` 警告

## 当前重要开关

在 `SharedInitiativeOrchestrationController` 中：

- `automaticCoachAfterPlayerVoice`: 用户语音结束后自动触发 Coach
- `showManualPauseButton`: 当前默认关闭
- `speakCoachFeedback`: 当前开启
- `useWindowsTtsFallbackWhenConvaiSilent`: 当前开启
- `convaiSpeechFallbackDelaySeconds`: Convai 无语音时等待多久启动 Windows TTS fallback
- `maxConversationTurns`: 当前为 3 轮

## 测试检查清单

基本 Play Mode 验证：

1. 点击 `Start Conversation` 后，Mike 先发言。
2. 用户按住 `T` 回答。
3. 用户发言结束后，Anna 自动给出 Coach feedback。
4. Anna 的语音应该较温和，不应明显尖锐刺耳。
5. 点击 `Need an example?` 后，Anna 给出具体表达框架。
6. 点击 `Continue` 后，Mike 进入下一轮发言。
7. 点击 `End Session` 后，Anna 应直接输出最终 Coach feedback。
8. 点击 `End Session` 后，不应再出现延迟冒出的旧反馈。
9. Round Timer 应保持隐藏。

## 已知限制

- Convai 云端角色音色仍取决于 Convai 后台 character 配置，Unity 侧只能做有限 pitch 和 prompt 调整。
- Coach feedback 依赖 OpenAI/中转站配置；如果没有 API key，会使用本地规则 fallback。
- 语音链路依赖 Convai 网络响应，真实体验需要在 Play Mode 中手动验证。
- 当前 Coach 只在这个 Shared Initiative 场景中启用，其他 Debate 场景不应被影响。

## 建议下一步

- 在 Convai 后台为 Anna/Coach 更换更自然的女性 voice。
- 让老师确认 Coach feedback 的教学口吻：更像教师评价，还是更像同伴提示。
- 决定 `Summary` 反馈是否需要更研究化，例如输出 CREEI 得分或学习建议标签。
- 将 Play Mode 检查流程录制成短视频，方便非开发成员验收。
