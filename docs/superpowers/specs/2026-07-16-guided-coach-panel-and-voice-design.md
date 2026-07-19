# Guided Coach Panel and Voice Design

## Scope

This change applies only to the Guided CREEI flow in scene 04. It changes the runtime study-panel placement and guarantees that every learner-visible Coach suggestion is spoken by Anna. It does not change diagnosis, feedback content, mode policy, research setup, or scene 05.

## Layout

- The researcher setup panel remains centered at its current size.
- After `Start Study`, the runtime study panel switches to a fixed upper-left layout.
- Target width: 420 px.
- Margins: 16 px from the left and top edges.
- Maximum height: current canvas height minus 32 px, capped at 680 px.
- The panel contains stage instructions, transcript confirmation, requests, suggestion cards, feedback, technical errors, and action buttons.
- Overflow scrolls inside the panel. The panel must not expand toward the center of the screen or cover Anna.
- Returning to researcher setup restores the centered layout.

## Coach Voice

- Only learner-visible Coach feedback is spoken. Research setup, working status, diagnosis status, and technical-error text remain silent.
- When feedback becomes visible, the controller first interrupts any previous Anna speech, then sends one exact-feedback speech request through the wired `ConvaiNPC`.
- The view displays `Coach speaking…` while Anna reports that she is talking, and clears the state when speech ends.
- A newer suggestion supersedes and interrupts an older one.
- `Revise`, `Next Stage`, `Safety Skip`, `Technical Skip`, component disable, and scene exit stop current Coach speech.
- Recording cannot begin while Anna is talking. This preserves microphone/TTS mutual exclusion.
- If Convai speech fails, the on-screen feedback remains usable and the study flow is not blocked.

## Implementation Boundaries

- `GuidedCreeiStudyView` owns centered-versus-upper-left layout and the internal scroll viewport.
- `GuidedCreeiStudyController` owns Coach speech requests, talking-state subscription, recording gate, and cleanup.
- Existing `speakCoachFeedback` and the scene-wired Anna `ConvaiNPC` remain the configuration source.
- No fallback synthetic voice is added in this change.

## Verification

- Edit Mode tests verify the centered setup layout, upper-left study layout, scroll viewport, exact-feedback speech prompt, and recording suppression while Coach speech is active.
- Scene contract tests verify Anna and the Guided controller/view references remain wired.
- Play Mode acceptance verifies that the panel does not cover Anna, visible feedback triggers Anna, `Coach speaking…` follows the talking state, and Revise/Next/Skip stop speech.
- Real Convai voice output remains a manual network-dependent acceptance check.
