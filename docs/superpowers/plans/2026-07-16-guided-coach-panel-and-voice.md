# Guided Coach Panel and Voice Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Move the scene-04 Guided CREEI study UI to a compact scrollable upper-left panel and make Anna speak every learner-visible Coach suggestion once.

**Architecture:** `GuidedCreeiStudyView` will own the centered setup layout, upper-left study layout, internal scrolling, and voice-status label. `GuidedCreeiStudyController` will build an exact-speech prompt, interrupt superseded speech, poll `ConvaiNPC.IsCharacterTalking`, gate recording, and clear speech state safely. Existing diagnosis, feedback, policy, logging, ASR, and scene wiring remain unchanged.

**Tech Stack:** Unity 6, C#, uGUI, TextMeshPro, Convai `ConvaiNPC`, NUnit Edit Mode tests, Funplay Unity MCP.

**Workspace note:** The project already contains unrelated uncommitted changes. Do not create commits, stage files, or rewrite unrelated assets.

---

### Task 1: Lock the setup and study layout contracts

**Files:**
- Modify: `Assets/Editor/Tests/GuidedCreeiStudyTests.cs`
- Modify: `Assets/Game/Scripts/GuidedCreeiStudyView.cs`

- [ ] **Step 1: Write failing layout tests**

Extend `GuidedViewBuildsScrollableTranscriptAndThreeSuggestionSlots` and add a focused test that obtains the view root through `view.RootRect`, calls `ShowSetup()`, then calls `ShowStage(...)`:

```csharp
view.ShowSetup();
Assert.AreEqual(new Vector2(0.5f, 0.5f), view.RootRect.anchorMin);
Assert.AreEqual(new Vector2(940f, 700f), view.RootRect.sizeDelta);

view.ShowStage(GuidedPracticeStageKind.Claim, "Task", "Ready", false);
Assert.AreEqual(new Vector2(0f, 1f), view.RootRect.anchorMin);
Assert.AreEqual(new Vector2(16f, -16f), view.RootRect.anchoredPosition);
Assert.AreEqual(420f, view.RootRect.sizeDelta.x);
Assert.IsNotNull(view.StudyScrollRect);
Assert.IsTrue(view.StudyScrollRect.vertical);
Assert.IsFalse(view.StudyScrollRect.horizontal);
```

- [ ] **Step 2: Run the Guided tests and verify RED**

Run `Game.Tests.EditMode.GuidedCreeiStudyTests` through Unity Test Runner.

Expected: compilation/test failure because `RootRect` and `StudyScrollRect` do not exist and the study root is still centered.

- [ ] **Step 3: Implement minimal layout switching and scrolling**

In `GuidedCreeiStudyView`:

```csharp
public RectTransform RootRect => _root == null ? null : _root.GetComponent<RectTransform>();
public ScrollRect StudyScrollRect => _studyScrollRect;

private void ApplySetupLayout()
{
    RectTransform rect = RootRect;
    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0.5f, 0.5f);
    rect.anchoredPosition = Vector2.zero;
    rect.sizeDelta = new Vector2(940f, 700f);
}

private void ApplyStudyLayout()
{
    RectTransform rect = RootRect;
    rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0f, 1f);
    rect.anchoredPosition = new Vector2(16f, -16f);
    rect.sizeDelta = new Vector2(420f, Mathf.Min(680f, Screen.height - 32f));
}
```

Create a `ScrollRect` + `RectMask2D` viewport between `_root` and `_studyPanel`; configure vertical-only scrolling and use the existing `_studyPanel` as its content. `ShowSetup` calls `ApplySetupLayout`; all study states reach `SetPanels(false, true)`, which calls `ApplyStudyLayout`.

- [ ] **Step 4: Run Guided tests and verify GREEN**

Expected: the new layout tests and all existing Guided tests pass.

---

### Task 2: Lock Coach voice prompt and recording-gate behavior

**Files:**
- Modify: `Assets/Editor/Tests/GuidedCreeiStudyTests.cs`
- Modify: `Assets/Game/Scripts/GuidedCreeiStudyController.cs`

- [ ] **Step 1: Write failing voice-contract tests**

Add tests for the wished-for pure helpers:

```csharp
[Test]
public void CoachSpeechPromptRequestsExactVisibleFeedbackOnce()
{
    string prompt = GuidedCreeiStudyController.BuildCoachSpeechPrompt("Use one specific example.");
    StringAssert.Contains("Use one specific example.", prompt);
    StringAssert.Contains("exact", prompt.ToLowerInvariant());
    StringAssert.Contains("once", prompt.ToLowerInvariant());
}

[TestCase(false, false, true)]
[TestCase(true, false, false)]
[TestCase(false, true, false)]
public void RecordingGateBlocksPendingOrActiveCoachSpeech(
    bool speechPending, bool characterTalking, bool expected)
{
    Assert.AreEqual(expected,
        GuidedCreeiStudyController.CanStartRecording(speechPending, characterTalking));
}
```

- [ ] **Step 2: Run the Guided tests and verify RED**

Expected: compilation failure because the two helpers do not exist.

- [ ] **Step 3: Implement the pure helpers**

```csharp
public static string BuildCoachSpeechPrompt(string feedback) =>
    "Speak only the exact English Coach feedback inside brackets once. " +
    "Do not paraphrase, answer, or add any words. Feedback: [" +
    (feedback ?? string.Empty).Trim() + "]";

public static bool CanStartRecording(bool speechPending, bool characterTalking) =>
    !speechPending && !characterTalking;
```

- [ ] **Step 4: Run Guided tests and verify GREEN**

Expected: helper tests and all prior Guided tests pass.

---

### Task 3: Add visible Coach speech state and runtime lifecycle

**Files:**
- Modify: `Assets/Editor/Tests/GuidedCreeiStudyTests.cs`
- Modify: `Assets/Game/Scripts/GuidedCreeiStudyView.cs`
- Modify: `Assets/Game/Scripts/GuidedCreeiStudyController.cs`

- [ ] **Step 1: Write a failing view-state test**

After building the view, call:

```csharp
view.SetCoachSpeaking(true);
Assert.AreEqual("Coach speaking…", view.CoachVoiceStatusText);
view.SetCoachSpeaking(false);
Assert.AreEqual(string.Empty, view.CoachVoiceStatusText);
```

Expected: compilation failure because the voice-status API does not exist.

- [ ] **Step 2: Add the voice-status label**

Create `_coachVoiceStatus` immediately after `_feedback`, expose `CoachVoiceStatusText`, and implement:

```csharp
public void SetCoachSpeaking(bool speaking)
{
    if (_coachVoiceStatus == null) return;
    _coachVoiceStatus.text = speaking ? "Coach speaking…" : string.Empty;
    _coachVoiceStatus.gameObject.SetActive(speaking);
}
```

- [ ] **Step 3: Implement the controller speech lifecycle**

Add `_coachSpeechPending`, `_coachSpeechStarted`, and `_coachSpeechDeadline`. At the start of each `Update`, poll `coachNPC.IsCharacterTalking`:

```csharp
bool talking = coachNPC != null && coachNPC.IsCharacterTalking;
if (talking)
{
    _coachSpeechStarted = true;
    studyView?.SetCoachSpeaking(true);
}
else if (_coachSpeechStarted ||
         (_coachSpeechPending && Time.realtimeSinceStartup >= _coachSpeechDeadline))
{
    _coachSpeechPending = false;
    _coachSpeechStarted = false;
    studyView?.SetCoachSpeaking(false);
}
```

Update `SpeakCoachFeedback` to call `StopCoachSpeech()`, set a 15-second start deadline, and send `BuildCoachSpeechPrompt(feedback)`. Update `StopCoachSpeech()` to clear all flags/status after interrupting Anna. At the top of `StartRecording`, reject recording when `CanStartRecording(_coachSpeechPending, coachNPC?.IsCharacterTalking == true)` is false.

- [ ] **Step 4: Verify every learner-visible feedback path triggers speech**

Keep the single call to `SpeakCoachFeedback(_latestFeedback)` inside `NotifyFeedbackPresented`, because all visible LearnerLed, SharedControl, and AiLed suggestions converge there. Confirm working/diagnosis/technical-error methods do not call it.

- [ ] **Step 5: Run Guided tests and full Edit Mode suite**

Expected: all Guided tests pass; full Edit Mode suite reports zero failures; Unity reports zero compilation errors.

---

### Task 4: Play Mode visual and voice acceptance

**Files:**
- Verify: `Assets/Game/Scenes/04 coach Agent.unity`
- Verify: `Assets/Game/Scenes/05Level_PlayerVsNPCDebate 1.unity`

- [ ] **Step 1: Open scene 04 and enter Play Mode**

Verify the setup panel remains centered and no console errors appear.

- [ ] **Step 2: Start each orchestration mode without changing scene wiring**

Verify the study panel moves to the upper-left, stays approximately 420 px wide, scrolls internally, and does not cover Anna.

- [ ] **Step 3: Perform one real network-backed feedback cycle**

Verify the visible feedback remains on screen, Anna speaks it once, `Coach speaking…` appears only while she talks, and the microphone does not start simultaneously.

- [ ] **Step 4: Verify interruption paths**

During Coach speech, use `Revise`, `Next Stage`, and one Skip path in separate runs. Verify Anna stops immediately and no late speech starts in the next stage.

- [ ] **Step 5: Exit Play Mode and leave scene 05 active**

Open scene 05, confirm its Transfer guard produces no Coach-component error, then exit Play Mode with scene 05 active and clean.
