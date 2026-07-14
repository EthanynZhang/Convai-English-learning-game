# NPC Debate Speaking Tutorial Narration Design

## Goal

Update `Level_NPCVsNPCDebate` so the learning canvas is fully visible, every tutorial stage is introduced by Anna in English, external TTS audio drives visible mouth movement, and all practice material uses the reading-versus-speaking topic with a speaking-first stance.

## Scope

- Move the runtime world-space learning canvas upward without changing its size or rotation.
- Keep the existing keyboard navigation and tutorial-time Convai conversation suppression.
- Preserve Anna/Mike fixed two-speaker dialogue demos.
- Add an Anna narration before each non-terminal stage. Demo dialogue starts after the narration finishes.
- Replace AI-use and reading-first practice examples with speaking-first reading-versus-speaking material.
- Use MiniMax synchronous TTS with a persistent local WAV cache and RT-Voice as a fallback.
- Drive `jawOpen` from the actual NPC AudioSource output for MiniMax, RT-Voice, and cached clips.

## Content

The shared topic is:

`Reading and speaking, which is more important in learning English?`

Practice examples model this stance:

`Speaking is more important for learning English.`

CREEI examples use speaking practice, active retrieval, immediate feedback, real conversations, practical confidence, and communication readiness. Logos, Ethos, and Pathos demonstrations all show different ways to support speaking practice. The five-step voice practice remains mandatory and collects Claim, Reason, Evidence, Explanation, and Impact in that order.

## Narration Flow

1. Enter a stage and render its fixed local UI text immediately.
2. Stop audio left from the previous stage.
3. Build a concise English narration from the stage title and body.
4. Request or load Anna's MiniMax audio and play it through Anna's AudioSource.
5. If MiniMax is unavailable, use the existing Anna RT-Voice path.
6. For demo stages, start the Anna/Mike fixed dialogue only after narration completes or fails.
7. Left/right navigation cancels the active narration and starts the destination stage cleanly.

The terminal `StartDebate` stage is not narrated because it immediately hands control to the existing round manager.

## MiniMax TTS

- Endpoint: `https://api.minimax.io/v1/t2a_v2`
- Model: `speech-02-hd`
- Voice: `English_Graceful_Lady`
- Language boost: `English`
- Output: non-streaming WAV, 32 kHz, mono
- Authentication: Windows user environment variable `MINIMAX_API_KEY`
- Cache: `Application.persistentDataPath/minimax_tts_cache`
- Cache key: SHA-256 of model, voice, speed, and text

The API key must never be serialized into a Unity scene, asset, source file, log, or CSV.

## Lip Sync

A reusable audio-driven component watches the target NPC AudioSource. While audio is playing, it measures RMS output and maps it to the head mesh's `jawOpen` blend shape with smoothing. It also updates the animator `Talk` parameter. When playback stops or the component is disabled, it restores the mouth and talk state to neutral.

## Error Handling

- Missing API key: warn once without printing the key and use RT-Voice fallback.
- HTTP/API/JSON/audio failure: log a concise status and use RT-Voice fallback.
- Cache corruption: delete the invalid cache entry and regenerate it.
- Navigation during a request: cancel the old narration token so late responses cannot play in the new stage.
- Missing jaw blend shape: keep the talk animation and audio working, log one warning, and do not throw.

## Verification

- EditMode tests verify the topic and stance across all practice content, the five ordered CREEI prompts, safe MiniMax request/cache behavior, and source-level stage narration/lip-sync wiring.
- PlayMode verification checks canvas position, no old Next/Previous buttons, Anna narration ordering, demo handoff, audio-driven jaw movement, navigation cancellation, and continued suppression of ordinary Convai conversation.
- Regression verification confirms the formal debate still starts only after the tutorial and other debate scenes are unchanged.
