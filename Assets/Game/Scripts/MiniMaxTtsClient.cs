using System;
using System.Collections;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using UnityEngine.Networking;

namespace Game.Debate
{
    public sealed class MiniMaxTtsClient : MonoBehaviour
    {
        public const string ApiKeyEnvironmentVariable = "MINIMAX_API_KEY";
        public const string Endpoint = "https://api.minimax.io/v1/t2a_v2";
        public const string Model = "speech-02-hd";
        public const string FemaleVoiceId = "English_Graceful_Lady";
        public const string MaleVoiceId = "English_Gentle-voiced_man";
        public const string VoiceId = FemaleVoiceId;
        public const string CacheDirectoryName = "minimax_tts_cache";
        // Never commit a live credential. Local and CI builds inject this through the environment.
        private const string EmbeddedInternalTestApiKey = "";

        [SerializeField, Range(0.5f, 2f)] private float speed = 0.92f;
        [SerializeField, Range(0.1f, 10f)] private float volume = 1f;
        [SerializeField, Range(-12, 12)] private int pitch;
        [SerializeField, Min(5)] private int timeoutSeconds = 40;

        public IEnumerator RequestClip(
            string text,
            int generation,
            Func<int, bool> isGenerationCurrent,
            Action<AudioClip> onSuccess,
            Action<string> onFailure)
        {
            yield return RequestClipInternal(
                text,
                FemaleVoiceId,
                generation,
                isGenerationCurrent,
                onSuccess,
                onFailure,
                true);
        }

        public IEnumerator RequestClip(
            string text,
            string voiceId,
            int generation,
            Func<int, bool> isGenerationCurrent,
            Action<AudioClip> onSuccess,
            Action<string> onFailure)
        {
            yield return RequestClipInternal(
                text,
                voiceId,
                generation,
                isGenerationCurrent,
                onSuccess,
                onFailure,
                true);
        }

        public IEnumerator RequestLocalClip(
            string text,
            int generation,
            Func<int, bool> isGenerationCurrent,
            Action<AudioClip> onSuccess,
            Action<string> onFailure)
        {
            yield return RequestClipInternal(
                text,
                FemaleVoiceId,
                generation,
                isGenerationCurrent,
                onSuccess,
                onFailure,
                false);
        }

        public IEnumerator RequestLocalClip(
            string text,
            string voiceId,
            int generation,
            Func<int, bool> isGenerationCurrent,
            Action<AudioClip> onSuccess,
            Action<string> onFailure)
        {
            yield return RequestClipInternal(
                text,
                voiceId,
                generation,
                isGenerationCurrent,
                onSuccess,
                onFailure,
                false);
        }

        private IEnumerator RequestClipInternal(
            string text,
            string voiceId,
            int generation,
            Func<int, bool> isGenerationCurrent,
            Action<AudioClip> onSuccess,
            Action<string> onFailure,
            bool allowNetwork)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                onFailure?.Invoke("MiniMax TTS text is empty.");
                yield break;
            }

            string resolvedVoiceId = NormalizeVoiceId(voiceId);
            string cachePath = GetCachePath(text, resolvedVoiceId);
            if (File.Exists(cachePath))
            {
                bool loaded = false;
                yield return LoadCachedClip(
                    cachePath,
                    generation,
                    isGenerationCurrent,
                    clip =>
                    {
                        loaded = true;
                        onSuccess?.Invoke(clip);
                    },
                    null,
                    true);
                if (!IsCurrent(generation, isGenerationCurrent))
                {
                    yield break;
                }

                if (loaded)
                {
                    yield break;
                }
            }

            string packagedCachePath = GetPackagedCachePath(text, resolvedVoiceId);
            if (File.Exists(packagedCachePath))
            {
                bool loaded = false;
                string packagedCacheError = string.Empty;
                yield return LoadCachedClip(
                    packagedCachePath,
                    generation,
                    isGenerationCurrent,
                    clip =>
                    {
                        loaded = true;
                        onSuccess?.Invoke(clip);
                    },
                    error => packagedCacheError = error,
                    false);
                if (!IsCurrent(generation, isGenerationCurrent))
                {
                    yield break;
                }

                if (loaded)
                {
                    yield break;
                }

                if (!allowNetwork)
                {
                    onFailure?.Invoke(packagedCacheError);
                    yield break;
                }
            }

            if (!allowNetwork)
            {
                onFailure?.Invoke(
                    $"Packaged MiniMax TTS WAV is missing: {Path.GetFileName(packagedCachePath)}. " +
                    $"Pack the developer cache into StreamingAssets/{CacheDirectoryName} before building.");
                yield break;
            }

            string apiKey = ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onFailure?.Invoke($"MiniMax TTS is unavailable because {ApiKeyEnvironmentVariable} is not set.");
                yield break;
            }

            byte[] body = Encoding.UTF8.GetBytes(BuildRequestJson(text, resolvedVoiceId, speed, volume, pitch));
            using UnityWebRequest request = new(Endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(body),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = timeoutSeconds
            };
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            request.SetRequestHeader("Content-Type", "application/json");

            yield return request.SendWebRequest();
            if (!IsCurrent(generation, isGenerationCurrent))
            {
                yield break;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                onFailure?.Invoke($"MiniMax TTS request failed ({request.responseCode}): {request.error}");
                yield break;
            }

            if (!TryReadAudioHex(request.downloadHandler.text, out string audioHex, out string responseError))
            {
                onFailure?.Invoke(responseError);
                yield break;
            }

            if (!TryDecodeAudioHex(audioHex, out byte[] wavBytes))
            {
                onFailure?.Invoke("MiniMax TTS returned invalid hexadecimal audio data.");
                yield break;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cachePath) ?? GetRuntimeCacheDirectory());
                File.WriteAllBytes(cachePath, wavBytes);
            }
            catch (Exception exception)
            {
                onFailure?.Invoke("MiniMax TTS cache write failed: " + exception.Message);
                yield break;
            }

            yield return LoadCachedClip(
                cachePath,
                generation,
                isGenerationCurrent,
                onSuccess,
                onFailure,
                true);
        }

        public static string BuildRequestJson(string text)
        {
            return BuildRequestJson(text, FemaleVoiceId, 0.92f, 1f, 0);
        }

        public static string BuildRequestJson(string text, string voiceId)
        {
            return BuildRequestJson(text, NormalizeVoiceId(voiceId), 0.92f, 1f, 0);
        }

        public static string ComputeCacheKey(string text)
        {
            return ComputeCacheKey(text, FemaleVoiceId);
        }

        public static string ComputeCacheKey(string text, string voiceId)
        {
            string source = $"{Model}|{NormalizeVoiceId(voiceId)}|0.92|{text ?? string.Empty}";
            using SHA256 sha256 = SHA256.Create();
            byte[] hash = sha256.ComputeHash(Encoding.UTF8.GetBytes(source));
            StringBuilder builder = new(hash.Length * 2);
            foreach (byte value in hash)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }

        public static string ResolveApiKey()
        {
            string processValue = Environment.GetEnvironmentVariable(ApiKeyEnvironmentVariable);
            string userValue = null;

#if UNITY_EDITOR_WIN || UNITY_STANDALONE_WIN
            try
            {
                userValue = Environment.GetEnvironmentVariable(
                    ApiKeyEnvironmentVariable,
                    EnvironmentVariableTarget.User);
            }
            catch (Exception)
            {
                userValue = null;
            }
#endif

            InternalTestApiCredentials embeddedCredentials =
                InternalTestApiCredentials.Load();
            string embeddedValue = embeddedCredentials != null
                ? embeddedCredentials.MiniMaxApiKey
                : EmbeddedInternalTestApiKey;
            return SelectApiKey(processValue, userValue, embeddedValue);
        }

        public static string SelectApiKey(
            string processValue,
            string userValue,
            string embeddedValue)
        {
            if (!string.IsNullOrWhiteSpace(processValue))
            {
                return processValue.Trim();
            }

            if (!string.IsNullOrWhiteSpace(userValue))
            {
                return userValue.Trim();
            }

            return embeddedValue?.Trim() ?? string.Empty;
        }

        public static bool TryDecodeAudioHex(string audioHex, out byte[] audioBytes)
        {
            audioBytes = null;
            if (string.IsNullOrWhiteSpace(audioHex) || audioHex.Length % 2 != 0)
            {
                return false;
            }

            try
            {
                audioBytes = new byte[audioHex.Length / 2];
                for (int i = 0; i < audioBytes.Length; i++)
                {
                    audioBytes[i] = Convert.ToByte(audioHex.Substring(i * 2, 2), 16);
                }

                return true;
            }
            catch (FormatException)
            {
                audioBytes = null;
                return false;
            }
        }

        private IEnumerator LoadCachedClip(
            string path,
            int generation,
            Func<int, bool> isGenerationCurrent,
            Action<AudioClip> onSuccess,
            Action<string> onFailure,
            bool deleteInvalidFile)
        {
            string uri = new Uri(path).AbsoluteUri;
            using UnityWebRequest request = UnityWebRequestMultimedia.GetAudioClip(uri, AudioType.WAV);
            yield return request.SendWebRequest();

            if (!IsCurrent(generation, isGenerationCurrent))
            {
                yield break;
            }

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (deleteInvalidFile)
                {
                    TryDeleteCacheFile(path);
                }

                onFailure?.Invoke("MiniMax TTS cached WAV could not be loaded: " + request.error);
                yield break;
            }

            AudioClip clip = DownloadHandlerAudioClip.GetContent(request);
            if (clip == null || clip.length <= 0f)
            {
                if (deleteInvalidFile)
                {
                    TryDeleteCacheFile(path);
                }

                onFailure?.Invoke("MiniMax TTS cached WAV was empty.");
                yield break;
            }

            clip.name = "MiniMax_" + Path.GetFileNameWithoutExtension(path);
            onSuccess?.Invoke(clip);
        }

        private string GetCachePath(string text, string voiceId)
        {
            return Path.Combine(GetRuntimeCacheDirectory(), ComputeCacheKey(text, voiceId) + ".wav");
        }

        public static string GetPackagedCachePath(string text, string voiceId)
        {
            return Path.Combine(GetPackagedCacheDirectory(), ComputeCacheKey(text, voiceId) + ".wav");
        }

        public static string GetRuntimeCacheDirectory()
        {
            return Path.Combine(Application.persistentDataPath, CacheDirectoryName);
        }

        public static string GetPackagedCacheDirectory()
        {
            return Path.Combine(Application.streamingAssetsPath, CacheDirectoryName);
        }

        private static bool IsCurrent(int generation, Func<int, bool> isGenerationCurrent)
        {
            return isGenerationCurrent == null || isGenerationCurrent(generation);
        }

        private static void TryDeleteCacheFile(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }

        private static string BuildRequestJson(
            string text,
            string voiceId,
            float speed,
            float volume,
            int pitch)
        {
            TtsRequest payload = new()
            {
                model = Model,
                text = text,
                stream = false,
                language_boost = "English",
                voice_setting = new VoiceSetting
                {
                    voice_id = NormalizeVoiceId(voiceId),
                    speed = speed,
                    vol = volume,
                    pitch = pitch,
                    emotion = "neutral"
                },
                audio_setting = new AudioSetting
                {
                    sample_rate = 32000,
                    format = "wav",
                    channel = 1
                }
            };
            return JsonUtility.ToJson(payload);
        }

        private static string NormalizeVoiceId(string voiceId)
        {
            return string.IsNullOrWhiteSpace(voiceId) ? FemaleVoiceId : voiceId.Trim();
        }

        private static bool TryReadAudioHex(string json, out string audioHex, out string error)
        {
            audioHex = string.Empty;
            error = string.Empty;
            try
            {
                TtsResponse response = JsonUtility.FromJson<TtsResponse>(json);
                if (response == null)
                {
                    error = "MiniMax TTS returned an empty response.";
                    return false;
                }

                if (response.base_resp != null && response.base_resp.status_code != 0)
                {
                    error = $"MiniMax TTS API error {response.base_resp.status_code}: {response.base_resp.status_msg}";
                    return false;
                }

                audioHex = response.data?.audio;
                if (string.IsNullOrWhiteSpace(audioHex))
                {
                    error = "MiniMax TTS response did not contain audio.";
                    return false;
                }

                return true;
            }
            catch (Exception exception)
            {
                error = "MiniMax TTS response parsing failed: " + exception.Message;
                return false;
            }
        }

        [Serializable]
        private sealed class TtsRequest
        {
            public string model;
            public string text;
            public bool stream;
            public string language_boost;
            public VoiceSetting voice_setting;
            public AudioSetting audio_setting;
        }

        [Serializable]
        private sealed class VoiceSetting
        {
            public string voice_id;
            public float speed;
            public float vol;
            public int pitch;
            public string emotion;
        }

        [Serializable]
        private sealed class AudioSetting
        {
            public int sample_rate;
            public string format;
            public int channel;
        }

        [Serializable]
        private sealed class TtsResponse
        {
            public TtsAudioData data;
            public BaseResponse base_resp;
        }

        [Serializable]
        private sealed class TtsAudioData
        {
            public string audio;
        }

        [Serializable]
        private sealed class BaseResponse
        {
            public int status_code;
            public string status_msg;
        }
    }
}
