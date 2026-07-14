# NPC Tutorial Navigation And Input Isolation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace visible Previous/Next buttons with a keyboard hint footer and prevent ordinary Convai NPC conversations while the NPC-vs-NPC learning tutorial is active.

**Architecture:** Keep navigation and input ownership inside `NpcDebateLearningPhaseController`. Arrow keys call stage actions directly, while scene-local static Convai interceptors block ordinary voice/text conversation until the tutorial completes; the existing five-step voice practice continues to start its own STT stream directly.

**Tech Stack:** Unity 6, C#, uGUI, TextMeshPro, Convai Unity SDK, NUnit EditMode tests, FunPlay MCP.

---

### Task 1: Add failing regression tests

**Files:**
- Modify: `Assets/Editor/Tests/DebateLearningContentTests.cs`

- [ ] Add source-contract tests asserting that the controller creates `Learning Keyboard Hint`, displays `Previous` and `Next / Confirm`, does not create Previous/Next Buttons, registers tutorial isolation at tutorial start, and releases it on completion.
- [ ] Recompile Unity and invoke the new tests; confirm they fail because the keyboard hint and tutorial-wide isolation do not exist yet.

### Task 2: Replace Previous/Next buttons with keyboard guidance

**Files:**
- Modify: `Assets/Game/Scripts/NpcDebateLearningPhaseController.cs`
- Modify: `Assets/Editor/Tests/DebateLearningPhaseBatchVerifier.cs`
- Modify: `Assets/Editor/Tests/CreeiVoicePracticeBatchVerifier.cs`

- [ ] Remove runtime creation and field dependencies for Previous/Next buttons while retaining Replay Demo.
- [ ] Add a fixed-height footer containing two uGUI Image keycaps (`\u2190`, `\u2192`) with `Previous` and `Next / Confirm` labels.
- [ ] Route left/right keyboard shortcuts directly through `PreviousStage` and `AdvanceStage`, preserving the five-step voice-practice completion gate.
- [ ] Update verifiers to assert the hint exists and Previous/Next Button objects do not.

### Task 3: Isolate Convai conversation for the tutorial lifetime

**Files:**
- Modify: `Assets/Game/Scripts/NpcDebateLearningPhaseController.cs`

- [ ] Register scene-local Convai voice, NPC interaction, chat-toggle, text-submit, transcript, and active-NPC interceptors when the tutorial starts.
- [ ] Suppress ordinary Convai talk/text for `_started && !_completed`; route final transcripts to the existing five-step voice practice only when that practice is awaiting STT.
- [ ] Release only callbacks owned by this controller when the tutorial completes, disables, or is destroyed.

### Task 4: Verify behavior

**Files:**
- Test: `Assets/Editor/Tests/DebateLearningContentTests.cs`
- Test: `Assets/Editor/Tests/DebateLearningPhaseBatchVerifier.cs`
- Test: `Assets/Editor/Tests/CreeiVoicePracticeBatchVerifier.cs`

- [ ] Recompile with zero errors.
- [ ] Run focused source-contract tests and runtime assertions.
- [ ] Enter Play Mode, verify the keycap footer is visible, simulate left/right navigation, and confirm tutorial input interceptors are installed.
- [ ] Exit Play Mode and inspect Console errors and the final diff.
