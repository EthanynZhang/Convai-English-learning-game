using System.IO;
using System.Collections.Generic;
using Game.Debate;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    public static class MiniMaxTtsCachePackager
    {
        private const string PackMenuPath = "Tools/Debate/Pack MiniMax TTS Cache Into Build...";
        private const string RevealMenuPath = "Tools/Debate/Open Packaged MiniMax TTS Cache";
        private const string ValidateMenuPath = "Tools/Debate/Validate Scene 1 Packaged TTS";

        [MenuItem(PackMenuPath)]
        public static void PackCacheIntoBuild()
        {
            string suggestedSource = MiniMaxTtsClient.GetRuntimeCacheDirectory();
            string sourceDirectory = EditorUtility.OpenFolderPanel(
                "Select the populated MiniMax TTS cache",
                Directory.Exists(suggestedSource) ? suggestedSource : Application.persistentDataPath,
                string.Empty);

            if (string.IsNullOrWhiteSpace(sourceDirectory))
            {
                return;
            }

            if (!Directory.Exists(sourceDirectory))
            {
                EditorUtility.DisplayDialog("MiniMax TTS cache", "The selected folder does not exist.", "OK");
                return;
            }

            string[] sourceFiles = Directory.GetFiles(sourceDirectory, "*.wav", SearchOption.TopDirectoryOnly);
            string destinationDirectory = MiniMaxTtsClient.GetPackagedCacheDirectory();
            Directory.CreateDirectory(destinationDirectory);

            int copied = 0;
            int skipped = 0;
            foreach (string sourceFile in sourceFiles)
            {
                if (!IsHashedCacheFile(sourceFile))
                {
                    skipped++;
                    continue;
                }

                string destinationFile = Path.Combine(destinationDirectory, Path.GetFileName(sourceFile));
                File.Copy(sourceFile, destinationFile, true);
                copied++;
            }

            AssetDatabase.Refresh();
            string[] missingEntries = FindMissingSceneOneEntries();
            EditorUtility.DisplayDialog(
                "MiniMax TTS cache packaged",
                $"Copied {copied} WAV file(s) into:\n{destinationDirectory}\n\n" +
                $"Skipped {skipped} file(s) whose names were not 64-character cache hashes. " +
                $"Scene 1 missing packaged entries: {missingEntries.Length}. " +
                "Player builds can load included clips without calling MiniMax.",
                "OK");
        }

        [MenuItem(ValidateMenuPath)]
        public static void ValidateSceneOneCache()
        {
            string[] missingEntries = FindMissingSceneOneEntries();
            if (missingEntries.Length == 0)
            {
                EditorUtility.DisplayDialog(
                    "Scene 1 packaged TTS",
                    "All fixed Scene 1 narrations and dialogue clips are available locally.",
                    "OK");
                return;
            }

            EditorUtility.DisplayDialog(
                "Scene 1 packaged TTS",
                "Missing local audio:\n\n" + string.Join("\n", missingEntries) +
                "\n\nRun Scene 1 through these items in the editor, then pack the cache again.",
                "OK");
        }

        [MenuItem(RevealMenuPath)]
        public static void RevealPackagedCache()
        {
            string destinationDirectory = MiniMaxTtsClient.GetPackagedCacheDirectory();
            Directory.CreateDirectory(destinationDirectory);
            EditorUtility.RevealInFinder(destinationDirectory);
        }

        public static bool IsHashedCacheFile(string path)
        {
            string name = Path.GetFileNameWithoutExtension(path);
            if (name.Length != 64)
            {
                return false;
            }

            foreach (char value in name)
            {
                if (!System.Uri.IsHexDigit(value))
                {
                    return false;
                }
            }

            return string.Equals(Path.GetExtension(path), ".wav", System.StringComparison.OrdinalIgnoreCase);
        }

        public static string[] FindMissingSceneOneEntries()
        {
            List<string> missing = new();

            foreach (DebateLearningStageSpec stage in DebateLearningContent.StageSequence)
            {
                if (stage.IsTerminal)
                {
                    continue;
                }

                string body = (stage.Body ?? string.Empty)
                    .Replace("\r", " ")
                    .Replace("\n", " ")
                    .Replace("_", string.Empty);
                string narration = string.IsNullOrWhiteSpace(body)
                    ? stage.Title
                    : stage.Title + ". " + body;
                AddIfPackagedClipMissing(missing, "Stage narration: " + stage.Title, narration, MiniMaxTtsClient.FemaleVoiceId);
            }

            foreach (CreeiVoicePracticePrompt prompt in DebateLearningContent.CreeiVoicePracticePrompts)
            {
                string narration = $"{prompt.Title}. {prompt.Prompt} {prompt.Example}";
                AddIfPackagedClipMissing(missing, "Voice prompt: " + prompt.Title, narration, MiniMaxTtsClient.FemaleVoiceId);
            }

            foreach (DebateLearningStageSpec stage in DebateLearningContent.StageSequence)
            {
                DemoDialogueLine[] lines = DebateLearningContent.GetDialogueLines(stage.Key);
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    DemoDialogueLine line = lines[lineIndex];
                    string resourcePath = "Assets/Resources/" +
                                          DebateLearningContent.GetDialogueClipResourcePath(stage.Key, lineIndex, line) +
                                          ".wav";
                    if (File.Exists(resourcePath))
                    {
                        continue;
                    }

                    string voiceId = string.Equals(line.SpeakerId, "Mike", System.StringComparison.OrdinalIgnoreCase)
                        ? MiniMaxTtsClient.MaleVoiceId
                        : MiniMaxTtsClient.FemaleVoiceId;
                    AddIfPackagedClipMissing(
                        missing,
                        $"Dialogue: {stage.Title} line {lineIndex + 1} ({line.SpeakerName})",
                        line.Text,
                        voiceId);
                }
            }

            return missing.ToArray();
        }

        private static void AddIfPackagedClipMissing(
            ICollection<string> missing,
            string label,
            string text,
            string voiceId)
        {
            string path = MiniMaxTtsClient.GetPackagedCachePath(text, voiceId);
            if (!File.Exists(path))
            {
                missing.Add(label + " -> " + Path.GetFileName(path));
            }
        }
    }
}
