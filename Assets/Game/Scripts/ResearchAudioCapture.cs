using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;

namespace Game.Debate
{
    public sealed class XfyunAudioCapture
    {
        public XfyunAudioCapture(byte[] pcm16Bytes, int sampleRateHz, int channels)
        {
            Pcm16Bytes = pcm16Bytes ?? Array.Empty<byte>();
            SampleRateHz = sampleRateHz;
            Channels = channels;
        }

        public byte[] Pcm16Bytes { get; }
        public int SampleRateHz { get; }
        public int Channels { get; }
        public float DurationSeconds => SampleRateHz <= 0 || Channels <= 0
            ? 0f
            : (float)Pcm16Bytes.Length / (sizeof(short) * SampleRateHz * Channels);
    }

    [Serializable]
    public sealed class ResearchAudioArtifactRecord
    {
        public int SchemaVersion;
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public string SceneId = string.Empty;
        public string StudyStage = string.Empty;
        public string TopicId = string.Empty;
        public string AttemptId = string.Empty;
        public string AudioFileId = string.Empty;
        public string ResponseRole = string.Empty;
        public string RelativePath = string.Empty;
        public float DurationSeconds;
        public int SampleRateHz;
        public int Channels;
        public long ByteCount;
        public string Sha256 = string.Empty;
        public string SavedAtUtc = string.Empty;
    }

    public sealed class ResearchAudioRecorder
    {
        public ResearchAudioArtifactRecord SaveCapture(
            string sessionDirectory,
            string sceneId,
            string responseRole,
            string attemptId,
            XfyunAudioCapture capture)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory))
                throw new ArgumentException("A session directory is required.", nameof(sessionDirectory));
            if (capture == null || capture.Pcm16Bytes.Length == 0)
                throw new ArgumentException("Captured PCM16 audio is required.", nameof(capture));
            if (capture.SampleRateHz <= 0 || capture.Channels <= 0 ||
                capture.Pcm16Bytes.Length % (sizeof(short) * capture.Channels) != 0)
                throw new ArgumentException("Captured PCM16 audio has an invalid shape.", nameof(capture));

            string safeAttempt = ResearchSessionPaths.SanitizeSegment(attemptId);
            string safeRole = ResearchSessionPaths.SanitizeSegment(responseRole);
            string safeScene = ResearchSessionPaths.SanitizeSegment(sceneId);
            if (string.IsNullOrWhiteSpace(safeAttempt))
                throw new ArgumentException("An attempt ID is required.", nameof(attemptId));
            if (string.IsNullOrWhiteSpace(safeRole)) safeRole = "speech";
            if (string.IsNullOrWhiteSpace(safeScene)) safeScene = "unknown";

            string fullSessionDirectory = Path.GetFullPath(sessionDirectory);
            string audioDirectory = Path.Combine(fullSessionDirectory, "audio");
            Directory.CreateDirectory(audioDirectory);
            string prefix = BuildPrefix(safeScene, safeRole);
            string fileName = prefix + "_" + safeAttempt + ".wav";
            string fullPath = Path.GetFullPath(Path.Combine(audioDirectory, fileName));
            string audioPrefix = audioDirectory.TrimEnd(
                                     Path.DirectorySeparatorChar,
                                     Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            if (!fullPath.StartsWith(audioPrefix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The research audio path escaped its session directory.");

            byte[] wav = BuildPcm16Wav(capture);
            string temporaryPath = fullPath + ".tmp";
            File.WriteAllBytes(temporaryPath, wav);
            File.Copy(temporaryPath, fullPath, true);
            File.Delete(temporaryPath);

            return new ResearchAudioArtifactRecord
            {
                AttemptId = safeAttempt,
                AudioFileId = Guid.NewGuid().ToString("N"),
                ResponseRole = safeRole,
                RelativePath = Path.Combine("audio", fileName).Replace('\\', '/'),
                DurationSeconds = capture.DurationSeconds,
                SampleRateHz = capture.SampleRateHz,
                Channels = capture.Channels,
                ByteCount = wav.LongLength,
                Sha256 = ComputeSha256(wav),
                SavedAtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        private static string BuildPrefix(string sceneId, string responseRole)
        {
            return sceneId switch
            {
                "03" => "scene03_baseline",
                "04" => "scene04_" + responseRole,
                "05" => "scene05_transfer",
                _ => "scene" + sceneId + "_" + responseRole
            };
        }

        private static byte[] BuildPcm16Wav(XfyunAudioCapture capture)
        {
            int dataLength = capture.Pcm16Bytes.Length;
            int blockAlign = capture.Channels * sizeof(short);
            int byteRate = capture.SampleRateHz * blockAlign;
            using MemoryStream stream = new(44 + dataLength);
            using BinaryWriter writer = new(stream, Encoding.ASCII, true);
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + dataLength);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));
            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(16);
            writer.Write((short)1);
            writer.Write((short)capture.Channels);
            writer.Write(capture.SampleRateHz);
            writer.Write(byteRate);
            writer.Write((short)blockAlign);
            writer.Write((short)16);
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataLength);
            writer.Write(capture.Pcm16Bytes);
            writer.Flush();
            return stream.ToArray();
        }

        private static string ComputeSha256(byte[] bytes)
        {
            using SHA256 sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(bytes);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }
    }

    [Serializable]
    public sealed class ResearchCompletionSummary
    {
        public int SchemaVersion;
        public string ParticipantId = string.Empty;
        public string SessionId = string.Empty;
        public bool DataComplete;
        public string MissingRequiredData = string.Empty;
        public int TranscriptCount;
        public int AudioArtifactCount;
        public string[] MissingAudioAttemptIds = Array.Empty<string>();
        public string[] OrphanAudioAttemptIds = Array.Empty<string>();
        public string GeneratedAtUtc = string.Empty;
    }

    public static class ResearchCompletenessChecker
    {
        public static ResearchCompletionSummary Evaluate(
            string sessionDirectory,
            ResearchSessionSnapshot snapshot)
        {
            if (string.IsNullOrWhiteSpace(sessionDirectory))
                throw new ArgumentException("A session directory is required.", nameof(sessionDirectory));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            string fullDirectory = Path.GetFullPath(sessionDirectory);
            string[] transcriptAttempts = ReadAttemptIds<ResearchTranscriptRecord>(
                Path.Combine(fullDirectory, "transcripts.jsonl"),
                row => row.AttemptId);
            string[] audioAttempts = ReadAttemptIds<ResearchAudioArtifactRecord>(
                Path.Combine(fullDirectory, "audio_artifacts.jsonl"),
                row => row.AttemptId);
            string[] missingAudio = transcriptAttempts.Except(audioAttempts, StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
            string[] orphanAudio = audioAttempts.Except(transcriptAttempts, StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();

            return new ResearchCompletionSummary
            {
                SchemaVersion = ResearchSessionPaths.SchemaVersion,
                ParticipantId = snapshot.ParticipantId,
                SessionId = snapshot.SessionId,
                DataComplete = missingAudio.Length == 0 && orphanAudio.Length == 0,
                TranscriptCount = transcriptAttempts.Length,
                AudioArtifactCount = audioAttempts.Length,
                MissingAudioAttemptIds = missingAudio,
                OrphanAudioAttemptIds = orphanAudio,
                GeneratedAtUtc = DateTimeOffset.UtcNow.ToString("o", CultureInfo.InvariantCulture)
            };
        }

        private static string[] ReadAttemptIds<T>(string path, Func<T, string> selector)
            where T : class
        {
            if (!File.Exists(path)) return Array.Empty<string>();
            List<string> values = new();
            int lineNumber = 0;
            foreach (string line in File.ReadLines(path))
            {
                lineNumber++;
                if (string.IsNullOrWhiteSpace(line)) continue;
                T record;
                try
                {
                    record = JsonConvert.DeserializeObject<T>(line);
                }
                catch (JsonException exception)
                {
                    throw new InvalidDataException(
                        $"Invalid JSONL record in {Path.GetFileName(path)} at line {lineNumber}.",
                        exception);
                }
                if (record == null) continue;
                string value = selector(record)?.Trim() ?? string.Empty;
                if (!string.IsNullOrWhiteSpace(value)) values.Add(value);
            }
            return values.Distinct(StringComparer.Ordinal)
                .OrderBy(value => value, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
