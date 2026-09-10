using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace Game.Tests.EditMode
{
    public sealed class InternalBuildCredentialsTests
    {
        [Test]
        public void InternalCredentialAssetStoresEveryRequiredServiceValue()
        {
            Type credentialType = typeof(Game.Debate.MiniMaxTtsClient).Assembly.GetType(
                "Game.Debate.InternalTestApiCredentials");
            Assert.IsNotNull(credentialType,
                "A runtime Resources credential asset is required for portable internal builds.");

            ScriptableObject asset = ScriptableObject.CreateInstance(credentialType);
            try
            {
                SetField(credentialType, asset, "miniMaxApiKey", "  minimax-test  ");
                SetField(credentialType, asset, "debateApiKey", "  debate-test  ");
                SetField(credentialType, asset, "debateBaseUrl", "  https://relay.test  ");
                SetField(credentialType, asset, "xfyunAppId", "  app-test  ");
                SetField(credentialType, asset, "xfyunApiKey", "  xfyun-test  ");

                Assert.AreEqual("minimax-test", GetProperty(credentialType, asset, "MiniMaxApiKey"));
                Assert.AreEqual("debate-test", GetProperty(credentialType, asset, "DebateApiKey"));
                Assert.AreEqual("https://relay.test", GetProperty(credentialType, asset, "DebateBaseUrl"));
                Assert.AreEqual("app-test", GetProperty(credentialType, asset, "XfyunAppId"));
                Assert.AreEqual("xfyun-test", GetProperty(credentialType, asset, "XfyunApiKey"));
                Assert.IsTrue((bool)credentialType.GetProperty("IsComplete")!.GetValue(asset));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }

        [Test]
        public void EveryRuntimeApiClientUsesTheEmbeddedCredentialFallback()
        {
            AssertSourceContains("Assets/Game/Scripts/MiniMaxTtsClient.cs",
                "InternalTestApiCredentials.Load");
            AssertSourceContains("Assets/Game/Scripts/DebateCommandParser.cs",
                "InternalTestApiCredentials.Load");
            AssertSourceContains("Assets/Game/Scripts/XfyunRealtimeTranscriber.cs",
                "InternalTestApiCredentials.Load");
            AssertSourceContains(".gitignore",
                "InternalTestApiCredentials.asset");
        }

        [Test]
        public void InternalWindowsBuilderInjectsAndValidatesCredentialsBeforeBuilding()
        {
            const string path = "Assets/Editor/InternalResearchWindowsBuilder.cs";
            Assert.IsTrue(File.Exists(path), "Missing internal Windows build pipeline.");
            string source = File.ReadAllText(path);
            foreach (string required in new[]
                     {
                         "MINIMAX_API_KEY",
                         "DEBATE_OPENAI_API_KEY",
                         "DEBATE_OPENAI_BASE_URL",
                         "XFYUN_RTASR_APP_ID",
                         "XFYUN_RTASR_API_KEY",
                         "ConvaiAPIKey",
                         "BuildTarget.StandaloneWindows64",
                         "BuildPipeline.BuildPlayer",
                         "InternalTestApiCredentials.asset",
                         "IsComplete",
                         "ResearchData",
                         "LocalLow\\\\DebateAI\\\\Debatequick"
                     })
            {
                StringAssert.Contains(required, source);
            }
        }

        private static void SetField(Type type, object target, string name, string value)
        {
            FieldInfo field = type.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, "Missing serialized field: " + name);
            field.SetValue(target, value);
        }

        private static string GetProperty(Type type, object target, string name)
        {
            PropertyInfo property = type.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
            Assert.IsNotNull(property, "Missing public property: " + name);
            return (string)property.GetValue(target);
        }

        private static void AssertSourceContains(string path, string expected)
        {
            Assert.IsTrue(File.Exists(path), "Missing source file: " + path);
            StringAssert.Contains(expected, File.ReadAllText(path));
        }
    }
}
