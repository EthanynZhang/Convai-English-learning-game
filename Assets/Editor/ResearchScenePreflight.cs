using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Game.Debate;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.EditorTools
{
    [Serializable]
    public sealed class ResearchScenePreflightRow
    {
        public string SceneId = string.Empty;
        public string SceneName = string.Empty;
        public string ScenePath = string.Empty;
        public bool EnabledInBuild;
        public int BuildIndex = -1;
        public string RequiredComponents = string.Empty;
        public string ComponentCounts = string.Empty;
        public int MissingScriptCount;
        public bool Passed;
        public string[] Issues = Array.Empty<string>();
    }

    [Serializable]
    public sealed class ResearchScenePreflightResult
    {
        public bool Passed;
        public string BuildSequence = string.Empty;
        public string GeneratedAtUtc = string.Empty;
        public ResearchScenePreflightRow[] Scenes = Array.Empty<ResearchScenePreflightRow>();
        public string[] Issues = Array.Empty<string>();
    }

    public static class ResearchScenePreflightService
    {
        private sealed class SceneRule
        {
            public string SceneId;
            public string ScenePath;
            public string[] Required;
            public string[] Forbidden;
        }

        private static readonly Encoding Utf8WithoutBom = new UTF8Encoding(false);

        private static readonly SceneRule[] Rules =
        {
            new()
            {
                SceneId = "01",
                ScenePath = "Assets/Game/Scenes/01Level_NPCVsNPCDebate.unity",
                Required = new[]
                {
                    "NpcDebateLearningPhaseController",
                    "NpcDebateRoundManager"
                },
                Forbidden = Array.Empty<string>()
            },
            new()
            {
                SceneId = "03",
                ScenePath = "Assets/Game/Scenes/03Level_PlayerVsNPCDebate.unity",
                Required = new[] { "DebatePreparationController", "XfyunRealtimeTranscriber" },
                Forbidden = new[] { "CoachEpisodeController", "ThreeStageDebatePracticeController" }
            },
            new()
            {
                SceneId = "04",
                ScenePath = "Assets/Game/Scenes/04 coach Agent.unity",
                Required = new[]
                {
                    "ThreeStageDebatePracticeController",
                    "XfyunRealtimeTranscriber",
                    "CoachEpisodeController"
                },
                Forbidden = Array.Empty<string>()
            },
            new()
            {
                SceneId = "05",
                ScenePath = "Assets/Game/Scenes/05Level_PlayerVsNPCDebate 1.unity",
                Required = new[]
                {
                    "DebatePreparationController",
                    "XfyunRealtimeTranscriber",
                    "TransferDebateStageGuard"
                },
                Forbidden = new[]
                {
                    "CoachEpisodeController",
                    "ThreeStageDebatePracticeController",
                    "CoachResearchLogger"
                }
            }
        };

        public static ResearchScenePreflightResult EvaluateCurrentProject()
        {
            EditorBuildSettingsScene[] buildScenes = EditorBuildSettings.scenes;
            List<string> projectIssues = new();
            ResearchScenePreflightRow[] rows = Rules
                .Select((rule, expectedIndex) => EvaluateScene(rule, expectedIndex, buildScenes))
                .ToArray();

            string[] buildSequence = buildScenes
                .Where(scene => scene.enabled)
                .Select(scene => Rules.FirstOrDefault(rule =>
                    string.Equals(rule.ScenePath, scene.path, StringComparison.Ordinal)))
                .Where(rule => rule != null)
                .Select(rule => rule.SceneId)
                .ToArray();
            string sequenceText = string.Join(">", buildSequence);
            if (!buildSequence.SequenceEqual(Rules.Select(rule => rule.SceneId),
                    StringComparer.Ordinal))
                projectIssues.Add("formal_build_sequence_invalid");

            foreach (ResearchScenePreflightRow row in rows)
            {
                foreach (string issue in row.Issues)
                    projectIssues.Add("scene" + row.SceneId + ":" + issue);
            }

            return new ResearchScenePreflightResult
            {
                Passed = projectIssues.Count == 0,
                BuildSequence = sequenceText,
                GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture),
                Scenes = rows,
                Issues = projectIssues.Distinct(StringComparer.Ordinal)
                    .OrderBy(value => value, StringComparer.Ordinal).ToArray()
            };
        }

        public static string ExportCsv(string outputPath = null)
        {
            string path = string.IsNullOrWhiteSpace(outputPath)
                ? Path.Combine(ResearchSessionPaths.GetDefaultRootDirectory(),
                    "research_scene_preflight.csv")
                : Path.GetFullPath(outputPath);
            string directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrWhiteSpace(directory)) Directory.CreateDirectory(directory);

            ResearchScenePreflightResult result = EvaluateCurrentProject();
            StringBuilder csv = new();
            csv.AppendLine(string.Join(",", new[]
            {
                "generated_at_utc", "project_passed", "build_sequence", "scene_id",
                "scene_name", "scene_path", "enabled_in_build", "build_index",
                "required_components", "component_counts", "missing_script_count",
                "scene_passed", "scene_issues", "project_issues"
            }.Select(EscapeCsv)));
            foreach (ResearchScenePreflightRow row in result.Scenes)
            {
                string[] values =
                {
                    result.GeneratedAtUtc,
                    Bool(result.Passed),
                    result.BuildSequence,
                    row.SceneId,
                    row.SceneName,
                    row.ScenePath,
                    Bool(row.EnabledInBuild),
                    row.BuildIndex.ToString(CultureInfo.InvariantCulture),
                    row.RequiredComponents,
                    row.ComponentCounts,
                    row.MissingScriptCount.ToString(CultureInfo.InvariantCulture),
                    Bool(row.Passed),
                    string.Join(";", row.Issues),
                    string.Join(";", result.Issues)
                };
                csv.AppendLine(string.Join(",", values.Select(EscapeCsv)));
            }
            File.WriteAllText(path, csv.ToString(), Utf8WithoutBom);
            return path;
        }

        [MenuItem("Tools/Research/Export Scene Preflight")]
        public static void ExportFromMenu()
        {
            string path = ExportCsv();
            Debug.Log("Research scene preflight CSV exported: " + path);
            EditorUtility.RevealInFinder(path);
        }

        private static ResearchScenePreflightRow EvaluateScene(
            SceneRule rule,
            int expectedIndex,
            IReadOnlyList<EditorBuildSettingsScene> buildScenes)
        {
            List<string> issues = new();
            int buildIndex = -1;
            bool enabled = false;
            for (int index = 0; index < buildScenes.Count; index++)
            {
                if (!string.Equals(buildScenes[index].path, rule.ScenePath,
                        StringComparison.Ordinal)) continue;
                buildIndex = index;
                enabled = buildScenes[index].enabled;
                break;
            }
            if (buildIndex < 0) issues.Add("missing_from_build_settings");
            else if (!enabled) issues.Add("disabled_in_build_settings");
            if (buildIndex != expectedIndex) issues.Add("build_index_expected_" + expectedIndex);

            if (!File.Exists(Path.GetFullPath(rule.ScenePath)))
            {
                issues.Add("scene_asset_missing");
                return BuildRow(rule, buildIndex, enabled, string.Empty, 0, issues);
            }

            Scene scene = SceneManager.GetSceneByPath(rule.ScenePath);
            bool openedForInspection = !scene.IsValid() || !scene.isLoaded;
            if (openedForInspection)
                scene = EditorSceneManager.OpenScene(rule.ScenePath, OpenSceneMode.Additive);
            try
            {
                Component[] allComponents = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<Component>(true))
                    .ToArray();
                int missingScripts = allComponents.Count(component => component == null);
                if (missingScripts > 0) issues.Add("missing_script_components");

                Dictionary<string, Component[]> matches = rule.Required
                    .Concat(rule.Forbidden)
                    .Distinct(StringComparer.Ordinal)
                    .ToDictionary(
                        typeName => typeName,
                        typeName => allComponents.Where(component => component != null &&
                            string.Equals(component.GetType().Name, typeName,
                                StringComparison.Ordinal)).ToArray(),
                        StringComparer.Ordinal);
                foreach (string required in rule.Required)
                {
                    Component[] found = matches[required];
                    if (found.Length == 0)
                        issues.Add("required_component_missing:" + required);
                    else if (found.OfType<Behaviour>().Any(behaviour => !behaviour.enabled))
                        issues.Add("required_component_disabled:" + required);
                }
                foreach (string forbidden in rule.Forbidden)
                {
                    if (matches[forbidden].Length > 0)
                        issues.Add("forbidden_component_present:" + forbidden);
                }

                string counts = string.Join(";", matches.OrderBy(pair => pair.Key,
                        StringComparer.Ordinal)
                    .Select(pair => pair.Key + "=" + pair.Value.Length));
                return BuildRow(rule, buildIndex, enabled, counts, missingScripts, issues);
            }
            finally
            {
                if (openedForInspection) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static ResearchScenePreflightRow BuildRow(
            SceneRule rule,
            int buildIndex,
            bool enabled,
            string componentCounts,
            int missingScriptCount,
            ICollection<string> issues)
        {
            string[] orderedIssues = issues.OrderBy(value => value, StringComparer.Ordinal).ToArray();
            return new ResearchScenePreflightRow
            {
                SceneId = rule.SceneId,
                SceneName = Path.GetFileNameWithoutExtension(rule.ScenePath),
                ScenePath = rule.ScenePath,
                EnabledInBuild = enabled,
                BuildIndex = buildIndex,
                RequiredComponents = string.Join(";", rule.Required),
                ComponentCounts = componentCounts,
                MissingScriptCount = missingScriptCount,
                Passed = orderedIssues.Length == 0,
                Issues = orderedIssues
            };
        }

        private static string EscapeCsv(string value)
        {
            string clean = value ?? string.Empty;
            return clean.IndexOfAny(new[] { ',', '"', '\r', '\n' }) < 0
                ? clean
                : "\"" + clean.Replace("\"", "\"\"") + "\"";
        }

        private static string Bool(bool value) => value ? "true" : "false";
    }
}
