using System;
using System.Collections.Generic;
using System.IO;
using Game.Debate;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Game.Editor
{
    public static class InternalResearchWindowsBuilder
    {
        private const string CredentialAssetPath =
            "Assets/Resources/InternalTestApiCredentials.asset";
        private const string OutputDirectory =
            "Builds/InternalResearch_Windows_2026-07-22";
        private const string OutputExecutable = "Debatequick_Internal.exe";

        public static void BuildFromCommandLine()
        {
            try
            {
                InternalTestApiCredentials credentials = InjectCredentials();
                ValidateConvaiCredential();
                if (!credentials.IsComplete)
                {
                    throw new BuildFailedException(
                        "The embedded internal credential asset is incomplete.");
                }

                string projectRoot = Directory.GetParent(Application.dataPath)!.FullName;
                string outputFolder = Path.Combine(projectRoot, OutputDirectory);
                string executablePath = Path.Combine(outputFolder, OutputExecutable);
                Directory.CreateDirectory(outputFolder);

                List<string> scenes = new();
                foreach (EditorBuildSettingsScene scene in EditorBuildSettings.scenes)
                {
                    if (scene.enabled && !string.IsNullOrWhiteSpace(scene.path))
                    {
                        scenes.Add(scene.path);
                    }
                }

                if (scenes.Count == 0)
                {
                    throw new BuildFailedException("No enabled scenes exist in Build Settings.");
                }

                BuildPlayerOptions options = new()
                {
                    scenes = scenes.ToArray(),
                    locationPathName = executablePath,
                    target = BuildTarget.StandaloneWindows64,
                    options = BuildOptions.None
                };
                BuildReport report = BuildPipeline.BuildPlayer(options);
                if (report.summary.result != BuildResult.Succeeded)
                {
                    throw new BuildFailedException(
                        "Windows build failed with result " + report.summary.result + ".");
                }

                WriteInternalBuildReadme(outputFolder, report);
                WriteResearchDataReadme(outputFolder);
                Debug.Log(
                    "[InternalBuild] SUCCESS. Embedded credentials: MiniMax=SET, " +
                    "CoachRelay=SET, Xfyun=SET, Convai=SET. Output=" + executablePath +
                    "; bytes=" + report.summary.totalSize +
                    "; warnings=" + report.summary.totalWarnings + ".");
            }
            catch (Exception exception)
            {
                Debug.LogError("[InternalBuild] FAILED: " + exception.Message);
                throw;
            }
        }

        private static InternalTestApiCredentials InjectCredentials()
        {
            string miniMaxApiKey = ReadRequiredEnvironment("MINIMAX_API_KEY");
            string debateApiKey = ReadRequiredEnvironment("DEBATE_OPENAI_API_KEY");
            string debateBaseUrl = ReadRequiredEnvironment("DEBATE_OPENAI_BASE_URL");
            string xfyunAppId = ReadRequiredEnvironment("XFYUN_RTASR_APP_ID");
            string xfyunApiKey = ReadRequiredEnvironment("XFYUN_RTASR_API_KEY");

            Directory.CreateDirectory("Assets/Resources");
            InternalTestApiCredentials credentials =
                AssetDatabase.LoadAssetAtPath<InternalTestApiCredentials>(CredentialAssetPath);
            if (credentials == null)
            {
                credentials = ScriptableObject.CreateInstance<InternalTestApiCredentials>();
                AssetDatabase.CreateAsset(credentials, CredentialAssetPath);
            }

            SerializedObject serialized = new(credentials);
            serialized.FindProperty("miniMaxApiKey").stringValue = miniMaxApiKey;
            serialized.FindProperty("debateApiKey").stringValue = debateApiKey;
            serialized.FindProperty("debateBaseUrl").stringValue = debateBaseUrl;
            serialized.FindProperty("xfyunAppId").stringValue = xfyunAppId;
            serialized.FindProperty("xfyunApiKey").stringValue = xfyunApiKey;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(credentials);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log(
                "[InternalBuild] Credential asset prepared. MiniMax=SET, " +
                "CoachRelay=SET, Xfyun=SET. Values are intentionally not logged.");
            return credentials;
        }

        private static string ReadRequiredEnvironment(string variableName)
        {
            string value = Environment.GetEnvironmentVariable(variableName);
#if UNITY_EDITOR_WIN
            if (string.IsNullOrWhiteSpace(value))
            {
                value = Environment.GetEnvironmentVariable(
                    variableName,
                    EnvironmentVariableTarget.User);
            }
#endif
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new BuildFailedException(
                    "Required internal-build environment variable is missing: " +
                    variableName + ".");
            }

            return value.Trim();
        }

        private static void ValidateConvaiCredential()
        {
            ConvaiAPIKeySetup convai = Resources.Load<ConvaiAPIKeySetup>("ConvaiAPIKey");
            if (convai == null || string.IsNullOrWhiteSpace(convai.APIKey))
            {
                throw new BuildFailedException(
                    "ConvaiAPIKey Resources asset is missing or empty.");
            }

            Debug.Log("[InternalBuild] Convai credential asset is SET.");
        }

        private static void WriteInternalBuildReadme(
            string outputFolder,
            BuildReport report)
        {
            string readmePath = Path.Combine(outputFolder, "README_INTERNAL.txt");
            string content =
                "Debatequick Internal Research Build\r\n" +
                "Built: " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\r\n" +
                "Scenes: 01, 03, 04, 05\r\n" +
                "Embedded services: MiniMax TTS, Coach Relay, Xfyun ASR, Convai\r\n" +
                "\r\n" +
                "Keep the EXE, Debatequick_Internal_Data folder, UnityPlayer.dll, " +
                "and all other files together.\r\n" +
                "Research files are saved in the ResearchData folder beside the EXE.\r\n" +
                "This package is for authorized internal research testing only.\r\n" +
                "Internet access and Windows microphone permission are required.\r\n" +
                "Build size: " + report.summary.totalSize + " bytes\r\n";
            File.WriteAllText(readmePath, content);
        }

        private static void WriteResearchDataReadme(string outputFolder)
        {
            string dataFolder = Path.Combine(outputFolder, "ResearchData");
            Directory.CreateDirectory(dataFolder);
            string content =
                "Debatequick research data folder\r\n" +
                "\r\n" +
                "New sessions are stored under schema_v2\\<participant ID>\\<session ID>.\r\n" +
                "Each completed session contains CSV files in its exports folder.\r\n" +
                "Audio recordings are stored in its audio folder.\r\n" +
                "Older compatibility CSV logs are stored under legacy_logs.\r\n" +
                "\r\n" +
                "If this application folder is not writable, data automatically falls back to:\r\n" +
                "%USERPROFILE%\\AppData\\LocalLow\\DebateAI\\Debatequick\\ResearchData\r\n";
            File.WriteAllText(Path.Combine(dataFolder, "WHERE_IS_MY_DATA.txt"), content);
        }
    }
}
