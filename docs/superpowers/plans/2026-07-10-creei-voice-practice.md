# CREEI Five-Step Voice Practice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Turn the existing Micro Practice 2 stage into a required five-step CREEI voice exercise whose only completion condition is a non-empty final Convai transcript for Claim, Reason, Evidence, Explanation, and Impact.

**Architecture:** Keep `MicroPracticeOneSentence` as one top-level stage. Add an explicit, controller-owned substep state machine that temporarily owns Convai transcript interception and talk-input suppression, and persist only confirmed text in the append-only learning CSV fields.

**Tech Stack:** Unity 6, C#, TextMeshPro/uGUI, Convai runtime, NUnit EditMode tests.

---

### Task 1: Define immutable CREEI voice content and research metrics

**Files:**
- Modify: `Assets/Game/Scripts/DebateLearningContent.cs`
- Modify: `Assets/Game/Scripts/DebateLearningMetrics.cs`
- Test: `Assets/Editor/Tests/DebateLearningContentTests.cs`

- [x] **Step 1: Write failing content/metrics tests** for exactly five ordered prompts, the AI-in-class-and-homework topic, a 240-second reference duration, confirmed-only transcript persistence, aggregate legacy text, completion after Impact, and re-record counting.
- [x] **Step 2: Run the focused EditMode test** and confirm the new expectations fail because the prompt model/metrics do not yet exist.
- [x] **Step 3: Add `CreeiVoicePracticePrompt` and the fixed Claim→Impact prompt array**, replace the Micro Practice 2 introduction, set `OneSentencePracticeSeconds` to `240f`, and add metrics methods/properties that accept only confirmed steps.
- [x] **Step 4: Re-run the focused EditMode test** and confirm it passes.

### Task 2: Append research-safe CSV fields

**Files:**
- Modify: `Assets/Game/Scripts/DebateLearningLogger.cs`
- Modify: `Assets/Editor/Tests/DebateLearningContentTests.cs`

- [x] **Step 1: Write failing logger tests** that inspect a temporary CSV row for the five named new columns and correctly escaped commas, quotes, and newlines in voice transcripts.
- [x] **Step 2: Run the focused test** and confirm the new header/value assertions fail.
- [x] **Step 3: Append the seven specified columns after the compatibility columns** and write all metric values through `EscapeCsv`; retain the existing legacy aggregate/template fields.
- [x] **Step 4: Re-run the focused logger test** and confirm it passes.

### Task 3: Add the Micro Practice 2 voice-state machine and panel rendering

**Files:**
- Modify: `Assets/Game/Scripts/NpcDebateLearningPhaseController.cs`
- Test: `Assets/Editor/Tests/DebateLearningPhaseBatchVerifier.cs`

- [x] **Step 1: Write failing controller/verifier checks** for disabled confirmation without a transcript, Claim becoming ready after the controller transcript callback, confirm-to-Reason progression, re-record replacement, and no exit before Impact.
- [x] **Step 2: Run the focused verifier** and confirm it fails against the static current stage.
- [x] **Step 3: Add controller-owned practice state** (current index, five pending transcripts, five confirmed flags, recording/awaiting flags, re-record counter), a dedicated read-only transcript/status display, contextual confirmation labels, completed-part summary, and a `T` toggle path that calls the active `primaryDemoNPC` directly.
- [x] **Step 4: Gate right-arrow/keypad-6/Next and `AdvanceStage()`**, disabling navigation/replay during listening or transcribing, preserving prior confirmed steps, returning to the first unfinished step on revisit, and exiting normally only after confirmed Impact.
- [x] **Step 5: Re-run the focused verifier** and confirm state progression passes without an NPC reply.

### Task 4: Safely own and release Convai input during voice practice

**Files:**
- Modify: `Assets/Game/Scripts/NpcDebateLearningPhaseController.cs`
- Test: `Assets/Editor/Tests/DebateLearningPhaseBatchVerifier.cs`

- [x] **Step 1: Write failing verifier/source checks** for owner-safe registration of `TryHandleUserVoiceTranscript`, all three talk suppressors, active-NPC suppression, and owner-safe unregistering on stage exit, disable, and destroy.
- [x] **Step 2: Run the focused verifier** and confirm the callback lifecycle checks fail.
- [x] **Step 3: Register only while `MicroPracticeOneSentence` is active**, return `true` only for a non-empty final transcript owned by the current practice turn, provide retry status for unavailable microphone/failed startup/empty transcript, and clear static delegates only when they still equal this controller's delegate.
- [x] **Step 4: Re-run the focused verifier** and confirm leaving the practice stage restores normal Convai handling.

### Task 5: Compile and run the end-to-end Unity verification

**Files:**
- Verify: `Assets/Game/Scenes/Level_NPCVsNPCDebate.unity`
- Verify: `Assets/Editor/Tests/DebateLearningContentTests.cs`
- Verify: `Assets/Editor/Tests/DebateLearningPhaseBatchVerifier.cs`

- [x] **Step 1: Request Unity recompile** after external script edits and inspect all compiler errors/warnings.
- [x] **Step 2: Run the focused EditMode tests** for content/metrics/logger and inspect the full result.
- [x] **Step 3: Run the batch verifier in `Level_NPCVsNPCDebate`**, inject the five final transcripts through the same callback, and inspect the final CSV for participant, condition, five final values, completion, and re-record count.
- [x] **Step 4: Manually inspect the Game View in Play Mode** for the world-space topic, Claim 1/5 display, read-only transcript, disabled controls while listening/transcribing, and contextual confirmation labels.
