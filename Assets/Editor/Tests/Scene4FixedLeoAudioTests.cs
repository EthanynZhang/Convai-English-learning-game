using System.IO;
using Game.EditorTools;
using NUnit.Framework;
using UnityEngine;

namespace Game.Debate.Tests
{
    public sealed class Scene4FixedLeoAudioTests
    {
        [Test]
        public void FixedLeoSpeechHasOnePackagedClipPathPerCreeiStage()
        {
            string[] segments = SharedInitiativeOrchestrationController.GetMockDebateLeoSpeechSegments();
            Assert.AreEqual(5, segments.Length);
            CollectionAssert.IsEmpty(Scene4FixedLeoAudioGenerator.FindMissingAssetPaths());
            Assert.AreEqual(5, SharedInitiativeOrchestrationController.MockDebateLeoKokoroSpeakerId);
            StringAssert.EndsWith(
                "/Leo_01_Claim",
                SharedInitiativeOrchestrationController.GetMockDebateLeoClipResourcePath(0));
            StringAssert.EndsWith(
                "/Leo_05_Impact",
                SharedInitiativeOrchestrationController.GetMockDebateLeoClipResourcePath(4));
        }

        [Test]
        public void PcmWriterProducesMono16BitWav()
        {
            byte[] wav = Scene4FixedLeoAudioGenerator.BuildPcm16Wav(
                new[] { -1f, 0f, 1f },
                24000);

            Assert.AreEqual("RIFF", System.Text.Encoding.ASCII.GetString(wav, 0, 4));
            Assert.AreEqual("WAVE", System.Text.Encoding.ASCII.GetString(wav, 8, 4));
            Assert.AreEqual(1, System.BitConverter.ToInt16(wav, 22));
            Assert.AreEqual(24000, System.BitConverter.ToInt32(wav, 24));
            Assert.AreEqual(16, System.BitConverter.ToInt16(wav, 34));
        }

        [Test]
        public void LocalBranchLoadsResourcesWithoutConvaiFallback()
        {
            string source = File.ReadAllText(Path.Combine(
                Application.dataPath,
                "Game/Scripts/SharedInitiativeOrchestrationController.cs"));

            StringAssert.Contains("ConversationFlowSettings.DisableConvai", source);
            StringAssert.Contains("Resources.Load<AudioClip>", source);
            StringAssert.Contains("Convai fallback is intentionally disabled", source);
        }

    }
}
