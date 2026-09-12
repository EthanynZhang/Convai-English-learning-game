using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Game.Debate;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public class NpcDebateTutorialNarrationTests
    {
        [Test]
        public void PracticeContentUsesReadingVersusSpeakingAndSpeakingFirstStance()
        {
            Assert.AreEqual(
                "Reading and speaking, which is more important in learning English?",
                DebateLearningContent.CreeiVoicePracticeTopic);

            string allContent = string.Join("\n", new[]
                {
                    DebateLearningContent.CreeiDemoTranscript,
                    DebateLearningContent.CreeiStructureText,
                    DebateLearningContent.SpotMissingPracticeText,
                    DebateLearningContent.SpotMissingFeedback,
                    DebateLearningContent.OneSentenceTryText,
                    DebateLearningContent.OneSentenceTryFeedback,
                    DebateLearningContent.StrategyMiniTryText,
                    string.Join("\n", DebateLearningContent.StageSequence.Select(stage => stage.Body)),
                    string.Join("\n", DebateLearningContent.CreeiDialogueLines.Select(line => line.Text)),
                    string.Join("\n", DebateLearningContent.LogosDialogueLines.Select(line => line.Text)),
                    string.Join("\n", DebateLearningContent.EthosDialogueLines.Select(line => line.Text)),
                    string.Join("\n", DebateLearningContent.PathosDialogueLines.Select(line => line.Text))
                });

            StringAssert.Contains("Speaking is more important", allContent);
            StringAssert.DoesNotContain("AI in class", allContent);
            StringAssert.DoesNotContain("homework", allContent.ToLowerInvariant());
            StringAssert.DoesNotContain("responsible university", allContent.ToLowerInvariant());
        }

        [Test]
        public void VoicePracticeDefinesFiveSpeakingFirstCreeiPrompts()
        {
            Assert.AreEqual(5, DebateLearningContent.CreeiVoicePracticePrompts.Length);
            CollectionAssert.AreEqual(
                new[] { "Claim", "Reason", "Evidence", "Explanation", "Impact" },
                DebateLearningContent.CreeiVoicePracticePrompts.Select(prompt => prompt.Title).ToArray());
            Assert.IsTrue(DebateLearningContent.CreeiVoicePracticePrompts.All(prompt =>
                prompt.Prompt.Contains("speaking", StringComparison.OrdinalIgnoreCase) ||
                prompt.Part != CreeiPartKey.Claim));
            StringAssert.Contains("Speaking is more important", DebateLearningContent.CreeiVoicePracticePrompts[0].Prompt);
        }

        [Test]
        public void MiniMaxClientBuildsSpeech02EnglishGracefulLadyWavRequest()
        {
            Type clientType = typeof(DebateLearningContent).Assembly.GetType("Game.Debate.MiniMaxTtsClient");
            Assert.IsNotNull(clientType, "MiniMaxTtsClient must isolate HTTP, caching, and response parsing.");

            MethodInfo buildRequest = clientType.GetMethod(
                "BuildRequestJson",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            MethodInfo computeCacheKey = clientType.GetMethod(
                "ComputeCacheKey",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string) },
                null);
            MethodInfo decodeAudio = clientType.GetMethod("TryDecodeAudioHex", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(buildRequest);
            Assert.IsNotNull(computeCacheKey);
            Assert.IsNotNull(decodeAudio);

            string json = (string)buildRequest.Invoke(null, new object[] { "Welcome to the tutorial." });
            StringAssert.Contains("\"model\":\"speech-02-hd\"", json);
            StringAssert.Contains("\"voice_id\":\"English_Graceful_Lady\"", json);
            StringAssert.Contains("\"format\":\"wav\"", json);
            StringAssert.Contains("\"language_boost\":\"English\"", json);

            string first = (string)computeCacheKey.Invoke(null, new object[] { "Welcome to the tutorial." });
            string second = (string)computeCacheKey.Invoke(null, new object[] { "Welcome to the tutorial." });
            Assert.AreEqual(first, second);
            Assert.AreEqual(64, first.Length);

            object[] arguments = { "52494646", null };
            bool decoded = (bool)decodeAudio.Invoke(null, arguments);
            Assert.IsTrue(decoded);
            CollectionAssert.AreEqual(new byte[] { 0x52, 0x49, 0x46, 0x46 }, (byte[])arguments[1]);
        }

        [Test]
        public void MiniMaxClientSupportsDistinctFemaleAndGentleMaleVoices()
        {
            Type clientType = typeof(DebateLearningContent).Assembly.GetType("Game.Debate.MiniMaxTtsClient");
            Assert.IsNotNull(clientType);

            MethodInfo buildRequest = clientType.GetMethod(
                "BuildRequestJson",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(string) },
                null);
            MethodInfo computeCacheKey = clientType.GetMethod(
                "ComputeCacheKey",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(string) },
                null);
            Assert.IsNotNull(buildRequest);
            Assert.IsNotNull(computeCacheKey);

            const string transcript = "How does that evidence support your claim?";
            string femaleVoice = (string)clientType.GetField("FemaleVoiceId")?.GetRawConstantValue();
            string maleVoice = (string)clientType.GetField("MaleVoiceId")?.GetRawConstantValue();
            Assert.AreEqual("English_Graceful_Lady", femaleVoice);
            Assert.AreEqual("English_Gentle-voiced_man", maleVoice);

            string maleJson = (string)buildRequest.Invoke(null, new object[] { transcript, maleVoice });
            StringAssert.Contains("\"text\":\"" + transcript + "\"", maleJson);
            StringAssert.Contains("\"voice_id\":\"English_Gentle-voiced_man\"", maleJson);

            string femaleCache = (string)computeCacheKey.Invoke(null, new object[] { transcript, femaleVoice });
            string maleCache = (string)computeCacheKey.Invoke(null, new object[] { transcript, maleVoice });
            Assert.AreNotEqual(femaleCache, maleCache, "Male and female clips must never share a cache entry.");
        }

        [Test]
        public void MiniMaxClientPrefersConfiguredKeysWithoutBundlingARepositorySecret()
        {
            Type clientType = typeof(DebateLearningContent).Assembly.GetType("Game.Debate.MiniMaxTtsClient");
            Assert.IsNotNull(clientType);

            MethodInfo selectApiKey = clientType.GetMethod(
                "SelectApiKey",
                BindingFlags.Public | BindingFlags.Static);
            FieldInfo embeddedKeyField = clientType.GetField(
                "EmbeddedInternalTestApiKey",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.IsNotNull(selectApiKey, "Key priority should be independently testable.");
            Assert.IsNotNull(embeddedKeyField);

            string embeddedKey = (string)embeddedKeyField.GetRawConstantValue();
            Assert.IsEmpty(embeddedKey, "Repository source must not bundle a live API key.");

            Assert.AreEqual(
                "process-key",
                selectApiKey.Invoke(null, new object[] { " process-key ", "user-key", embeddedKey }));
            Assert.AreEqual(
                "user-key",
                selectApiKey.Invoke(null, new object[] { " ", " user-key ", embeddedKey }));
            Assert.AreEqual(
                string.Empty,
                selectApiKey.Invoke(null, new object[] { null, null, embeddedKey }));
        }

        [Test]
        public void DialogueDemoPrefersPackagedWavsAndSharedLipSync()
        {
            string controllerSource = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "NpcDebateLearningPhaseController.cs"));
            string clientSource = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "MiniMaxTtsClient.cs"));

            StringAssert.Contains("PlayCachedDialogueLine", controllerSource);
            StringAssert.Contains("TryPlayCachedDemoTts", controllerSource);
            StringAssert.Contains("GetMiniMaxVoiceId(line)", controllerSource);
            StringAssert.Contains("MiniMaxTtsClient.MaleVoiceId", controllerSource);
            StringAssert.Contains("MiniMaxTtsClient.FemaleVoiceId", controllerSource);
            StringAssert.Contains("RequestSceneOneTtsClip", controllerSource);
            StringAssert.Contains("RequestLocalClip", controllerSource);
            StringAssert.DoesNotContain("speaker.SendTextDataAsync", controllerSource);
            StringAssert.Contains("EnsureAudioLipSync(primaryDemoNPC);", controllerSource);
            StringAssert.Contains("EnsureAudioLipSync(secondaryDemoNPC);", controllerSource);
            StringAssert.Contains("EnsureAudioLipSync(speaker);", controllerSource);
            StringAssert.Contains("EnvironmentVariableTarget.User", clientSource);
        }

        [Test]
        public void SceneOnePlayerBuildUsesPackagedCacheWithoutNetworkFallback()
        {
            string controllerSource = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "NpcDebateLearningPhaseController.cs"));
            string clientSource = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "MiniMaxTtsClient.cs"));

            StringAssert.Contains("Application.isEditor", controllerSource);
            StringAssert.Contains("_miniMaxTtsClient.RequestClip", controllerSource);
            StringAssert.Contains("_miniMaxTtsClient.RequestLocalClip", controllerSource);
            StringAssert.DoesNotContain("speaker.SendTextDataAsync", controllerSource);
            StringAssert.Contains("bool allowNetwork", clientSource);
            StringAssert.Contains("if (!allowNetwork)", clientSource);
            StringAssert.Contains("Application.streamingAssetsPath", clientSource);
        }

        [Test]
        public void AudioDrivenLipSyncExposesRmsCalculationAndJawOpenDriver()
        {
            Type lipSyncType = typeof(DebateLearningContent).Assembly.GetType("Game.Debate.AudioDrivenNpcLipSync");
            Assert.IsNotNull(lipSyncType, "AudioDrivenNpcLipSync must animate external TTS audio.");

            MethodInfo calculateRms = lipSyncType.GetMethod("CalculateRms", BindingFlags.Public | BindingFlags.Static);
            MethodInfo calculateJawWeight = lipSyncType.GetMethod(
                "CalculateJawWeight",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(calculateRms);
            Assert.IsNotNull(calculateJawWeight);
            float rms = (float)calculateRms.Invoke(null, new object[] { new[] { 0.5f, -0.5f, 0.5f, -0.5f } });
            Assert.AreEqual(0.5f, rms, 0.0001f);
            float quietJaw = (float)calculateJawWeight.Invoke(null, new object[] { 0f, 1.2f, 0.15f, 1.5f });
            float loudJaw = (float)calculateJawWeight.Invoke(null, new object[] { 1f, 1.2f, 0.15f, 1.5f });
            Assert.AreEqual(0.15f, quietJaw, 0.0001f);
            Assert.AreEqual(1.5f, loudJaw, 0.0001f);

            string source = File.ReadAllText(Path.Combine("Assets", "Game", "Scripts", "AudioDrivenNpcLipSync.cs"));
            StringAssert.Contains("GetOutputData", source);
            StringAssert.Contains("clip.GetData", source);
            StringAssert.Contains("jawOpen", source);
            StringAssert.Contains("SetBlendShapeWeight", source);
            StringAssert.Contains("SetBool(talkParameter", source);
            StringAssert.Contains("mouthGain = 1.2f", source);
            StringAssert.Contains("minimumSpeakingJawWeight = 0.15f", source);
            StringAssert.Contains("maximumJawWeight = 1.5f", source);
        }

        [Test]
        public void LearningControllerRaisesCanvasAndNarratesBeforeDemoPlayback()
        {
            string source = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "NpcDebateLearningPhaseController.cs"));
            string scene = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scenes", "01Level_NPCVsNPCDebate.unity"));

            StringAssert.Contains("new(-0.31524f, 1.45f, 3.7437f)", source);
            StringAssert.Contains("fixedPanelPosition: {x: -0.31524, y: 1.45, z: 3.7437}", scene);
            StringAssert.Contains("BeginStageNarration(stage)", source);
            StringAssert.Contains("PlayStageNarrationThenContinue", source);
            StringAssert.Contains("MiniMaxTtsClient", source);
            StringAssert.Contains("primaryDemoNPC", source);
            StringAssert.Contains("BeginDemoStage(stage);", source);
        }

        [Test]
        public void CreeiLabelsAndMicroPracticeHeadingsAppearOnlyOnce()
        {
            foreach (CreeiPart part in DebateLearningContent.CreeiParts)
            {
                Assert.IsFalse(
                    part.Text.StartsWith(part.Label + ":", StringComparison.OrdinalIgnoreCase),
                    part.Label + " is duplicated by both the label and its description.");
                StringAssert.Contains(part.Label + ":", DebateLearningContent.CreeiReadingText);
                StringAssert.DoesNotContain(
                    part.Label + ": " + part.Label + ":",
                    DebateLearningContent.CreeiReadingText);
            }

            StringAssert.DoesNotStartWith("Micro Practice", DebateLearningContent.SpotMissingPracticeText);
            StringAssert.DoesNotStartWith("Micro Practice", DebateLearningContent.OneSentenceTryText);
            StringAssert.DoesNotStartWith("Micro Practice", DebateLearningContent.StrategyMiniTryText);

            foreach (DebateLearningStageSpec stage in DebateLearningContent.StageSequence
                         .Where(stage => stage.ViewKind == DebateLearningViewKind.Demo))
            {
                string headingKeyword = stage.Title.Split(' ')[0];
                Assert.IsFalse(
                    stage.Body.Contains(headingKeyword, StringComparison.OrdinalIgnoreCase),
                    stage.Title + " repeats its heading keyword in the spoken introduction.");
            }
        }

        [Test]
        public void DialogueUsesTheSamePackagedCachePipelineAsStageNarration()
        {
            string source = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "NpcDebateLearningPhaseController.cs"));

            StringAssert.Contains("PlayCachedDialogueLine", source);
            StringAssert.Contains("RequestSceneOneTtsClip", source);
            StringAssert.Contains("_miniMaxTtsClient.RequestLocalClip", source);
            StringAssert.Contains("line.Text", source);
            StringAssert.Contains("PlayAudioClipOnNpc(generatedClip, speaker, true)", source);
        }

        [Test]
        public void LearningPanelUsesLowerPositionAndExplicitVisibleStageTitle()
        {
            string source = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "NpcDebateLearningPhaseController.cs"));
            string scene = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scenes", "01Level_NPCVsNPCDebate.unity"));

            StringAssert.Contains("new(-0.31524f, 1.45f, 3.7437f)", source);
            StringAssert.Contains("fixedPanelPosition: {x: -0.31524, y: 1.45, z: 3.7437}", scene);
            StringAssert.Contains("SetTextObjectActive(_titleText, true)", source);
        }

        [Test]
        public void RenamedNpcDebateSceneStillActivatesTutorialAndPauseMenu()
        {
            Type controllerType = typeof(NpcDebateLearningPhaseController);
            MethodInfo matcher = controllerType.GetMethod(
                "IsTargetSceneName",
                BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public);
            Assert.IsNotNull(matcher);
            Assert.IsTrue((bool)matcher.Invoke(null, new object[] { "01Level_NPCVsNPCDebate" }));
            Assert.IsTrue((bool)matcher.Invoke(null, new object[] { "Level_NPCVsNPCDebate" }));
            Assert.IsFalse((bool)matcher.Invoke(null, new object[] { "02Level_InteractiveNPCDebate 1" }));

            string scenePath = Path.Combine("Assets", "Game", "Scenes", "01Level_NPCVsNPCDebate.unity");
            Assert.IsTrue(File.Exists(scenePath));
            string pauseMenu = File.ReadAllText(Path.Combine("Assets", "Game", "Scripts", "DebatePauseMenu.cs"));
            StringAssert.Contains("01Level_NPCVsNPCDebate", pauseMenu);
        }

        [Test]
        public void AllRenamedDebateScenesAreMappedWithoutStaleBuildPaths()
        {
            string[] sceneFiles =
            {
                "01Level_NPCVsNPCDebate.unity",
                "03Level_PlayerVsNPCDebate.unity",
                "04 coach Agent.unity",
                "05Level_PlayerVsNPCDebate 1.unity"
            };

            foreach (string sceneFile in sceneFiles)
            {
                Assert.IsTrue(
                    File.Exists(Path.Combine("Assets", "Game", "Scenes", sceneFile)),
                    sceneFile + " is missing.");
            }

            string pauseMenu = File.ReadAllText(Path.Combine("Assets", "Game", "Scripts", "DebatePauseMenu.cs"));
            StringAssert.Contains("03Level_PlayerVsNPCDebate", pauseMenu);
            StringAssert.Contains("01Level_NPCVsNPCDebate", pauseMenu);
            StringAssert.Contains("04 coach Agent", pauseMenu);
            StringAssert.Contains("05Level_PlayerVsNPCDebate 1", pauseMenu);
            StringAssert.DoesNotContain("02Level_InteractiveNPCDebate 1", pauseMenu);

            string buildSettings = File.ReadAllText(Path.Combine("ProjectSettings", "EditorBuildSettings.asset"));
            foreach (string sceneFile in sceneFiles)
            {
                StringAssert.Contains("Assets/Game/Scenes/" + sceneFile, buildSettings);
            }

            StringAssert.DoesNotContain("Assets/Game/Scenes/backup/Level_InteractiveNPCDebate.unity", buildSettings);
            StringAssert.DoesNotContain("Assets/Game/Scenes/Level_SharedInitiativeOrchestration.unity", buildSettings);
            StringAssert.Contains("Assets/Game/Scenes/05Level_PlayerVsNPCDebate 1.unity", buildSettings);
        }

        [Test]
        public void TutorialVoicePracticeSuppressesConvaiAgentRepliesBeforeStreamingStarts()
        {
            string grpcSource = File.ReadAllText(Path.Combine(
                "Assets", "Convai", "Scripts", "Runtime", "Core", "ConvaiGRPCAPI.cs"));
            string controllerSource = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "NpcDebateLearningPhaseController.cs"));

            StringAssert.Contains("public static Func<bool> ShouldSuppressVoiceResponse", grpcSource);
            StringAssert.Contains(
                "_suppressCurrentVoiceResponse = ShouldSuppressVoiceResponse?.Invoke() == true",
                grpcSource);
            StringAssert.DoesNotContain(
                "ShouldSuppressVoiceResponse?.Invoke() != true",
                grpcSource,
                "The scene callback must not be invoked again from the background response worker.");
            StringAssert.Contains(
                "ConvaiGRPCAPI.ShouldSuppressVoiceResponse = ShouldSuppressTutorialVoiceResponse",
                controllerSource);
            StringAssert.Contains("SilenceConvaiAgentResponse(primaryDemoNPC)", controllerSource);
        }

        [Test]
        public void TutorialVoicePracticeUsesXfyunInsteadOfConvaiListening()
        {
            string controllerSource = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "NpcDebateLearningPhaseController.cs"));

            StringAssert.Contains("XfyunRealtimeTranscriber realtimeTranscriber", controllerSource);
            StringAssert.Contains("EnsureRealtimeTranscriber", controllerSource);
            StringAssert.Contains("SubscribeToRealtimeTranscriber", controllerSource);
            StringAssert.Contains("SessionStarted += HandleVoicePracticeSessionStarted", controllerSource);
            StringAssert.Contains("TranscriptUpdated += HandleVoicePracticeTranscriptUpdated", controllerSource);
            StringAssert.Contains("SessionCompleted += HandleVoicePracticeSessionCompleted", controllerSource);
            StringAssert.Contains("SessionFailed += HandleVoicePracticeSessionFailed", controllerSource);
            StringAssert.Contains("realtimeTranscriber.StartSession", controllerSource);
            StringAssert.Contains("realtimeTranscriber?.StopSession", controllerSource);
            StringAssert.Contains("TryHandleVoicePracticeTranscript(transcript)", controllerSource);
            StringAssert.DoesNotContain("primaryDemoNPC.StartListening()", controllerSource);
            StringAssert.DoesNotContain("primaryDemoNPC?.StopListening()", controllerSource);
        }

        [Test]
        public void VoicePracticeNarratesEveryCreeiStepInsteadOfOnlyTheStageIntroduction()
        {
            string controllerSource = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "NpcDebateLearningPhaseController.cs"));

            StringAssert.Contains("private void BeginVoicePracticePromptNarration()", controllerSource);
            StringAssert.Contains("PlayVoicePracticePromptNarration", controllerSource);
            StringAssert.Contains("BeginVoicePracticePromptNarration();", controllerSource);
            StringAssert.Contains("prompt.Prompt", controllerSource);
            StringAssert.Contains("prompt.Example", controllerSource);
            StringAssert.Contains(
                "return _stageNarrationInProgress || _voicePracticeConnecting ||",
                controllerSource);
        }

        [Test]
        public void SpotMissingMicroPracticePageIsRemovedFromTheTutorialSequence()
        {
            CollectionAssert.DoesNotContain(
                DebateLearningContent.StageSequence.Select(stage => stage.Key).ToArray(),
                DebateLearningStageKey.MicroPracticeSpotMissing);
            Assert.IsFalse(DebateLearningContent.StageSequence.Any(stage =>
                stage.Title.Contains("Micro Practice 1", StringComparison.OrdinalIgnoreCase)));
        }

        [Test]
        public void TranscriptGuidePointsToF5SceneMenuAndDisablesConvaiSettingsShortcut()
        {
            string[] transcriptPrefabs =
            {
                "Convai Transcript Canvas - Subtitle.prefab",
                "Convai Transcript Canvas - QA.prefab",
                "Convai Transcript Canvas - Chat.prefab"
            };

            foreach (string prefabName in transcriptPrefabs)
            {
                string prefab = File.ReadAllText(Path.Combine(
                    "Assets", "Convai", "Prefabs", "Transcript UI Canvases", "Default", prefabName));
                StringAssert.Contains("m_text: Scene Menu [F05]", prefab, prefabName);
                StringAssert.DoesNotContain("Settings [F10]", prefab, prefabName);
            }

            string inputPrefab = File.ReadAllText(Path.Combine(
                "Assets", "Convai", "Prefabs", "Utils", "Convai Input Manager.prefab"));
            int settingsAction = inputPrefab.IndexOf("SettingsKeyAction:", StringComparison.Ordinal);
            Assert.GreaterOrEqual(settingsAction, 0);
            string settingsSection = inputPrefab.Substring(settingsAction);
            StringAssert.Contains("m_SingletonActionBindings: []", settingsSection);
            StringAssert.DoesNotContain("m_Path: <Keyboard>/f5", settingsSection);
            StringAssert.DoesNotContain("m_Path: <Keyboard>/f10", inputPrefab);

            string inputManagerSource = File.ReadAllText(Path.Combine(
                "Assets", "Convai", "Scripts", "Runtime", "Core", "ConvaiInputManager.cs"));
            StringAssert.Contains("OpenSettingPanelKey = KeyCode.None", inputManagerSource);
            StringAssert.DoesNotContain("OpenSettingPanelKey = KeyCode.F10", inputManagerSource);

            string inputActions = File.ReadAllText(Path.Combine(
                "Assets", "Convai", "Resources", "Controls.inputactions"));
            string generatedControls = File.ReadAllText(Path.Combine(
                "Assets", "Convai", "Resources", "Controls.cs"));
            StringAssert.DoesNotContain("<Keyboard>/f10", inputActions);
            StringAssert.DoesNotContain("<Keyboard>/f10", generatedControls);

            string sceneMenuSource = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "DebugSceneJumpMenu.cs"));
            StringAssert.Contains("STUDY SCENE MENU", sceneMenuSource);
            StringAssert.Contains("WasF5Pressed", sceneMenuSource);
            StringAssert.Contains("WasEscapePressed", sceneMenuSource);
            StringAssert.Contains("Press F5 or Esc to close", sceneMenuSource);
            StringAssert.Contains("Exit Application", sceneMenuSource);
            StringAssert.Contains("Application.Quit()", sceneMenuSource);
            StringAssert.DoesNotContain("DEBUG SCENE JUMP", sceneMenuSource);
            StringAssert.DoesNotContain("bypass formal research", sceneMenuSource);
        }
    }
}
