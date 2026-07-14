# Convai English Learning Game - Development Update

Date: 2026-07-14

## Overview

This update extends the English debate game with a CREEI-based Coach Agent flow, continuous English speech transcription, a three-minute Mock Debate, and a separate AI-homework debate scene. The work is based on the latest remote `master` branch and keeps the existing Convai NPC integration.

## Coach Agent Scene

Scene: `Assets/Game/Scenes/04 coach Agent.unity`

- Splits guided practice into `Claim`, `Reason`, `Evidence`, `Explanation`, `Impact`, and `Mock Debate` stages.
- Adds stage-selection buttons so the learner can enter a specific stage directly.
- Generates Leo's stage statement independently from the learner's latest answer.
- Records the learner with tap-to-start/tap-to-stop `T` input during normal CREEI stages.
- Starts GPT analysis automatically after the learner finishes speaking. `Ask Anna` is enabled only after valid feedback is ready.
- Supports `Ask Anna`, `Need an example?`, `Continue`, and `End Session` choices.
- Makes `Need an example?` generate a concrete model sentence based on the learner's response and the previous Coach advice, then sends it to Anna automatically.
- Adds minimal/detail JSON modes. Minimal mode returns only `feedback_text`; detail mode returns the complete structured analysis.
- Forces Coach feedback and examples to be generated in English.
- Adds a GPT Debug panel that shows the request prompt, raw response, parsing result, HTTP/API errors, and the prompt sent to Convai.
- Uses Anna's Convai voice for Coach delivery, with retry and silence detection.
- Removes the previous topic/status/transcript/Coach text blocks from the left control panel.
- Adds a world-space whiteboard behind the NPCs. The board stays visible, while its title, stage, and feedback text appear only when Anna starts speaking.
- Keeps NPC speech captions above the active speaker and disables the legacy overlapping Convai speech bubbles.
- Uses right mouse button to switch between UI cursor mode and first-person look mode.

## Mock Debate

- Adds a sixth stage after the five CREEI practice stages.
- Leo delivers a preset complete CREEI speech first.
- The learner then receives a three-minute speaking turn.
- Pressing `T` once starts continuous recording; recording stops automatically at `03:00`.
- Silence does not end the recording session and does not cause Leo to interrupt.
- The complete learner transcript is sent to GPT after recording ends.
- GPT evaluates the full Claim, Reason, Evidence, Explanation, and Impact structure after the speech is complete.
- The final Coach summary is prepared automatically, but Anna speaks only after the learner presses `Ask Anna`.
- The countdown is displayed at the top center of the screen.

## AI Homework CREEI Debate Scene

Scene: `Assets/Game/Scenes/Level_AIHomeworkCreeiDebate.unity`

Topic: "Should students be allowed to use AI for homework?"

- Adds a separate debate scene without a Coach Agent.
- Leo speaks first using a preset complete CREEI argument.
- Leo uses Convai when available and falls back to local Windows English TTS if Convai produces no audio.
- The learner speaks second for up to three minutes.
- Uses continuous iFlytek English transcription so short and long pauses do not terminate the recording.
- Keeps the complete player and Leo transcripts instead of truncating them.
- Displays both transcripts in fixed-height scroll views with automatic scrolling to the latest text.
- Adds the scene to Unity Build Settings.

## Voice And Transcription Changes

- Changes normal-stage voice interaction from hold-to-talk to tap `T` to start and tap `T` again to stop.
- Removes the old 30-second recording limit for Coach practice stages.
- Uses a looping microphone buffer for longer Convai recording sessions.
- Preserves the final tail chunk of Convai user transcription.
- Handles expected voice-stream cancellation without logging it as a fatal error.
- Adds `XfyunRealtimeTranscriber` for continuous 16 kHz English realtime transcription.
- Sends 40 ms PCM packets and merges corrected realtime segments instead of repeatedly appending duplicate preview text.
- Keeps recording active through silence until the learner manually stops or the three-minute limit is reached.

## GPT And Prompt Logic

- Uses stage-specific CREEI definitions and guidance for Coach feedback, examples, and Leo statements.
- Keeps Leo's normal-stage statement independent from the learner's response.
- Rejects advice-like output when a concrete example was requested.
- Retries transient GPT/network failures and reports HTTP status, timeout, parsing, and validation details in the debug UI.
- Disables generic local feedback such as "Your claim is understandable" so it cannot be confused with a GPT response.
- Sends only the parsed speakable `feedback_text` to Convai, not the full JSON object.

## API Configuration

The following credentials are intentionally stored in this project at the project owner's request. Environment variables and PlayerPrefs still take precedence, so credentials can be replaced without changing source code.

### GPT Relay

- Endpoint: `https://api.meding.site/v1/chat/completions`
- Model: `gpt-4o-mini`
- API key: `sk-jp3lFBmZA8Jv7He2ulCkpJvQUsR2MkL1kCyvIopmNGGs40c8`
- Built-in fallback location: `Assets/Game/Scripts/DebateCommandParser.cs`
- Override environment variables: `DEBATE_OPENAI_API_KEY`, `OPENAI_API_KEY`, `DEBATE_OPENAI_BASE_URL`, `OPENAI_BASE_URL`
- Override PlayerPrefs keys: `DEBATE_OPENAI_API_KEY`, `DEBATE_OPENAI_BASE_URL`

### iFlytek Realtime Speech Transcription

- Endpoint: `wss://rtasr.xfyun.cn/v1/ws`
- App ID: `309e7d3c`
- API key: `739024599abf08cd804bafad624dc704`
- Recognition language: English (`lang=en`, `pd=edu`, `vadMdn=2`)
- Built-in fallback location: `Assets/Game/Scripts/XfyunRealtimeTranscriber.cs`
- Override environment variables: `XFYUN_RTASR_APP_ID`, `XFYUN_RTASR_API_KEY`
- Override PlayerPrefs keys: `XFYUN_RTASR_APP_ID`, `XFYUN_RTASR_API_KEY`

### Convai

- Service website: `https://api.convai.com`
- API key: `14abc9fef8fdadfedee9c6187162fcd5`
- Project asset: `Assets/Resources/ConvaiAPIKey.asset`
- Used for NPC text-to-speech, voice delivery, character audio, lip sync, and NPC interaction.

### MiniMax TTS

- Endpoint: `https://api.minimax.io/v1/t2a_v2`
- Environment variable: `MINIMAX_API_KEY`
- No MiniMax API key was supplied in this development session, so no MiniMax key is committed.
- The existing scene can fall back to RT-Voice/Convai when MiniMax is unavailable.

## Main Files

- `Assets/Game/Scripts/SharedInitiativeOrchestrationController.cs`
- `Assets/Game/Scripts/DebateCoachFeedbackGenerator.cs`
- `Assets/Game/Scripts/CreeiOpponentStatementGenerator.cs`
- `Assets/Game/Scripts/MockDebateEvaluationGenerator.cs`
- `Assets/Game/Scripts/XfyunRealtimeTranscriber.cs`
- `Assets/Game/Scripts/AiHomeworkCreeiDebateController.cs`
- `Assets/Convai/Scripts/Runtime/Core/ConvaiGRPCAPI.cs`
- `Assets/Convai/Scripts/Runtime/Core/ConvaiInputManager.cs`
- `Assets/Convai/Scripts/Runtime/Core/ConvaiNPC.cs`
- `Assets/Editor/Tests/SharedInitiativeCoachAgentTests.cs`

## Validation

- Unity script compilation completed without errors.
- Coach Agent stage flow, GPT prompt construction, detailed/minimal JSON modes, concrete-example validation, long voice capture, transcript-tail preservation, Mock Debate flow, AI Homework scene flow, and scrollable transcript UI are covered by edit-mode tests.
- Coach whiteboard visibility was verified in Play Mode: the board is visible before speech, prepared text stays hidden, and text appears when Coach speech begins.

## Credential Warning

This repository contains live API credentials in source-controlled files. Anyone with repository access can use them. Rotate all listed keys before making the repository public or distributing a build outside the intended team.
