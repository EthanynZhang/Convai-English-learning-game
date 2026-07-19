using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Debate;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public class SharedInitiativeCoachAgentTests
    {
        private const string SharedInitiativeScenePath = "Game/Scenes/04 coach Agent.unity";
        private const string SharedInitiativeControllerGuid = "1bedf553246c4a589354b41b7f20d6df";
        private const string TranscriptBridgeGuid = "0ee0de56c9b14c74ae428d45f2e04d50";

        private static readonly Assembly RuntimeAssembly = typeof(SharedInitiativeOrchestrationController).Assembly;

        [Test]
        public void CoachFeedbackSchemaContainsFeedbackOnlyFields()
        {
            Type generatorType = GetRuntimeType("Game.Debate.DebateCoachFeedbackGenerator");
            Assert.IsNotNull(generatorType, "DebateCoachFeedbackGenerator should exist.");

            MethodInfo schemaMethod = generatorType.GetMethod(
                "BuildStructuredOutputSchema",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(schemaMethod, "BuildStructuredOutputSchema should be public for tests and prompt validation.");

            JObject schema = (JObject)schemaMethod.Invoke(null, null);
            JArray required = (JArray)schema["required"];
            JObject properties = (JObject)schema["properties"];

            CollectionAssert.AreEquivalent(
                new[]
                {
                    "feedback_type",
                    "feedback_level",
                    "feedback_text",
                    "next_action",
                    "target_success_criterion"
                },
                required.Select(token => (string)token).ToArray());

            Assert.IsNull(properties["strong_component"]);
            Assert.IsNull(properties["weak_component"]);
            Assert.IsNull(properties["dominant_strategy"]);
            Assert.IsNull(properties["recommended_strategy"]);
            CollectionAssert.Contains(
                ((JArray)properties["feedback_level"]["enum"]).Select(token => (string)token).ToArray(),
                "Level2");
            CollectionAssert.Contains(
                ((JArray)properties["feedback_level"]["enum"]).Select(token => (string)token).ToArray(),
                "Level3");
        }

        [Test]
        public void LevelPromptsEnforcePostTurnFeedbackBoundaries()
        {
            Type requestType = GetRuntimeType("Game.Debate.CoachFeedbackRequest");
            Type levelType = GetRuntimeType("Game.Debate.CoachFeedbackLevel");
            Type generatorType = GetRuntimeType("Game.Debate.DebateCoachFeedbackGenerator");
            Assert.IsNotNull(requestType);
            Assert.IsNotNull(levelType);
            Assert.IsNotNull(generatorType);

            object request = Activator.CreateInstance(requestType);
            SetField(request, "Topic", "Reading and speaking");
            SetField(request, "PlayerSide", "Speaking is more important.");
            SetField(request, "CurrentCreeiStage", "Reason");
            SetField(request, "ConfirmedFocus", "Reason");
            SetField(request, "DiagnosisIssueCode", "CREEI_REASON_WEAK");
            SetField(request, "TargetSuccessCriterion", "State one reason that directly supports the claim.");
            SetField(request, "OpponentUtteranceText", "Reading gives students vocabulary.");
            SetField(request, "PlayerUtteranceText", "Speaking is better because practice is important.");
            SetField(request, "FeedbackLevel", Enum.Parse(levelType, "Level2"));

            string level2Prompt = InvokeString(generatorType, "BuildPrompt", request);
            StringAssert.Contains("only provide short post-turn feedback", level2Prompt);
            StringAssert.Contains("Do not write a full answer", level2Prompt);
            StringAssert.Contains("three short sentences", level2Prompt);
            StringAssert.Contains("Quote one short exact phrase from the learner", level2Prompt);
            StringAssert.Contains("concrete revision example", level2Prompt);
            StringAssert.Contains("Write feedback_text in English only", level2Prompt);
            StringAssert.Contains("JSON mode: minimal", level2Prompt);
            StringAssert.Contains("exactly one field named feedback_text", level2Prompt);
            StringAssert.Contains("condition-blind diagnosis is already complete", level2Prompt);
            StringAssert.Contains("confirmed_focus: Reason", level2Prompt);
            StringAssert.Contains("diagnosis_issue_code: CREEI_REASON_WEAK", level2Prompt);
            StringAssert.Contains("target_success_criterion", level2Prompt);
            StringAssert.Contains("current_creei_stage: Reason", level2Prompt);
            StringAssert.DoesNotContain("condition:", level2Prompt.ToLowerInvariant());

            SetField(request, "DetailedJson", true);
            string detailedPrompt = InvokeString(generatorType, "BuildPrompt", request);
            StringAssert.Contains("JSON mode: detail", detailedPrompt);
            StringAssert.Contains("feedback_type", detailedPrompt);
            StringAssert.Contains("target_success_criterion", detailedPrompt);
            StringAssert.Contains("next_action", detailedPrompt);
            StringAssert.Contains("Do not return diagnostic component or strategy labels", detailedPrompt);

            SetField(request, "FeedbackLevel", Enum.Parse(levelType, "Level3"));
            SetField(request, "PreviousCoachFeedbackText", "Add one concrete example to support your claim.");
            string level3Prompt = InvokeString(generatorType, "BuildPrompt", request);
            StringAssert.Contains("This is an EXAMPLE task", level3Prompt);
            StringAssert.Contains("Do not evaluate the learner. Do not give advice", level3Prompt);
            StringAssert.Contains("one natural, speakable model sentence", level3Prompt);
            StringAssert.Contains("Never write phrases such as 'you should'", level3Prompt);
            StringAssert.Contains("previous_coach_advice", level3Prompt);
            StringAssert.Contains("Add one concrete example to support your claim.", level3Prompt);
            StringAssert.DoesNotContain("Diagnose the learner's utterance", level3Prompt);

            MethodInfo validator = generatorType.GetMethod("IsConcreteExampleText", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(validator);
            Assert.IsTrue((bool)validator.Invoke(null, new object[]
            {
                "Speaking is more important because it helps learners communicate their needs in daily life."
            }));
            Assert.IsFalse((bool)validator.Invoke(null, new object[]
            {
                "You should add a clearer reason to support your claim."
            }));
        }

        [Test]
        public void OpponentPromptsAreStageSpecificAndIndependentFromLearnerText()
        {
            Type requestType = GetRuntimeType("Game.Debate.CreeiOpponentStatementRequest");
            Type generatorType = GetRuntimeType("Game.Debate.CreeiOpponentStatementGenerator");
            Type stageType = GetRuntimeType("Game.Debate.CreeiStage");
            Assert.IsNotNull(requestType);
            Assert.IsNotNull(generatorType);
            Assert.IsNotNull(stageType);

            object request = Activator.CreateInstance(requestType);
            SetField(request, "Topic", "Reading and speaking");
            SetField(request, "OpponentStance", "Reading is more important.");

            foreach (string stage in new[] { "Claim", "Reason", "Evidence", "Explanation", "Impact" })
            {
                SetField(request, "Stage", Enum.Parse(stageType, stage));
                string prompt = InvokeString(generatorType, "BuildPrompt", request);
                StringAssert.Contains("Current CREEI stage: " + stage, prompt);
                StringAssert.Contains("Method for this stage", prompt);
                StringAssert.Contains("Your own completed CREEI stages", prompt);
                StringAssert.Contains("Do not respond to the learner", prompt);
                StringAssert.Contains("speech_text", prompt);
                StringAssert.DoesNotContain("learner_current_turn", prompt);
                StringAssert.DoesNotContain("Learner's latest response", prompt);
            }
        }

        [Test]
        public void MockDebatePromptEvaluatesCompleteCreeiSpeechAndReturnsFinalFeedback()
        {
            Type requestType = GetRuntimeType("Game.Debate.MockDebateEvaluationRequest");
            Type generatorType = GetRuntimeType("Game.Debate.MockDebateEvaluationGenerator");
            Assert.IsNotNull(requestType);
            Assert.IsNotNull(generatorType);

            object request = Activator.CreateInstance(requestType);
            SetField(request, "Topic", "Reading and speaking, which is more important?");
            SetField(request, "LearnerStance", "Speaking is more important.");
            SetField(request, "Transcript", "Speaking matters because learners need real communication practice.");
            SetField(request, "DurationSeconds", 125f);
            SetField(request, "TargetDurationSeconds", 180f);

            string prompt = InvokeString(generatorType, "BuildPrompt", request);
            foreach (string stage in new[] { "Claim", "Reason", "Evidence", "Explanation", "Impact" })
            {
                StringAssert.Contains(stage + ":", prompt);
            }

            StringAssert.Contains("Analyze the complete speech after the learner has finished", prompt);
            StringAssert.Contains("Do not decide whether the learner is allowed to finish or pass the stage", prompt);
            StringAssert.Contains("complete CREEI structure", prompt);
            StringAssert.Contains("four to six clear, supportive sentences", prompt);
            StringAssert.Contains("exactly one field named feedback_text", prompt);
            StringAssert.Contains("feedback_text", prompt);
            StringAssert.DoesNotContain("is_complete", prompt);
            StringAssert.DoesNotContain("detected_components", prompt);
            StringAssert.DoesNotContain("missing_components", prompt);
            StringAssert.Contains("speech_duration_seconds: 125.0", prompt);
            StringAssert.Contains("target_duration_seconds: 180.0", prompt);
            StringAssert.Contains("Write all feedback in English only", prompt);
        }

        [Test]
        public void ControllerAddsRequiredMockDebateAfterImpact()
        {
            string source = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Game/Scripts/SharedInitiativeOrchestrationController.cs"));

            StringAssert.Contains("EnterMockDebate();", source);
            StringAssert.Contains("mockDebateTargetSeconds = 180f", source);
            StringAssert.Contains("Phase = OrchestrationPhase.MockDebateOpponentSpeaking", source);
            StringAssert.Contains("Phase = OrchestrationPhase.MockDebateReady", source);
            StringAssert.Contains("Phase = OrchestrationPhase.MockDebateSpeaking", source);
            StringAssert.Contains("Phase = OrchestrationPhase.MockDebateAwaitingTranscript", source);
            StringAssert.Contains("Phase = OrchestrationPhase.MockDebateEvaluating", source);
            StringAssert.Contains("StartMockDebateLeoSpeech", source);
            StringAssert.Contains("SendNextMockDebateLeoSegment", source);
            StringAssert.Contains("MockDebateLeoCreeiSegments", source);
            StringAssert.Contains("_mockDebateLeoStartedAt = Time.realtimeSinceStartup", source);
            StringAssert.Contains("StartMockDebatePlayerTurn", source);
            StringAssert.Contains("StartMockDebateEvaluation", source);
            StringAssert.Contains("_mockDebateFeedbackReady = true", source);
            StringAssert.Contains("Phase = OrchestrationPhase.CoachSuggestionReady", source);
            StringAssert.Contains("Press Ask Anna to hear the complete CREEI summary and advice", source);
            StringAssert.Contains("Press Ask Anna when you want to hear it", source);
            StringAssert.DoesNotContain("Anna is speaking the complete CREEI summary and advice", source);
            StringAssert.DoesNotContain("Anna is delivering the final Coach feedback", source);
            StringAssert.Contains("BuildMockDebateTimer();", source);
            StringAssert.Contains("anchorMin = new Vector2(0.5f, 1f)", source);
            StringAssert.Contains("CREEI Stage 6/6: Mock Debate", source);
            StringAssert.Contains("manager.activeConvaiNPC != conversationNPC", source);
            StringAssert.Contains("HandleOrchestrationTapToTalk", source);
            StringAssert.Contains("ShouldUseOrchestrationTapToTalk", source);
            StringAssert.Contains("TogglePracticeStageRecording", source);
            StringAssert.Contains("_practiceVoiceRecording = true", source);
            StringAssert.Contains("_practiceVoiceAwaitingTranscript = true", source);
            StringAssert.Contains("Press T again when you finish", source);
            StringAssert.Contains("RealtimeCapturePurpose.PracticeStage", source);
            StringAssert.Contains("HandlePlayerUtterance(practiceTranscript, false)", source);
            int mockFeedbackStart = source.IndexOf("private void ApplyMockDebateFeedback", StringComparison.Ordinal);
            int mockFeedbackEnd = source.IndexOf("private void HandleConvaiResultReceived", mockFeedbackStart, StringComparison.Ordinal);
            Assert.GreaterOrEqual(mockFeedbackStart, 0);
            Assert.Greater(mockFeedbackEnd, mockFeedbackStart);
            string mockFeedbackMethod = source.Substring(mockFeedbackStart, mockFeedbackEnd - mockFeedbackStart);
            StringAssert.DoesNotContain("AskAnnaForReadyFeedback", mockFeedbackMethod);

            int practiceToggleStart = source.IndexOf("private void TogglePracticeStageRecording", StringComparison.Ordinal);
            int practiceToggleEnd = source.IndexOf("private void StartMockDebateTapRecording", practiceToggleStart, StringComparison.Ordinal);
            Assert.GreaterOrEqual(practiceToggleStart, 0);
            Assert.Greater(practiceToggleEnd, practiceToggleStart);
            string practiceToggleMethod = source.Substring(practiceToggleStart, practiceToggleEnd - practiceToggleStart);
            StringAssert.Contains("realtimeTranscriber?.StopSession()", practiceToggleMethod);
            StringAssert.Contains("realtimeTranscriber.StartSession", practiceToggleMethod);
            StringAssert.Contains("XfyunRealtimeTranscriber", source);
            StringAssert.Contains("realtimeTranscriber.StartSession", source);
            StringAssert.Contains("HandleRealtimeTranscriptUpdated", source);
            StringAssert.Contains("realtimeTranscriber?.StopSession();", source);
            StringAssert.Contains("CompleteMockDebateAfterTranscriptTimeout", source);
            StringAssert.Contains("FinalizeMockDebateTranscriptAndEvaluate", source);
            StringAssert.Contains("if (_mockDebateRecording)", source);
            StringAssert.Contains("ShouldSuppressMockDebateNpcResponse", source);
            StringAssert.Contains("ConvaiGRPCAPI.ShouldSuppressVoiceResponse = ShouldSuppressMockDebateNpcResponse", source);
            StringAssert.Contains("Phase == OrchestrationPhase.MockDebateAwaitingTranscript", source);
            StringAssert.Contains("StopMockDebateRecordingAtTimeLimit", source);
            StringAssert.Contains("Recording stops automatically at 03:00", source);
            StringAssert.DoesNotContain("MockDebateVoiceChunkSeconds", source);
            StringAssert.DoesNotContain("RotateMockDebateVoiceChunk", source);
            StringAssert.DoesNotContain("durationMet || result.IsComplete", source);
            StringAssert.DoesNotContain("Mock Debate incomplete", source);
            StringAssert.Contains("Start Mock Debate", source);
            StringAssert.DoesNotContain(
                "if (_creeiStageIndex >= CreeiStages.Length - 1)\r\n            {\r\n                StartCoachFeedbackRequest(CoachFeedbackLevel.Summary",
                source);

            Type controllerType = GetRuntimeType("Game.Debate.SharedInitiativeOrchestrationController");
            FieldInfo presetField = controllerType?.GetField(
                "MockDebateLeoCreeiSegments",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(presetField);
            string[] segments = (string[])presetField.GetValue(null);
            Assert.AreEqual(5, segments.Length);
            int wordCount = string.Join(" ", segments)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Length;
            Assert.GreaterOrEqual(wordCount, 380);
        }

        [Test]
        public void MockDebateRealtimeTranscriptionUsesContinuousEnglishAudio()
        {
            string source = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Game/Scripts/XfyunRealtimeTranscriber.cs"));

            StringAssert.Contains("wss://rtasr.xfyun.cn/v1/ws", source);
            StringAssert.Contains("&lang=en&pd=edu&vadMdn=2", source);
            StringAssert.Contains("SamplesPerPacket = 640", source);
            StringAssert.Contains("PacketIntervalMilliseconds = 40", source);
            StringAssert.Contains("Microphone.Start(", source);
            StringAssert.Contains("true,", source);
            StringAssert.Contains("data.cn?.st?.type, \"0\"", source);
            StringAssert.Contains("_segments[data.seg_id] = segment", source);
            StringAssert.Contains("_liveSegment = segment", source);
            StringAssert.Contains("confirmedTranscript, _liveSegment", source);
            StringAssert.Contains("PlayerPrefs.GetString", source);
            StringAssert.Contains("Environment.GetEnvironmentVariable", source);
            StringAssert.DoesNotContain("[SerializeField] private string apiKey", source);
        }

        [Test]
        public void ConvaiVoiceCancellationIsHandledWithoutAnErrorLog()
        {
            string source = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Convai/Scripts/Runtime/Core/ConvaiGRPCAPI.cs"));

            StringAssert.Contains("catch (OperationCanceledException)", source);
            StringAssert.Contains("Voice recording stream ended after cancellation", source);
            StringAssert.Contains("Voice stream was cancelled while sending microphone audio", source);
            StringAssert.Contains("_isFinalUserQueryTextBuffer = string.Empty", source);
            StringAssert.Contains("ShouldSuppressVoiceResponse", source);
            StringAssert.Contains("_suppressCurrentVoiceResponse = ShouldSuppressVoiceResponse?.Invoke() == true", source);
        }

        [Test]
        public void VoiceRecordingUsesTapToTalkAndAContinuousLoopingBuffer()
        {
            string inputSource = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Convai/Scripts/Runtime/Core/ConvaiInputManager.cs"));
            string npcSource = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Convai/Scripts/Runtime/Core/ConvaiNPC.cs"));
            string grpcSource = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Convai/Scripts/Runtime/Core/ConvaiGRPCAPI.cs"));

            StringAssert.Contains("ShouldUseTapToTalk", inputSource);
            StringAssert.Contains("TapToTalkRequested", inputSource);
            StringAssert.Contains("if (context.performed)", inputSource);
            StringAssert.Contains("ShouldUseTapToTalk?.Invoke() != true", inputSource);
            StringAssert.Contains("StartListening(int recordingLengthSeconds)", npcSource);
            StringAssert.Contains("DEFAULT_MICROPHONE_BUFFER_SECONDS = 10", npcSource);
            StringAssert.Contains("StartListeningInternal(DEFAULT_MICROPHONE_BUFFER_SECONDS)", npcSource);
            StringAssert.Contains("StartListeningInternal(Mathf.Max(1, recordingLengthSeconds))", npcSource);
            StringAssert.Contains("Microphone.Start(", grpcSource);
            StringAssert.Contains("true,", grpcSource);
            StringAssert.Contains("microphoneBufferSeconds", grpcSource);
            StringAssert.Contains("GetRingBufferDistance(pos, newPos, audioClip.samples)", grpcSource);
            StringAssert.Contains("sampleCount - previousPosition + currentPosition", grpcSource);
        }

        [Test]
        public void AiHomeworkLeoUsesACompletePresetCreeiSpeech()
        {
            Type controllerType = GetRuntimeType("Game.Debate.AiHomeworkCreeiDebateController");
            FieldInfo presetField = controllerType?.GetField(
                "PresetLeoCreeiSegments",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(presetField);
            string[] segments = (string[])presetField.GetValue(null);
            Assert.AreEqual(5, segments.Length);
            Assert.IsTrue(segments.All(segment => !string.IsNullOrWhiteSpace(segment)));
            int wordCount = string.Join(" ", segments)
                .Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Length;
            Assert.GreaterOrEqual(wordCount, 280);
        }

        [Test]
        public void AiHomeworkSceneUsesLeoAndHasNoCoachFlow()
        {
            string scenePath = Path.Combine(
                GetAssetsPath(),
                "Game/Scenes/Level_AIHomeworkCreeiDebate.unity");
            string source = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Game/Scripts/AiHomeworkCreeiDebateController.cs"));

            Assert.IsTrue(File.Exists(scenePath));
            StringAssert.Contains("Should students be allowed to use AI for homework?", source);
            StringAssert.Contains("leoNPC.SendTextDataAsync", source);
            StringAssert.Contains("SendNextLeoSegment", source);
            StringAssert.Contains("StopPlayerRecordingAtLimit", source);
            StringAssert.Contains("XfyunRealtimeTranscriber", source);
            StringAssert.Contains("realtimeTranscriber.StartSession", source);
            StringAssert.Contains("realtimeTranscriber?.StopSession()", source);
            StringAssert.Contains("HandleRealtimeTranscriptUpdated", source);
            StringAssert.Contains("HandleRealtimeTranscriptionCompleted", source);
            StringAssert.Contains("HandleRealtimeTranscriptionFailed", source);
            StringAssert.Contains("Recording continuously through pauses", source);
            StringAssert.DoesNotContain("leoNPC.StartListening", source);
            StringAssert.Contains("PresetLeoCreeiSegments", source);
            StringAssert.Contains("StartLeoPresetSpeech", source);
            StringAssert.Contains("StartLeoPresetSpeechWhenReady", source);
            StringAssert.Contains("IsLeoSpeechPipelineReady", source);
            StringAssert.Contains("GuardLeoSpeechStart", source);
            StringAssert.Contains("MaxLeoSegmentSendAttempts = 2", source);
            StringAssert.Contains("RetryOrFailLeoSegment", source);
            StringAssert.Contains("OnTextSendFailed", source);
            StringAssert.Contains("Press Retry Leo", source);
            StringAssert.Contains("StartLocalLeoTtsFallback", source);
            StringAssert.Contains("StartWindowsTtsProcess", source);
            StringAssert.Contains("offline English voice", source);
            StringAssert.Contains("Round 1: Leo's CREEI Argument", source);
            StringAssert.Contains("Round 2: Your CREEI Argument", source);
            StringAssert.Contains("StartPlayerTurn", source);
            StringAssert.Contains("Your CREEI argument was recorded. Debate complete.", source);
            StringAssert.DoesNotContain("CreeiThreeMinuteOpponentGenerator", source);
            StringAssert.DoesNotContain("openAI", source);
            StringAssert.DoesNotContain("GPT", source);
            StringAssert.DoesNotContain("CoachFeedback", source);
            StringAssert.DoesNotContain("coachNPC", source);
        }

        [Test]
        public void AiHomeworkLiveTranscriptPreservesFullTextAndUsesScrollableViews()
        {
            Type controllerType = GetRuntimeType("Game.Debate.AiHomeworkCreeiDebateController");
            MethodInfo formatter = controllerType?.GetMethod(
                "BuildRollingTranscriptText",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(formatter);

            string confirmed = "The first confirmed segment.";
            string preview = "This is the newest recognition preview.";
            string result = (string)formatter.Invoke(null, new object[] { confirmed, preview });
            Assert.AreEqual(confirmed + " " + preview, result);

            string longResult = (string)formatter.Invoke(null, new object[]
            {
                new string('a', 500),
                "latest words"
            });
            Assert.Greater(longResult.Length, 500);
            StringAssert.EndsWith("latest words", longResult);

            string source = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Game/Scripts/AiHomeworkCreeiDebateController.cs"));
            StringAssert.Contains("_completePlayerTranscript.Clear();", source);
            StringAssert.Contains("CreateScrollableTranscript", source);
            StringAssert.Contains("viewportObject.AddComponent<RectMask2D>()", source);
            StringAssert.Contains("ContentSizeFitter.FitMode.PreferredSize", source);
            StringAssert.Contains("verticalScrollbar = scrollbar", source);
            StringAssert.Contains("verticalNormalizedPosition = 0f", source);
            StringAssert.DoesNotContain("TextOverflowModes.Ellipsis", source);
        }

        [Test]
        public void LocalFallbackIsNotSpeakableCoachAdvice()
        {
            Type requestType = GetRuntimeType("Game.Debate.CoachFeedbackRequest");
            Type resultType = GetRuntimeType("Game.Debate.CoachFeedbackResult");
            Type levelType = GetRuntimeType("Game.Debate.CoachFeedbackLevel");
            Type generatorType = GetRuntimeType("Game.Debate.DebateCoachFeedbackGenerator");
            Assert.IsNotNull(requestType);
            Assert.IsNotNull(resultType);
            Assert.IsNotNull(levelType);
            Assert.IsNotNull(generatorType);

            object request = Activator.CreateInstance(requestType);
            SetField(request, "PlayerUtteranceText", "Speaking is better because I think so.");
            SetField(request, "SelectedStrategy", "Logos");
            SetField(request, "FeedbackLevel", Enum.Parse(levelType, "Level2"));

            object result = generatorType.GetMethod("BuildLocalFallback", BindingFlags.Public | BindingFlags.Static)
                ?.Invoke(null, new[] { request });
            Assert.IsNotNull(result);
            Assert.AreEqual("Evidence", GetField(result, "WeakComponent"));
            Assert.AreEqual("Logos", GetField(result, "RecommendedStrategy"));
            Assert.IsEmpty((string)GetField(result, "FeedbackText"));
            StringAssert.Contains("disabled", ((string)GetField(result, "DebugInfo")).ToLowerInvariant());
        }

        [Test]
        public void CoachLoggerWritesSupplementHeaderAndEscapesRows()
        {
            Type loggerType = GetRuntimeType("Game.Debate.DebateCoachLogger");
            Type rowType = GetRuntimeType("Game.Debate.CoachFeedbackLogRow");
            Assert.IsNotNull(loggerType);
            Assert.IsNotNull(rowType);

            string path = Path.Combine(Path.GetTempPath(), "coach-log-" + Guid.NewGuid().ToString("N") + ".csv");
            object logger = Activator.CreateInstance(loggerType, path);
            object row = Activator.CreateInstance(rowType);
            SetField(row, "ParticipantId", "P,01");
            SetField(row, "Condition", "Condition C");
            SetField(row, "Stage", "Practice Debate");
            SetField(row, "TurnId", 2);
            SetField(row, "PlayerUtteranceText", "one, \"two\"\nthree");
            SetField(row, "CoachFeedbackLevel", "Level2");
            SetField(row, "CoachFeedbackText", "Your evidence is weak. Add one example.");
            SetField(row, "TimestampFeedbackShown", "2026-07-03T00:00:00Z");

            loggerType.GetMethod("LogFeedback")?.Invoke(logger, new[] { row });

            string csv = File.ReadAllText(path);
            StringAssert.Contains("participant_id,condition,stage,topic_id,turn_id,player_side", csv);
            StringAssert.Contains("\"P,01\"", csv);
            StringAssert.Contains("\"one, \"\"two\"\"\nthree\"", csv);
        }

        [Test]
        public void SharedInitiativeControllerDefaultsToVoiceCaptureThenPlayerChoiceFlow()
        {
            Assert.IsTrue(SharedInitiativeOrchestrationController.DefaultAutomaticCoachAfterPlayerVoice);
            Assert.IsFalse(SharedInitiativeOrchestrationController.DefaultShowManualPauseButton);

            string source = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Game/Scripts/SharedInitiativeOrchestrationController.cs"));
            string bridgeSource = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Game/Scripts/InteractiveDebateTranscriptBridge.cs"));
            string mockGeneratorSource = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Game/Scripts/MockDebateEvaluationGenerator.cs"));

            StringAssert.Contains("ConvaiGRPCAPI.TryHandleUserVoiceTranscript", source);
            StringAssert.Contains("Phase == OrchestrationPhase.OpponentSpeaking", source);
            StringAssert.Contains("PlayerResponseReady", source);
            StringAssert.Contains("Coach is preparing feedback. Ask Anna will unlock", source);
            StringAssert.Contains("RequestCoachFeedbackForCurrentResponse", source);
            StringAssert.Contains("AskAnnaForReadyFeedback", source);
            StringAssert.Contains("Ask Anna", source);
            StringAssert.Contains("StartCoachFeedbackRequest(CoachFeedbackLevel.Level2, false);", source);
            StringAssert.Contains("speakWhenReady", source);
            StringAssert.Contains("HasPreparedCoachFeedback", source);
            StringAssert.Contains("Anna did not produce audio. Press Ask Anna to try again.", source);
            StringAssert.Contains("CombineTranscript", source);
            StringAssert.Contains("CreeiOpponentStatementGenerator", source);
            StringAssert.Contains("StartCurrentCreeiStage", source);
            StringAssert.Contains("BuildOpponentConvaiPrompt", source);
            StringAssert.Contains("CREEI Stage", source);
            StringAssert.Contains("CurrentCreeiStage = CurrentCreeiStage.ToString()", source);
            StringAssert.Contains("PreviousLearnerCreeiStages = BuildCreeiContext", source);
            StringAssert.Contains("PreviousOpponentCreeiStages = BuildCreeiContext", source);
            StringAssert.Contains("Read the model sentence below verbatim exactly once", source);
            StringAssert.Contains("The Example result was rejected because it looked like advice", source);
            StringAssert.Contains("_exampleRequestedForCurrentTurn = false", source);
            StringAssert.DoesNotContain("Learner's latest response", source);
            StringAssert.Contains("Coach GPT Debug Panel", source);
            StringAssert.Contains("Prompt prepared for GPT", source);
            StringAssert.Contains("Request sent:", source);
            StringAssert.Contains("Valid GPT feedback returned:", source);
            StringAssert.Contains("GPT response / validation status", source);
            StringAssert.Contains("DebateCoachFeedbackGenerator.BuildPrompt(request)", source);
            StringAssert.Contains("UpdateCoachDebugResult", source);
            StringAssert.Contains("ApplyUnavailableCoachFeedback", source);
            StringAssert.Contains("Prompt sent to Anna / Convai", source);
            StringAssert.Contains("Speak only in English", source);
            StringAssert.Contains("Convai delivery: request dispatched to Anna", source);
            StringAssert.Contains("Convai delivery failure: no Anna audio was observed before timeout", source);
            StringAssert.Contains("OnTextSendFailed", source);
            StringAssert.Contains("HandleConvaiTextSendFailed", source);
            StringAssert.Contains("RetryCoachSpeechAfterDelay(2f)", source);
            StringAssert.Contains("Convai is unavailable. Press Ask Anna to retry", source);
            StringAssert.Contains("CreateToggle(_debugRoot.transform, \"Detail\")", source);
            StringAssert.Contains("_debugRoot.SetActive(false)", source);
            StringAssert.Contains("DetailedJson = _detailToggle != null && _detailToggle.isOn", source);
            StringAssert.Contains("BuildStageSelector(_root.transform)", source);
            StringAssert.Contains("string[] labels = { \"Claim\", \"Reason\", \"Evidence\", \"Explanation\", \"Impact\", \"Mock Debate\" }", source);
            StringAssert.Contains("() => SelectStage(selectedIndex)", source);
            StringAssert.Contains("public void SelectStage(int stageIndex)", source);
            StringAssert.Contains("CancelCurrentStageActivity", source);
            StringAssert.Contains("if (stageIndex == CreeiStages.Length)", source);
            StringAssert.Contains("_creeiStageIndex = stageIndex", source);
            StringAssert.Contains("UpdateStageSelectionVisuals", source);
            StringAssert.Contains("Local default feedback is disabled", source);
            StringAssert.Contains("BuildCoachFailureSummary", source);
            StringAssert.Contains("Reason: API key problem", source);
            StringAssert.Contains("Reason: network/proxy/DNS/timeout problem.", source);
            StringAssert.Contains("inputField.textViewport", source);
            StringAssert.Contains("ApplyDebugPanelLayout", source);
            StringAssert.Contains("viewportObject.AddComponent<RectMask2D>()", source);
            StringAssert.Contains("accepted player voice while the opponent-speaking phase was still active", source);
            StringAssert.Contains("HandlePlayerSpeakingChanged", source);
            StringAssert.Contains("BeginPlayerRetake", source);
            StringAssert.Contains("Previous response and Coach feedback cleared", source);
            StringAssert.Contains("_pendingFeedbackRows.RemoveAll", source);
            StringAssert.Contains("Phase == OrchestrationPhase.CoachGenerating", source);
            StringAssert.Contains("Phase == OrchestrationPhase.CoachSuggestionReady", source);
            StringAssert.Contains("Need an example?", source);
            StringAssert.Contains("PreviousCoachFeedbackText = _latestFeedbackText", source);
            StringAssert.Contains("_coachFeedbackSentToAnna", source);
            StringAssert.Contains("Concrete example ready", source);
            StringAssert.Contains("ShowCoachLineInLeftUi", source);
            StringAssert.Contains("BuildWorldSpeechCaptions();", source);
            StringAssert.Contains("BuildCoachFeedbackBoard();", source);
            StringAssert.Contains("Coach Feedback World Canvas", source);
            StringAssert.Contains("new Vector3(0.18f, 1.52f, 3.7437f)", source);
            StringAssert.Contains("HideTextFromLayout(_taskText);", source);
            StringAssert.Contains("HideTextFromLayout(_statusText);", source);
            StringAssert.Contains("HideTextFromLayout(_playerText);", source);
            StringAssert.Contains("HideTextFromLayout(_coachText);", source);
            StringAssert.Contains("ShowCoachFeedbackOnBoard(\"Mock Debate - Complete CREEI\", feedback);", source);
            StringAssert.Contains("request.CurrentCreeiStage + \" Feedback\"", source);
            StringAssert.Contains("verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide", source);
            StringAssert.Contains("SetCoachFeedbackBoardTextVisible(true);", source);
            StringAssert.Contains("SetCoachFeedbackBoardTextVisible(false);", source);
            StringAssert.Contains("_coachBoardTitleText.enabled = visible;", source);
            StringAssert.Contains("_coachBoardRoot.SetActive(false);", source);
            StringAssert.Contains("_opponentHeadCaptionText", source);
            StringAssert.Contains("_coachHeadCaptionText", source);
            StringAssert.Contains("ShowWorldCaption(", source);
            StringAssert.Contains("_opponentHeadCaptionScrollRect", source);
            StringAssert.Contains("viewportObject.AddComponent<RectMask2D>()", source);
            StringAssert.Contains("scrollRect.verticalScrollbar = scrollbar", source);
            StringAssert.Contains("scrollRect.verticalNormalizedPosition = 0f", source);
            StringAssert.Contains("MockDebateMinimumResponseTimeoutSeconds = 90f", source);
            StringAssert.Contains("GetMockDebateResponseTimeoutSeconds()", source);
            StringAssert.Contains("const int maxAttempts = 2", mockGeneratorSource);
            StringAssert.Contains("IsRetryableFailure", mockGeneratorSource);
            StringAssert.Contains("timeout_seconds={_timeoutSeconds}", mockGeneratorSource);
            StringAssert.Contains("\"Coach: \" + safeText", source);
            StringAssert.Contains("SubscribeToCoachAudio();", source);
            StringAssert.Contains("CaptureCoachAudio", source);
            StringAssert.Contains("DisableConvaiTranscriptUi", bridgeSource);
            StringAssert.Contains("TranscriptUIActiveStatus = false", bridgeSource);
            StringAssert.Contains("_suppressConvaiTranscriptUi", bridgeSource);
            StringAssert.Contains("Input.GetMouseButtonDown(1)", source);
            StringAssert.Contains("SetControlCursor(!_isUiMode)", source);
            StringAssert.Contains("ApplyControlCursorState", source);
            StringAssert.DoesNotContain("transcriptBridge?.PublishPlayerUtterance", source);
            StringAssert.Contains("DisableLegacyNpcSpeechBubbles", source);
            StringAssert.Contains("speechBubble.gameObject.SetActive(false)", source);
            StringAssert.Contains("DebateCoachLogger", source);
            StringAssert.Contains("OnDisable()", source);
            StringAssert.Contains("UnregisterVoiceInterceptor();", source);
            StringAssert.Contains("RegisterConvaiInputSuppressors();", source);
            StringAssert.Contains("ShouldSuppressCoachTalkInput", source);
            StringAssert.Contains("PrepareForPlayerVoiceInput", source);
            StringAssert.Contains("ShowTalkInputBlockedStatus", source);
            StringAssert.Contains("Wait for GPT to prepare Leo's", source);
            StringAssert.Contains("DeactivateInputField(_debugPromptInput)", source);
            StringAssert.Contains("EventSystem.current.SetSelectedGameObject(null)", source);
            StringAssert.Contains("manager.activeConvaiNPC != conversationNPC", source);
            StringAssert.Contains("ShouldSuppressAutoActiveNpcUpdate", source);
        }

        [Test]
        public void CoachVoiceUsesCoachNpcAndEndSessionRequestsImmediateFeedback()
        {
            string source = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Game/Scripts/SharedInitiativeOrchestrationController.cs"));

            StringAssert.Contains("ConvaiNPCManager.Instance?.SetActiveConvaiNPC(coachNPC);", source);
            StringAssert.DoesNotContain(
                "ConvaiNPCManager.Instance?.SetActiveConvaiNPC(conversationNPC);\r\n\r\n            CoachFeedbackRequest request = BuildCoachRequest(level);",
                source);
            StringAssert.DoesNotContain(
                "ConvaiNPCManager.Instance?.SetActiveConvaiNPC(conversationNPC);\n\n            CoachFeedbackRequest request = BuildCoachRequest(level);",
                source);
            StringAssert.DoesNotContain("SendTextDataAsSoloSpeech", source);
            StringAssert.Contains("StartCoachFeedbackRequest(CoachFeedbackLevel.Summary, false, true);", source);
            StringAssert.Contains("StartCoachFeedbackRequest(CoachFeedbackLevel.Level3, true, true);", source);
            StringAssert.DoesNotContain("StartCoachFeedbackSpeech(closingText);", source);
            StringAssert.Contains("_coachRequestVersion", source);
            StringAssert.Contains("requestVersion != _coachRequestVersion", source);
            StringAssert.Contains("coachVoicePitch = 0.92f", source);
            StringAssert.Contains("useWindowsTtsFallbackWhenConvaiSilent = false", source);
            StringAssert.Contains("warm, gentle", source);
            StringAssert.Contains("Please say this prepared Coach feedback aloud", source);
            StringAssert.DoesNotContain("Say only the exact feedback inside the brackets", source);
        }

        [Test]
        public void CoachFeedbackDebugInfoIdentifiesGptOrLocalFallbackSource()
        {
            string generatorSource = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Game/Scripts/DebateCoachFeedbackGenerator.cs"));
            string typesSource = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Game/Scripts/CoachFeedbackTypes.cs"));

            StringAssert.Contains("public string DebugInfo = string.Empty;", typesSource);
            StringAssert.Contains("API key missing: GPT was not called", generatorSource);
            StringAssert.Contains("GPT was called successfully and returned a parseable structured response", generatorSource);
            StringAssert.Contains("GPT was attempted, but no usable result reached the app", generatorSource);
            StringAssert.Contains("BuildRequestFailureDebugInfo", generatorSource);
            StringAssert.Contains("http_status", generatorSource);
            StringAssert.Contains("API key missing", generatorSource);
            StringAssert.Contains("ResolveChatCompletionsEndpoint", generatorSource);
            StringAssert.Contains("[\"messages\"]", generatorSource);
            StringAssert.Contains("[\"response_format\"]", generatorSource);
            StringAssert.Contains("choices[0].message.content", generatorSource);
            StringAssert.Contains("StripJsonCodeFence", generatorSource);
            StringAssert.Contains("CombineAlternativeFeedbackFields", generatorSource);
            StringAssert.Contains("next_step_strategy", generatorSource);
            StringAssert.DoesNotContain("Your claim is understandable", generatorSource);
        }

        [Test]
        public void ConvaiUserTranscriptKeepsEndOfResponseTailChunk()
        {
            string grpcSource = File.ReadAllText(
                Path.Combine(GetAssetsPath(), "Convai/Scripts/Runtime/Core/ConvaiGRPCAPI.cs"));

            StringAssert.Contains("string textData = result.UserQuery.TextData ?? string.Empty;", grpcSource);
            StringAssert.Contains("CombineTranscript(_isFinalUserQueryTextBuffer, textData)", grpcSource);
            StringAssert.Contains("result.UserQuery.IsFinal", grpcSource);
            StringAssert.Contains(": CombineTranscript(_isFinalUserQueryTextBuffer, textData).Trim()", grpcSource);
        }

        [Test]
        public void ThreeStageSceneReplacesLegacyControllerAndKeepsAsrBridge()
        {
            string scene = ReadAssetText(SharedInitiativeScenePath);
            string threeStageControllerGuid = AssetDatabase.AssetPathToGUID(
                "Assets/Game/Scripts/ThreeStageDebatePracticeController.cs");

            StringAssert.Contains(threeStageControllerGuid, scene);
            StringAssert.DoesNotContain(SharedInitiativeControllerGuid, scene);
            StringAssert.Contains("speakCoachFeedback: 1", scene);
            StringAssert.Contains("realtimeTranscriber: {fileID: 8800100003}", scene);
            StringAssert.Contains(
                $"m_Script: {{fileID: 11500000, guid: {TranscriptBridgeGuid}, type: 3}}",
                scene);
            AssertSceneObjectInactive(scene, "Round Timer");
        }

        [Test]
        public void ThreeStageCoachControllerIsOnlyPresentInRuntimeSceneAndLegacyIsAbsent()
        {
            string scenesRoot = Path.Combine(GetAssetsPath(), "Game/Scenes");
            string[] scenePaths = Directory.GetFiles(scenesRoot, "*.unity", SearchOption.AllDirectories);
            string threeStageGuid = AssetDatabase.AssetPathToGUID(
                "Assets/Game/Scripts/ThreeStageDebatePracticeController.cs");
            string[] scenesWithCoach = scenePaths
                .Where(path => !path.Replace('\\', '/').Contains("/Scenes/backup/"))
                .Where(path => File.ReadAllText(path).Contains(threeStageGuid))
                .Select(path => Path.GetFileName(path))
                .ToArray();

            CollectionAssert.AreEquivalent(
                new[] { "04 coach Agent.unity" },
                scenesWithCoach);
            Assert.IsFalse(scenePaths
                .Where(path => !path.Replace('\\', '/').Contains("/Scenes/backup/"))
                .Any(path => File.ReadAllText(path).Contains(SharedInitiativeControllerGuid)));
        }

        private static Type GetRuntimeType(string typeName)
        {
            return RuntimeAssembly.GetType(typeName);
        }

        private static void SetField(object instance, string fieldName, object value)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            field.SetValue(instance, value);
        }

        private static object GetField(object instance, string fieldName)
        {
            FieldInfo field = instance.GetType().GetField(fieldName, BindingFlags.Public | BindingFlags.Instance);
            Assert.IsNotNull(field, fieldName);
            return field.GetValue(instance);
        }

        private static string InvokeString(Type type, string methodName, object argument)
        {
            MethodInfo method = type.GetMethod(methodName, BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(method, methodName);
            return (string)method.Invoke(null, new[] { argument });
        }

        private static string ReadAssetText(string relativePath)
        {
            return File.ReadAllText(Path.Combine(GetAssetsPath(), relativePath));
        }

        private static string GetAssetsPath()
        {
            string assetsPath;
            try
            {
                assetsPath = Application.dataPath;
            }
            catch
            {
                assetsPath = null;
            }

            if (string.IsNullOrWhiteSpace(assetsPath) || !Directory.Exists(assetsPath))
            {
                assetsPath = Path.Combine(Directory.GetCurrentDirectory(), "Assets");
            }

            return assetsPath;
        }

        private static void AssertSceneObjectInactive(string scene, string objectName)
        {
            int nameIndex = scene.IndexOf("m_Name: " + objectName, StringComparison.Ordinal);
            Assert.GreaterOrEqual(nameIndex, 0, objectName);

            int objectStart = scene.LastIndexOf("--- !u!1", nameIndex, StringComparison.Ordinal);
            Assert.GreaterOrEqual(objectStart, 0, objectName);

            int objectEnd = scene.IndexOf("--- !u!", nameIndex + 1, StringComparison.Ordinal);
            if (objectEnd < 0)
            {
                objectEnd = scene.Length;
            }

            string objectBlock = scene.Substring(objectStart, objectEnd - objectStart);
            StringAssert.Contains("m_IsActive: 0", objectBlock);
        }
    }
}
