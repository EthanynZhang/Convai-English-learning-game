using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using Game.Debate;
using PonyuDev.SherpaOnnx.Tts;
using PonyuDev.SherpaOnnx.Tts.Engine;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    public static class Scene4FixedLeoAudioGenerator
    {
        public const string GenerateMenuPath =
            "Tools/Debate/Generate Scene 4 Fixed Leo Audio (Kokoro speaker5)";
        public const string ValidateMenuPath =
            "Tools/Debate/Validate Scene 4 Fixed Leo Audio";
        public const string AssetFolder =
            "Assets/Resources/DebateLearningTts/Scene04MockDebate";

        private static bool _isGenerating;

        [MenuItem(GenerateMenuPath)]
        public static void Generate()
        {
            if (_isGenerating)
            {
                Debug.LogWarning("[Scene04FixedLeoAudio] Generation is already running.");
                return;
            }

            _isGenerating = true;
            TtsService service = null;
            try
            {
                Directory.CreateDirectory(Path.Combine(
                    Directory.GetCurrentDirectory(),
                    AssetFolder.Replace('/', Path.DirectorySeparatorChar)));

                service = new TtsService();
                Debug.Log("[Scene04FixedLeoAudio] Initializing local Kokoro model...");
                service.Initialize();
                if (!service.IsReady)
                {
                    throw new InvalidOperationException(
                        "The local Kokoro model is not ready. Check StreamingAssets/SherpaOnnx/tts-settings.json and model files.");
                }

                string[] segments = SharedInitiativeOrchestrationController.GetMockDebateLeoSpeechSegments();
                for (int index = 0; index < segments.Length; index++)
                {
                    EditorUtility.DisplayProgressBar(
                        "Generating Scene 4 Leo audio",
                        $"Kokoro speaker5: segment {index + 1}/{segments.Length}",
                        index / (float)segments.Length);

                    (float[] samples, int sampleRate) = GenerateSegment(service, segments[index]);

                    string resourcePath =
                        SharedInitiativeOrchestrationController.GetMockDebateLeoClipResourcePath(index);
                    string assetPath = "Assets/Resources/" + resourcePath + ".wav";
                    string absolutePath = Path.Combine(
                        Directory.GetCurrentDirectory(),
                        assetPath.Replace('/', Path.DirectorySeparatorChar));
                    File.WriteAllBytes(
                        absolutePath,
                        BuildPcm16Wav(samples, sampleRate));
                    Debug.Log(
                        $"[Scene04FixedLeoAudio] Wrote segment {index + 1}: {assetPath} " +
                        $"({samples.Length / (float)sampleRate:0.0}s, {sampleRate} Hz)");
                }

                service.Dispose();
                service = null;
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                for (int index = 0; index < segments.Length; index++)
                {
                    string assetPath = "Assets/Resources/" +
                                       SharedInitiativeOrchestrationController
                                           .GetMockDebateLeoClipResourcePath(index) +
                                       ".wav";
                    ConfigureAudioImporter(assetPath);
                }
                Debug.Log(
                    $"[Scene04FixedLeoAudio] Generated {segments.Length} packaged clips with Kokoro " +
                    $"speaker{SharedInitiativeOrchestrationController.MockDebateLeoKokoroSpeakerId}. " +
                    "They are included in builds and require no Convai request.");
            }
            catch (Exception exception)
            {
                Debug.LogError("[Scene04FixedLeoAudio] Generation failed: " + exception);
            }
            finally
            {
                EditorUtility.ClearProgressBar();
                service?.Dispose();
                _isGenerating = false;
            }
        }

        [MenuItem(ValidateMenuPath)]
        public static void Validate()
        {
            string[] missing = FindMissingAssetPaths();
            if (missing.Length == 0)
            {
                Debug.Log("[Scene04FixedLeoAudio] Validation passed: all five packaged clips are present.");
                return;
            }

            Debug.LogError(
                "[Scene04FixedLeoAudio] Missing packaged clips:\n" + string.Join("\n", missing));
        }

        public static string[] FindMissingAssetPaths()
        {
            string[] segments = SharedInitiativeOrchestrationController.GetMockDebateLeoSpeechSegments();
            System.Collections.Generic.List<string> missing = new();
            for (int index = 0; index < segments.Length; index++)
            {
                string assetPath = "Assets/Resources/" +
                                   SharedInitiativeOrchestrationController
                                       .GetMockDebateLeoClipResourcePath(index) +
                                   ".wav";
                if (!File.Exists(Path.Combine(
                        Directory.GetCurrentDirectory(),
                        assetPath.Replace('/', Path.DirectorySeparatorChar))))
                {
                    missing.Add(assetPath);
                }
            }

            return missing.ToArray();
        }

        public static byte[] BuildPcm16Wav(float[] samples, int sampleRate)
        {
            if (samples == null)
            {
                throw new ArgumentNullException(nameof(samples));
            }

            if (sampleRate <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sampleRate));
            }

            int dataLength = checked(samples.Length * sizeof(short));
            using MemoryStream stream = new(44 + dataLength);
            using BinaryWriter writer = new(stream, Encoding.UTF8, true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(sampleRate);
            writer.Write(sampleRate * sizeof(short));
            writer.Write((short)sizeof(short));
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);

            foreach (float sample in samples)
            {
                float clamped = Mathf.Clamp(sample, -1f, 1f);
                writer.Write((short)Mathf.RoundToInt(clamped * short.MaxValue));
            }

            writer.Flush();
            return stream.ToArray();
        }

        private static (float[] Samples, int SampleRate) GenerateSegment(
            TtsService service,
            string text)
        {
            string[] sentences = Regex.Split(text.Trim(), @"(?<=[.!?])\s+");
            List<float> combined = new();
            int sampleRate = 0;
            for (int sentenceIndex = 0; sentenceIndex < sentences.Length; sentenceIndex++)
            {
                string sentence = sentences[sentenceIndex].Trim();
                if (sentence.Length == 0)
                {
                    continue;
                }

                using TtsResult result = service.Generate(
                    sentence,
                    SharedInitiativeOrchestrationController.MockDebateLeoKokoroSpeed,
                    SharedInitiativeOrchestrationController.MockDebateLeoKokoroSpeakerId);
                if (result == null || !result.IsValid)
                {
                    throw new InvalidOperationException(
                        $"Kokoro returned no audio for sentence {sentenceIndex + 1}: {sentence}");
                }

                if (sampleRate == 0)
                {
                    sampleRate = result.SampleRate;
                }
                else if (sampleRate != result.SampleRate)
                {
                    throw new InvalidOperationException("Kokoro returned inconsistent sample rates.");
                }

                combined.AddRange(result.Samples);
                if (sentenceIndex < sentences.Length - 1)
                {
                    int pauseSamples = Mathf.RoundToInt(sampleRate * 0.12f);
                    for (int pauseIndex = 0; pauseIndex < pauseSamples; pauseIndex++)
                    {
                        combined.Add(0f);
                    }
                }
            }

            if (sampleRate <= 0 || combined.Count == 0)
            {
                throw new InvalidOperationException("The fixed Leo segment contained no synthesizable text.");
            }

            return (combined.ToArray(), sampleRate);
        }

        private static void ConfigureAudioImporter(string assetPath)
        {
            if (AssetImporter.GetAtPath(assetPath) is not AudioImporter importer)
            {
                throw new InvalidOperationException("AudioImporter was not created for " + assetPath);
            }

            importer.forceToMono = true;
            importer.loadInBackground = false;
            AudioImporterSampleSettings settings = importer.defaultSampleSettings;
            settings.loadType = AudioClipLoadType.DecompressOnLoad;
            settings.compressionFormat = AudioCompressionFormat.PCM;
            settings.sampleRateSetting = AudioSampleRateSetting.PreserveSampleRate;
            settings.preloadAudioData = true;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
        }
    }
}
