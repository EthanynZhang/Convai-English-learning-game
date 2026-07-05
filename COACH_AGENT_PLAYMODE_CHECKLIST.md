# Coach Agent Play Mode Acceptance Checklist

Scene: `Assets/Game/Scenes/Level_SharedInitiativeOrchestration.unity`

Purpose: verify the Condition C Practice Debate Coach Agent MVP path after Unity recompiles without errors.

## Setup

- Open `Level_SharedInitiativeOrchestration.unity`.
- Confirm `Debate Round System` has `SharedInitiativeOrchestrationController` enabled.
- Confirm `InteractiveDebateTranscriptBridge` is on the same GameObject and assigned to `transcriptBridge`.
- Confirm `Round Timer` is inactive or not visible.
- Confirm the old Interactive NPC Debate controls are hidden during play.
- Confirm Convai voice input still uses the `T` hold-to-talk path.

## Main Flow

1. Enter Play Mode.
2. Click `Start Conversation`.
3. Verify the opponent NPC speaks first and the Coach does not speak before the player responds.
4. After the opponent finishes, hold `T` and give a short player debate response.
5. Verify the player transcript appears in the panel and in the Convai transcript UI.
6. Verify Coach feedback appears automatically after the player transcript.
7. Verify the feedback is short Level 2 post-turn advice and does not write a full answer for the player.
8. Optionally click `Need an example?`.
9. Verify the optional Level 3 response is a short sentence frame or example fragment, not a full player answer.
10. Click `Continue`.
11. Verify the next opponent turn starts only after `Continue`.
12. Verify no Coach feedback is triggered again until the player speaks after that opponent turn.
13. Repeat until three player turns are complete.
14. After the third turn, click `Finish & Show Summary`.
15. Verify the Coach summary appears and the session enters the complete state.

## Logging Checks

- Confirm `Application.persistentDataPath/debate_coach_logs.csv` is created.
- Confirm each Coach row includes participant, condition, stage, topic, turn, opponent utterance, player utterance, selected strategy, feedback level, CREEI fields, strategy fields, feedback text, example flag, and timestamps.
- Confirm `timestamp_next_player_turn_started` is filled when `Continue` moves to the next opponent turn.

## Negative Checks

- Do not see Coach feedback before the player speaks.
- Do not see Coach feedback during Transfer Debate or unrelated scenes.
- Do not see automatic continuation immediately after player speech; `Continue` must be required.
- Do not see the 3-minute round timer in this scene.
