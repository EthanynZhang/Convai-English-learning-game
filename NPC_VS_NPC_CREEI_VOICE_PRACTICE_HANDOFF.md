# NPC vs NPC CREEI Voice Practice - Development Handoff

## Goal

Extend the existing `Micro Practice 2: One Sentence Try` in
`Assets/Game/Scenes/Level_NPCVsNPCDebate.unity` into one required, five-step
voice practice. The learner builds one complete CREEI argument by speaking one
sentence for each component: Claim, Reason, Evidence, Explanation, and Impact.

This is a shared pre-debate learning activity, not a scored assessment. The
only completion criterion is a non-empty Convai speech-to-text result for every
step. Do not use an LLM, keyword scoring, or semantic correctness gate.

## Locked Product Decisions

- Keep one main learning stage: `MicroPracticeOneSentence`; do not add five
  top-level entries to `DebateLearningContent.StageSequence`.
- Inside that stage, use five required substeps in this fixed order:
  `Claim -> Reason -> Evidence -> Explanation -> Impact`.
- The user presses `T` once to start recording and presses `T` again to stop.
  This is a toggle, not hold-to-talk.
- Convai returns the transcript. Display it in a read-only field in the learning
  panel. Keyboard editing is prohibited.
- The user may press `T` again after a transcript arrives to re-record the
  current substep; the latest non-empty transcript replaces the prior one.
- Right arrow / keypad `6` (and the existing Next button) confirms a populated
  substep and advances to the next CREEI component. On Impact, it advances to
  `Strategy Reading`.
- A blank transcript, microphone failure, speech stream failure, or an
  in-progress recording keeps confirmation disabled. There is no skip path to
  the next main learning stage.
- Left arrow / keypad `4` keeps its existing Previous behavior only while not
  recording. Returning later restores the first unfinished CREEI component and
  preserves already confirmed transcripts.
- Default reference duration is four minutes. It is for UI/logging only; do not
  force-complete, auto-skip, or reject slow participants when the time elapses.

## Learner Flow

The existing high-level sequence remains unchanged around this activity:

```text
CREEI Structure Study
-> Micro Practice 2: Build Your CREEI Argument (five voice substeps)
-> Strategy Reading
```

The panel must show the following content for the entire practice:

```text
Debate topic
Should students be allowed to use AI in class and when completing homework?
```

For every substep show `CREEI Step X / 5`, the component name, the prompt,
recording status, and the read-only transcript display.

| Step | Component | Prompt |
| --- | --- | --- |
| 1 | Claim | State your position in one clear sentence: should students be allowed to use AI in class and when completing homework? |
| 2 | Reason | Give one reason why your claim makes sense. |
| 3 | Evidence | Give one fact, example, observation, or personal experience that supports your reason. |
| 4 | Explanation | Explain how your evidence supports your reason and claim. |
| 5 | Impact | Explain why this matters for students, teachers, or learning. |

Required UI states:

1. `Press T to start speaking.` The current transcript is empty and confirm is disabled.
2. `Listening... Press T to stop.` All navigation and replay controls are disabled.
3. `Transcribing...` Controls remain disabled until Convai emits its final user transcript.
4. `Transcript ready. Press Right Arrow or Confirm to continue.` The transcript is read-only. Confirm is enabled and `T` re-records the same component.
5. After confirmation, show the next component with an empty transcript. After
   confirming Impact, leave the practice stage normally.

Use the current world-space canvas and its existing keyboard navigation. Rename
the visible Next label contextually to `Confirm Claim`, `Confirm Reason`, and
so on. On the fifth component it is `Finish CREEI Practice`. Do not add answer
options, typed-answer fields, scores, Coach feedback, or a multiple-choice UI.

## Implementation Design

### Content and state

Modify `Assets/Game/Scripts/DebateLearningContent.cs`.

- Replace the reflection-only `OneSentenceTryText` with a concise introduction
  to the five required spoken sentences and the AI-in-class/homework topic.
- Add an immutable content model, for example
  `CreeiVoicePracticePrompt { CreeiPartKey Part; string Title; string Prompt; }`,
  and a fixed array containing the five rows above. Do not derive the prompt
  order from UI labels or enum string formatting.
- Keep `MicroPracticeOneSentence` as the one existing stage key and retain its
  `MicroPractice` view kind.
- Change `OneSentencePracticeSeconds` to `240f`.

Modify `Assets/Game/Scripts/NpcDebateLearningPhaseController.cs`.

- Add an internal practice state with: current substep index, per-part
  transcripts, recording flag, awaiting-transcript flag, and per-part re-record
  counts.
- Build a dedicated read-only transcript display and a status line inside the
  current world-space panel. Reuse the current dynamic UI construction pattern;
  do not open Convai Chat UI and do not create a separate screen-space canvas.
- While `CurrentStage.Key == MicroPracticeOneSentence`, override the existing
  right-arrow / Next behavior to confirm only the current non-empty transcript.
  The global `AdvanceStage()` must refuse to leave this stage until all five
  parts have been confirmed.
- Handle `T` in the learning controller before normal learning keyboard actions:
  first press calls `StartListening()` on the assigned practice NPC; second
  press calls `StopListening()`. Reuse `primaryDemoNPC` as the practice NPC and
  set it active before recording.
- Register an owner-safe callback in
  `ConvaiGRPCAPI.TryHandleUserVoiceTranscript` only while this voice practice is
  active. When Convai provides a final non-empty transcript, route it only to
  the current CREEI substep, set the state to transcript-ready, and return
  `true`. Returning `true` is essential because the existing Convai API then
  suppresses the NPC response for that voice turn.
- During this stage, register the existing Convai input suppressors so `T` does
  not start normal player-to-NPC talk, terminate the NPC2NPC debate, or trigger
  a second microphone session. The controller still invokes its selected
  practice NPC directly.
- Clear every static Convai callback only if it still points to this controller.
  Do so on leaving the voice-practice stage, `OnDisable`, and `OnDestroy`; this
  prevents cross-scene leakage into Interactive Debate or Coach scenes.
- Re-recording replaces only the current component. Confirmed earlier parts
  remain visible in a compact `Completed: Claim, Reason...` summary, but are not
  editable.
- For microphone absence, recording startup failure, a closed/failed stream, or
  an empty final transcript: show a plain retry status, preserve the current
  step, and leave confirmation disabled. Never forward the transcript to Anna,
  Mike, the referee, or the formal debate manager.

### Research logging

Modify `Assets/Game/Scripts/DebateLearningMetrics.cs` and
`Assets/Game/Scripts/DebateLearningLogger.cs`.

- Preserve the current CSV columns for compatibility. Set the existing
  `micro_practice_2_template_choice` to `CREEI_voice_5_step` and store a
  newline-separated labeled aggregate in `micro_practice_2_short_text`.
- Add these append-only CSV columns and matching metric properties:

```text
micro_practice_2_claim
micro_practice_2_reason
micro_practice_2_evidence
micro_practice_2_explanation
micro_practice_2_impact
micro_practice_2_completed
micro_practice_2_rerecord_count
```

- Record only confirmed transcripts in these fields; do not store raw audio.
- Increment the re-record count when a ready transcript is deliberately
  replaced by another `T` recording.
- The existing `micro_practice_total_time` and stage logs remain the source of
  practice timing. All CSV values continue through `EscapeCsv`.

### Input and regression boundaries

- Do not modify the formal NPC vs NPC round timing, referee opening, NPC2NPC
  relay, demo playback, or RT-Voice presentation code.
- Do not change `Level_PlayerVsNPCDebate`, `Level_InteractiveNPCDebate`, or
  `Level_SharedInitiativeOrchestration` behavior.
- `T` retains its standard Convai meaning outside the five-substep practice.
- Existing `Previous`, `Replay Demo`, Next, arrow-key, and keypad mappings stay
  intact outside this special stage.

## Tests and Verification

Update `Assets/Editor/Tests/DebateLearningContentTests.cs`.

- Verify the voice practice prompt array has exactly five entries, in CREEI
  order, and each prompt includes the intended component.
- Verify the topic explicitly includes both in-class AI use and homework AI
  use.
- Verify the voice practice reference duration is 240 seconds.
- Verify metrics retain all five confirmed transcripts, aggregate them in the
  legacy field, mark completion only after Impact, and count re-records.
- Verify CSV escaping handles commas, quotes, and newline-containing transcripts
  in the new fields.

Update `Assets/Editor/Tests/DebateLearningPhaseBatchVerifier.cs` or add a
focused verifier for the controller.

1. Move from CREEI Structure Study into `MicroPracticeOneSentence`.
2. Confirm that Next/right arrow is disabled before a transcript exists.
3. Inject a final transcript through the same controller callback used by
   Convai; verify Claim becomes ready and does not invoke an NPC reply.
4. Confirm it with right arrow and verify progression to Reason.
5. Repeat through Impact, including one re-record replacement.
6. Verify only after Impact can the flow enter Strategy Reading.
7. Verify the CSV contains all five final transcripts, completion=true, the
   re-record total, participant ID, and condition.
8. Verify unloading the practice stage unregisters input/transcript callbacks;
   another scene's normal `T` handling must remain available.

Manual Play Mode acceptance in `Level_NPCVsNPCDebate`:

1. Enter the learning phase and navigate to `Micro Practice 2`.
2. See the AI-in-class/homework topic and `Claim 1 / 5` prompt.
3. Press `T`, speak, then press `T` again. Confirm the fixed learning panel
   receives a read-only transcript and no NPC speaks back.
4. Press `T` again to replace the current transcript, then use Right Arrow to
   confirm it.
5. Complete Reason, Evidence, Explanation, and Impact the same way.
6. Confirm Strategy Reading remains unreachable until Impact is confirmed.
7. Complete the rest of the learning flow and verify the formal NPC debate
   still starts normally after the learning phase.
