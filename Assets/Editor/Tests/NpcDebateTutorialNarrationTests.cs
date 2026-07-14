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

            MethodInfo buildRequest = clientType.GetMethod("BuildRequestJson", BindingFlags.Public | BindingFlags.Static);
            MethodInfo computeCacheKey = clientType.GetMethod("ComputeCacheKey", BindingFlags.Public | BindingFlags.Static);
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
        public void AudioDrivenLipSyncExposesRmsCalculationAndJawOpenDriver()
        {
            Type lipSyncType = typeof(DebateLearningContent).Assembly.GetType("Game.Debate.AudioDrivenNpcLipSync");
            Assert.IsNotNull(lipSyncType, "AudioDrivenNpcLipSync must animate external TTS audio.");

            MethodInfo calculateRms = lipSyncType.GetMethod("CalculateRms", BindingFlags.Public | BindingFlags.Static);
            Assert.IsNotNull(calculateRms);
            float rms = (float)calculateRms.Invoke(null, new object[] { new[] { 0.5f, -0.5f, 0.5f, -0.5f } });
            Assert.AreEqual(0.5f, rms, 0.0001f);

            string source = File.ReadAllText(Path.Combine("Assets", "Game", "Scripts", "AudioDrivenNpcLipSync.cs"));
            StringAssert.Contains("GetOutputData", source);
            StringAssert.Contains("jawOpen", source);
            StringAssert.Contains("SetBlendShapeWeight", source);
            StringAssert.Contains("SetBool(talkParameter", source);
            StringAssert.Contains("mouthGain = 1.2f", source);
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
        public void AnnaDialogueUsesTheSameMiniMaxVoiceAsStageNarration()
        {
            string source = File.ReadAllText(Path.Combine(
                "Assets", "Game", "Scripts", "NpcDebateLearningPhaseController.cs"));

            StringAssert.Contains("PlayAnnaDialogueLineWithStageVoice", source);
            StringAssert.Contains("_miniMaxTtsClient.RequestClip", source);
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
                "02Level_InteractiveNPCDebate 1.unity",
                "03Level_PlayerVsNPCDebate.unity",
                "04 coach Agent.unity"
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
            StringAssert.Contains("02Level_InteractiveNPCDebate 1", pauseMenu);
            StringAssert.Contains("04 coach Agent", pauseMenu);

            string buildSettings = File.ReadAllText(Path.Combine("ProjectSettings", "EditorBuildSettings.asset"));
            foreach (string sceneFile in sceneFiles)
            {
                StringAssert.Contains("Assets/Game/Scenes/" + sceneFile, buildSettings);
            }

            StringAssert.DoesNotContain("Assets/Game/Scenes/backup/Level_InteractiveNPCDebate.unity", buildSettings);
            StringAssert.DoesNotContain("Assets/Game/Scenes/Level_SharedInitiativeOrchestration.unity", buildSettings);
            StringAssert.DoesNotContain("Assets/Game/Scenes/05Level_PlayerVsNPCDebate 1.unity", buildSettings);
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
    }
}
