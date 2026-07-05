# Convai English Learning Game - Next Window Handoff

## Project

- Unity project path: `D:\unity project\Convai-English-learning-game`
- Current active development context has been Unity + FunPlay MCP.
- Main scenes:
  - `Assets/Game/Scenes/Level_PlayerVsNPCDebate.unity`
  - `Assets/Game/Scenes/Level_NPCVsNPCDebate.unity`
  - `Assets/Game/Scenes/Level_InteractiveNPCDebate.unity`
  - `Assets/Game/Scenes/Level_SharedInitiativeOrchestration.unity`
- The project uses Convai Unity SDK, NPC2NPC conversation flow, TextMeshPro UI, Unity Input System, and FunPlay MCP for editor-side verification.

## Important Instruction For The Next Window

Treat `Level_InteractiveNPCDebate.unity` / Interactive MPC Debate / Interactive NPC Debate as a fresh task.

Do not rely on the current experimental progress for that scene. The next window should inspect the scene, scripts, and current requirements from scratch before implementing anything. Any existing experimental command-mode changes around the interactive scene should be considered provisional and not authoritative.

## Completed Work To Preserve

### NPC vs NPC Reading & Demo Learning Phase

The NPC vs NPC debate scene has a required linear learning phase before the formal NPC vs NPC debate.

Scene:

- `Assets/Game/Scenes/Level_NPCVsNPCDebate.unity`

Key runtime scripts:

- `Assets/Game/Scripts/NpcDebateLearningPhaseController.cs`
- `Assets/Game/Scripts/DebateLearningContent.cs`
- `Assets/Game/Scripts/DebateLearningLogger.cs`
- `Assets/Game/Scripts/DebateLearningMetrics.cs`
- `Assets/Game/Scripts/NpcDebateRoundManager.cs`

Implemented learning order:

```text
Warm-up
-> CREEI Reading
-> CREEI Dialogue Demo
-> CREEI Structure Study
-> Strategy Reading
-> Logos Dialogue Demo
-> Ethos Dialogue Demo
-> Pathos Dialogue Demo
-> Micro-choice
-> Start Debate
```

Behavior:

- The learning phase appears before the formal NPC vs NPC debate.
- `NpcDebateRoundManager.waitForExternalStart` prevents the debate from auto-starting.
- The learning controller calls `BeginRound()` only after micro-choice.
- Demo dialogue uses fixed Anna/Mike transcript data.
- Demo audio is presentation-only; research data should use fixed UI text and timing logs.
- Demo should not use the normal NPC2NPC relay for learning-stage playback.
- Keyboard shortcuts were added because in-scene buttons are hard to click:
  - Learning phase `Next`: Right arrow
  - `Previous`: Left arrow
  - `Replay`: Down arrow
  - Micro-choice buttons from left to right:
    - Left arrow: first option
    - Down arrow: second option
    - Right arrow: third option

Logging:

- Learning logs write to:

```text
Application.persistentDataPath/debate_learning_logs.csv
```

Expected fields include participant, condition, stage, timestamps, card/demo/natural/structure view time, strategy viewed, micro-choice strategy, optional bad example flag, rewatch count, and total learning phase time.

## Local TTS Notes

There was investigation around Convai voices and fixed demo speech. For fixed research-grade demo dialogue, do not treat live Convai generation as ground truth. The stable data source is the fixed transcript shown in UI and recorded in logs.

If exact audio is required in the future, the better path is pre-recorded/generated audio clips rather than relying on Convai real-time generation to repeat text exactly.

Some local cached TTS resources may exist under:

```text
Assets/Resources/DebateLearningTts/
```

The next window should inspect the folder before changing or deleting anything.

## Tests And Verification Already Used

EditMode tests are under:

```text
Assets/Editor/Tests/
```

Known useful tests/verifiers:

- `DebateLearningContentTests.cs`
- `DebateLearningPhaseBatchVerifier.cs`

Verified expectations include:

- CREEI fixed content contains exactly:
  - Claim
  - Reason
  - Evidence
  - Explanation
  - Impact
- Strategy fixed content contains exactly:
  - Logos
  - Ethos
  - Pathos
- Learning stage sequence matches the 10-step order above.
- Every demo stage has fixed dialogue lines for Anna and Mike.
- CSV escaping handles commas, quotes, and newlines.
- Metrics accumulate card/demo/structure time and rewatch count.

Manual acceptance for NPC vs NPC:

- Opening `Level_NPCVsNPCDebate` should show the learning UI first.
- Formal debate timer and first speaker prompt should not start before micro-choice.
- CREEI demo should show Anna/Mike dialogue with transcript.
- Logos, Ethos, and Pathos demos should appear independently.
- After micro-choice, learning UI closes and the existing NPC vs NPC debate starts.
- CSV log should contain participant, condition, micro-choice strategy, demo time, and rewatch count.

## Regression Areas

Do not break:

- `Level_PlayerVsNPCDebate`
- `Level_NPCVsNPCDebate`
- F8 pause menu scene switching
- NPC vs NPC referee opening/ending/timer/cleanup
- Existing Convai NPC2NPC debate flow

## Worktree And Git Caution

The project may have a dirty working tree with user and previous-agent changes. Do not run destructive git commands. Do not revert files unless explicitly asked.

Before new work:

1. Check current branch:

```powershell
git -C "D:\unity project\Convai-English-learning-game" branch --show-current
```

2. Check status:

```powershell
git -C "D:\unity project\Convai-English-learning-game" status --short
```

3. Inspect relevant scripts/scenes before editing.

## Security And Keys

Do not commit API keys or relay keys into Unity assets, C# files, Markdown files, or scene YAML.

If an API key or third-party relay is needed, use local environment variables, Unity `PlayerPrefs`, or another local-only configuration mechanism. Ask the user before changing credential storage.

## Recommended Starting Point For The Next Window

For new work on Interactive MPC/NPC Debate:

1. Ignore prior interactive-scene implementation assumptions.
2. Re-read the user’s fresh requirement.
3. Inspect:

```text
Assets/Game/Scenes/Level_InteractiveNPCDebate.unity
Assets/Game/Scripts/InteractiveNpcDebateController.cs
Assets/Convai/Scripts/Runtime/Core/ConvaiInputManager.cs
Assets/Convai/Scripts/Runtime/Core/ConvaiPlayerInteractionManager.cs
Assets/Convai/Scripts/Runtime/Core/ConvaiGRPCAPI.cs
Assets/Convai/Scripts/Runtime/Features/NPC2NPC/
```

4. Decide the proper interaction model from scratch:
   - button-based
   - typed command
   - Convai voice transcript command
   - hybrid

5. Write or update tests before implementation where practical.

