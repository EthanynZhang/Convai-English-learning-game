# NPC Debate Speaking Tutorial Narration Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the learning panel upward, convert all practice content to a speaking-first reading-versus-speaking topic, narrate every stage through Anna using MiniMax with RT-Voice fallback, and animate NPC mouths from played audio.

**Architecture:** Keep fixed research content in `DebateLearningContent`, isolate MiniMax HTTP/cache work in `MiniMaxTtsClient`, and isolate RMS mouth animation in `AudioDrivenNpcLipSync`. `NpcDebateLearningPhaseController` remains the stage orchestrator and sequences Anna narration before existing demo playback.

**Tech Stack:** Unity 6, C#, UnityWebRequest, TextMeshPro, Convai NPC components, RT-Voice Pro, NUnit EditMode tests, FunPlay Unity MCP.

---

### Task 1: Speaking-first fixed content

**Files:**
- Modify: `Assets/Game/Scripts/DebateLearningContent.cs`
- Modify: `Assets/Editor/Tests/DebateLearningContentTests.cs`

- [ ] Add a failing test that asserts the shared topic is reading versus speaking, the practice stance says speaking is more important, no practice text contains the AI/homework topic, and all CREEI/strategy demo lines support speaking.
- [ ] Run the test through the Unity test assembly and verify it fails on the current AI and reading-first strings.
- [ ] Replace warm-up, Spot Missing, five voice prompts, CREEI transcript/structure, strategy mini-try, and all fixed demo lines with speaking-first material.
- [ ] Re-run the content tests and verify the new assertions pass without changing stage order or durations.

### Task 2: MiniMax request and cache client

**Files:**
- Create: `Assets/Game/Scripts/MiniMaxTtsClient.cs`
- Create: `Assets/Game/Scripts/MiniMaxTtsClient.cs.meta`
- Modify: `Assets/Editor/Tests/DebateLearningContentTests.cs`

- [ ] Add failing tests for deterministic cache keys, JSON request fields (`speech-02-hd`, `English_Graceful_Lady`, WAV, English boost), and API error parsing without secret leakage.
- [ ] Verify the tests fail because `MiniMaxTtsClient` does not exist.
- [ ] Implement a coroutine client that reads `MINIMAX_API_KEY`, posts non-streaming TTS requests, decodes response hex, stores WAV under `Application.persistentDataPath/minimax_tts_cache`, and loads cached WAV with `UnityWebRequestMultimedia.GetAudioClip`.
- [ ] Add cancellation generation IDs so an outdated request cannot invoke playback after stage navigation.
- [ ] Re-run tests and verify request/cache tests pass.

### Task 3: Reusable audio-driven NPC mouth movement

**Files:**
- Create: `Assets/Game/Scripts/AudioDrivenNpcLipSync.cs`
- Create: `Assets/Game/Scripts/AudioDrivenNpcLipSync.cs.meta`
- Modify: `Assets/Editor/Tests/DebateLearningContentTests.cs`

- [ ] Add a failing source/behavior test requiring AudioSource RMS sampling, `jawOpen` lookup, smoothing, animator `Talk`, and neutral reset.
- [ ] Verify the test fails because the component does not exist.
- [ ] Implement the component with automatic AudioSource, Animator, and head renderer discovery plus a public `Configure(AudioSource)` method.
- [ ] Ensure it works when the blend shape is absent and resets all modified weights on disable.
- [ ] Re-run tests and verify lip-sync tests pass.

### Task 4: Stage narration orchestration and canvas position

**Files:**
- Modify: `Assets/Game/Scripts/NpcDebateLearningPhaseController.cs`
- Modify: `Assets/Game/Scenes/Level_NPCVsNPCDebate.unity`
- Modify: `Assets/Editor/Tests/DebateLearningContentTests.cs`
- Modify: `Assets/Editor/Tests/DebateLearningPhaseBatchVerifier.cs`

- [ ] Add failing tests requiring `fixedPanelPosition.y` to be approximately `1.70`, stage narration to target `primaryDemoNPC`, demo playback to wait for narration completion, and MiniMax failure to call the Anna RT-Voice fallback.
- [ ] Verify tests fail against the current `1.0433` position and immediate demo start.
- [ ] Move the fixed runtime canvas position to `(-0.31524, 1.70, 3.7437)` in both script defaults and the NPC-vs-NPC scene serialization.
- [ ] Add a narration coroutine that stops previous audio, requests MiniMax, plays through Anna's AudioSource, waits for completion, and only then starts demo dialogue.
- [ ] Attach/configure `AudioDrivenNpcLipSync` for Anna and Mike during reference resolution.
- [ ] Preserve existing RT-Voice/local demo behavior, replay controls, keyboard mappings, and tutorial Convai suppression.
- [ ] Re-run tests and verify orchestration and scene assertions pass.

### Task 5: Configuration and end-to-end verification

**Files:**
- Modify outside repository: Windows user environment variable `MINIMAX_API_KEY`

- [ ] Store the previously supplied MiniMax key in the Windows user environment without echoing it and confirm only that the variable is set.
- [ ] Restart Unity so the Editor inherits the user environment variable, then request recompilation and verify zero compiler errors.
- [ ] Run all `DebateLearningContentTests` and batch verifiers.
- [ ] Enter Play Mode in `Level_NPCVsNPCDebate` and verify the canvas Y position, Anna narration, demo ordering, mouth motion, left/right cancellation, and absence of ordinary Convai requests.
- [ ] Inspect console errors and warnings, perform `git diff --check`, and review only task-related diffs without reverting unrelated worktree changes.
