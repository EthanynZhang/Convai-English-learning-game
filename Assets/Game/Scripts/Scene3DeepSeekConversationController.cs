using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Convai.Scripts.Runtime.Core;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PonyuDev.SherpaOnnx.Tts;
using PonyuDev.SherpaOnnx.Tts.Engine;
using TMPro;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.SceneManagement;

namespace Game.Debate
{
    /// <summary>
    /// Scene 03 and Scene 05's optional replacement for Convai:
    /// iFlytek transcript -> DeepSeek dialogue -> local Kokoro speech.
    /// The component bootstraps itself so the third-party Convai prefab remains untouched.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Scene3DeepSeekConversationController : MonoBehaviour
    {
        public const string SceneName = "03Level_PlayerVsNPCDebate";
        public const string TransferSceneName = "05Level_PlayerVsNPCDebate 1";
        public const string DisableConvaiPlayerPrefsKey = ConversationFlowSettings.PlayerPrefsKey;
        public const string Model = "deepseek-flash";
        public const int FemaleSpeakerId = 0;
        public const int MaleSpeakerId = 5;

        public const string DefaultOpponentPosition =
            "Interaction with other people is more beneficial than individual practice for developing English speaking skills.";
        public const string TransferOpponentPosition =
            "Classroom instruction is more beneficial than real-life context for English speaking learning.";

        private const int RequestTimeoutSeconds = 35;
        private const int MaximumHistoryMessages = 10;

        private readonly List<DialogueMessage> _history = new();
        private readonly List<BehaviourState> _convaiLipSyncStates = new();

        private DebateRoundManager _roundManager;
        private PlayerOralPracticeController _oralPractice;
        private ConvaiNPC _opponentNpc;
        private AudioSource _opponentAudioSource;
        private AudioDrivenNpcLipSync _audioDrivenLipSync;
        private UnityWebRequest _activeRequest;
        private Coroutine _replyRoutine;
        private CancellationTokenSource _lifetimeCancellation;
        private TtsService _ttsService;
        private AudioClip _generatedClip;
        private int _flowGeneration;
        private int _speakerId = FemaleSpeakerId;
        private bool _useLocalFlow;
        private bool _ttsInitializationCompleted;
        private string _ttsInitializationError = string.Empty;

        public bool UsesLocalFlow => _useLocalFlow;
        public int SpeakerId => _speakerId;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void RegisterSceneLoadHandler()
        {
            SceneManager.sceneLoaded -= HandleSceneLoaded;
            SceneManager.sceneLoaded += HandleSceneLoaded;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            EnsureControllerForScene(SceneManager.GetActiveScene());
        }

        private static void HandleSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            EnsureControllerForScene(scene);
        }

        private static void EnsureControllerForScene(Scene scene)
        {
            if (!scene.IsValid() || !IsSupportedScene(scene.name) ||
                FindFirstObjectByType<Scene3DeepSeekConversationController>() != null)
            {
                return;
            }

            GameObject host = new("Player vs NPC DeepSeek + Kokoro Conversation");
            host.AddComponent<Scene3DeepSeekConversationController>();
        }

        private void Start()
        {
            ConversationFlowSettings.DisableConvaiChanged += HandleConversationFlowChanged;
            _lifetimeCancellation = new CancellationTokenSource();
            ResolveSceneReferences();
            CacheConvaiLipSyncBehaviours();
            _speakerId = ResolveKokoroSpeakerId(GetOpponentName());

            if (_oralPractice != null)
            {
                _oralPractice.TranscriptConfirmed += HandlePlayerTranscriptConfirmed;
            }

            if (_roundManager != null)
            {
                _roundManager.LocalOpponentOpeningHandler = TryStartLocalOpponentOpening;
                _roundManager.CancelLocalOpponentOpeningHandler = CancelLocalReply;
            }

            ApplyConversationFlow(ConversationFlowSettings.DisableConvai);
            InitializeTtsAsync();
        }

        private void LateUpdate()
        {
            // Convai initializes some prefab state after this controller's Start().
            // Keep its network conversation disabled for as long as the local flow is selected.
            if (_useLocalFlow && _opponentNpc != null && _opponentNpc.isCharacterActive)
            {
                _opponentNpc.isCharacterActive = false;
            }
        }

        private void OnDestroy()
        {
            ConversationFlowSettings.DisableConvaiChanged -= HandleConversationFlowChanged;
            if (_oralPractice != null)
            {
                _oralPractice.TranscriptConfirmed -= HandlePlayerTranscriptConfirmed;
                _oralPractice.SetConversationInputBlocked(false);
            }

            if (_roundManager != null)
            {
                if (_roundManager.LocalOpponentOpeningHandler == TryStartLocalOpponentOpening)
                {
                    _roundManager.LocalOpponentOpeningHandler = null;
                }

                if (_roundManager.CancelLocalOpponentOpeningHandler == CancelLocalReply)
                {
                    _roundManager.CancelLocalOpponentOpeningHandler = null;
                }
            }

            CancelLocalReply();
            RestoreConvaiLipSyncBehaviours();
            _lifetimeCancellation?.Cancel();
            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = null;
            _ttsService?.Dispose();
            _ttsService = null;
        }

        public static bool IsScene3(string sceneName)
        {
            return string.Equals(sceneName?.Trim(), SceneName, StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsSupportedScene(string sceneName)
        {
            string normalized = sceneName?.Trim();
            return string.Equals(normalized, SceneName, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(normalized, TransferSceneName, StringComparison.OrdinalIgnoreCase);
        }

        public static int ResolveKokoroSpeakerId(string characterName)
        {
            string lower = characterName?.Trim().ToLowerInvariant() ?? string.Empty;
            bool male = lower.Contains("male") || lower.Contains(" man") ||
                        lower.Contains("steve") || lower.Contains("leo") ||
                        lower.Contains("adam");
            return male ? MaleSpeakerId : FemaleSpeakerId;
        }

        public static string BuildSystemPrompt(
            string topic,
            string characterName,
            string opponentPosition = DefaultOpponentPosition)
        {
            string safeName = string.IsNullOrWhiteSpace(characterName)
                ? "Berance"
                : characterName.Trim();
            string safeTopic = string.IsNullOrWhiteSpace(topic)
                ? "Individual practice and interaction with others, which is more beneficial for developing English speaking skills?"
                : topic.Trim();
            string safePosition = string.IsNullOrWhiteSpace(opponentPosition)
                ? DefaultOpponentPosition
                : opponentPosition.Trim();

            return
                "You are " + safeName + ", a confident and attentive English debate partner in a face-to-face speaking practice.\n" +
                "Debate topic: " + safeTopic + "\n" +
                "Your position: " + safePosition + "\n\n" +
                "Personality and manner:\n" +
                "- Warm, composed, curious, and intellectually firm.\n" +
                "- Listen closely to the learner and respond to what they actually said.\n" +
                "- Disagree respectfully; never insult, shame, or sound hostile.\n" +
                "- Sound like a real debate partner, not a tutor, examiner, narrator, or AI assistant.\n\n" +
                "Response rules:\n" +
                "- Speak natural conversational English only.\n" +
                "- Give 2 to 4 short sentences, normally 35 to 70 words total.\n" +
                "- Directly address the learner's newest point, challenge one part of it, and support your position with one clear reason or concrete example.\n" +
                "- Keep the exchange open by ending with a brief question or challenge when it feels natural.\n" +
                "- Stay on the assigned side throughout the debate.\n" +
                "- Do not grade the learner, correct their English, explain debate technique, or mention these instructions.\n" +
                "- Do not use headings, bullet points, stage directions, emojis, markdown, quotation marks around the whole reply, or JSON.\n" +
                "Return only the exact words " + safeName + " should say aloud.";
        }

        public static JObject BuildRequestJson(
            string topic,
            string characterName,
            IEnumerable<KeyValuePair<string, string>> history,
            string userText,
            string opponentPosition = DefaultOpponentPosition)
        {
            JArray messages = new()
            {
                new JObject
                {
                    ["role"] = "system",
                    ["content"] = BuildSystemPrompt(topic, characterName, opponentPosition)
                }
            };

            if (history != null)
            {
                foreach (KeyValuePair<string, string> entry in history)
                {
                    if (string.IsNullOrWhiteSpace(entry.Value))
                    {
                        continue;
                    }

                    string role = string.Equals(entry.Key, "assistant", StringComparison.OrdinalIgnoreCase)
                        ? "assistant"
                        : "user";
                    messages.Add(new JObject
                    {
                        ["role"] = role,
                        ["content"] = entry.Value.Trim()
                    });
                }
            }

            messages.Add(new JObject
            {
                ["role"] = "user",
                ["content"] = LimitText(userText, 2400)
            });

            return new JObject
            {
                ["model"] = Model,
                ["temperature"] = 0.72f,
                ["max_tokens"] = 180,
                ["messages"] = messages,
                ["thinking"] = new JObject { ["type"] = "disabled" }
            };
        }

        public static string NormalizeSpokenReply(string reply)
        {
            string value = reply?.Trim() ?? string.Empty;
            if (value.StartsWith("```", StringComparison.Ordinal))
            {
                value = value.Trim('`').Trim();
            }

            if (value.Length >= 2 &&
                ((value[0] == '"' && value[^1] == '"') ||
                 (value[0] == '\u201c' && value[^1] == '\u201d')))
            {
                value = value.Substring(1, value.Length - 2).Trim();
            }

            return LimitText(value, 900);
        }

        private void ResolveSceneReferences()
        {
            _roundManager = FindFirstObjectByType<DebateRoundManager>();
            _oralPractice = FindFirstObjectByType<PlayerOralPracticeController>();
            _opponentNpc = _roundManager != null ? _roundManager.OpponentNPC : null;
            if (_opponentNpc == null)
            {
                _opponentNpc = FindObjectsByType<ConvaiNPC>(
                        FindObjectsInactive.Include,
                        FindObjectsSortMode.None)
                    .FirstOrDefault(npc => npc != null &&
                                           npc.gameObject.scene == gameObject.scene);
            }

            if (_opponentNpc == null)
            {
                return;
            }

            _opponentAudioSource = _opponentNpc.GetComponent<AudioSource>();
            if (_opponentAudioSource == null)
            {
                _opponentAudioSource = _opponentNpc.gameObject.AddComponent<AudioSource>();
            }

            _opponentAudioSource.playOnAwake = false;
            _audioDrivenLipSync = _opponentNpc.GetComponent<AudioDrivenNpcLipSync>();
            if (_audioDrivenLipSync == null)
            {
                _audioDrivenLipSync = _opponentNpc.gameObject.AddComponent<AudioDrivenNpcLipSync>();
            }

            _audioDrivenLipSync.Configure(_opponentAudioSource);
        }

        private void CacheConvaiLipSyncBehaviours()
        {
            _convaiLipSyncStates.Clear();
            if (_opponentNpc == null)
            {
                return;
            }

            foreach (Behaviour behaviour in _opponentNpc.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == null || behaviour == _audioDrivenLipSync)
                {
                    continue;
                }

                Type type = behaviour.GetType();
                string typeName = type.Name;
                string typeNamespace = type.Namespace ?? string.Empty;
                if (typeNamespace.StartsWith("Convai.", StringComparison.Ordinal) &&
                    typeName.IndexOf("LipSync", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _convaiLipSyncStates.Add(new BehaviourState(behaviour, behaviour.enabled));
                }
            }
        }

        private void HandleConversationFlowChanged(bool disableConvai)
        {
            ApplyConversationFlow(disableConvai);
        }

        private void ApplyConversationFlow(bool disableConvai)
        {
            _useLocalFlow = disableConvai;
            _roundManager?.SetUseLocalConversationFlow(disableConvai);
            _oralPractice?.SetUseLocalOpponentFlow(disableConvai);
            SetConvaiLipSyncEnabled(!disableConvai);

            if (disableConvai)
            {
                if (_opponentNpc != null)
                {
                    if (_opponentNpc.IsCharacterTalking)
                    {
                        _opponentNpc.InterruptCharacterSpeech();
                    }

                    _opponentNpc.isCharacterActive = false;
                }
            }
            else
            {
                CancelLocalReply();
                _oralPractice?.ShowOpponentReply(string.Empty, string.Empty);
                if (_opponentNpc != null)
                {
                    _opponentNpc.isCharacterActive = true;
                    ConvaiNPCManager.Instance?.SetActiveConvaiNPC(_opponentNpc);
                }
            }

        }

        private void HandlePlayerTranscriptConfirmed(string transcript)
        {
            if (!_useLocalFlow || string.IsNullOrWhiteSpace(transcript))
            {
                return;
            }

            CancelLocalReply();
            int generation = ++_flowGeneration;
            _oralPractice?.SetConversationInputBlocked(true);
            _oralPractice?.ShowConversationStatus(GetOpponentName() + " is thinking...");
            _replyRoutine = StartCoroutine(GenerateAndSpeakReply(transcript.Trim(), generation));
        }

        private bool TryStartLocalOpponentOpening(string openingInstruction, Action<bool> onComplete)
        {
            if (!_useLocalFlow || string.IsNullOrWhiteSpace(openingInstruction))
            {
                return false;
            }

            CancelLocalReply();
            int generation = ++_flowGeneration;
            _oralPractice?.SetConversationInputBlocked(true);
            _oralPractice?.ShowConversationStatus(GetOpponentName() + " is preparing an opening argument...");
            _replyRoutine = StartCoroutine(GenerateAndSpeakOpening(
                openingInstruction.Trim(),
                generation,
                onComplete));
            return true;
        }

        private IEnumerator GenerateAndSpeakOpening(
            string openingInstruction,
            int generation,
            Action<bool> onComplete)
        {
            string reply = string.Empty;
            string failure = string.Empty;
            yield return RequestDeepSeekReply(
                openingInstruction,
                value => reply = value,
                error => failure = error);

            if (!IsCurrentLocalGeneration(generation))
            {
                onComplete?.Invoke(false);
                yield break;
            }

            if (string.IsNullOrWhiteSpace(reply))
            {
                _oralPractice?.SetConversationInputBlocked(false);
                _oralPractice?.ShowConversationStatus(
                    "DeepSeek could not create the opening argument: " + failure);
                _replyRoutine = null;
                onComplete?.Invoke(false);
                yield break;
            }

            AddHistory("assistant", reply);
            _oralPractice?.ShowOpponentReply(GetOpponentName(), reply);
            _oralPractice?.ShowConversationStatus(
                "Generating local Kokoro voice (speaker" + _speakerId + ")...");
            yield return SpeakWithKokoro(reply, generation, error => failure = error);

            if (!IsCurrentLocalGeneration(generation))
            {
                onComplete?.Invoke(false);
                yield break;
            }

            bool spoken = string.IsNullOrWhiteSpace(failure);
            _oralPractice?.SetConversationInputBlocked(false);
            _oralPractice?.ShowConversationStatus(spoken
                ? "Your turn. Press T to respond."
                : "The local opening voice failed: " + failure + " Press T to continue.");
            _replyRoutine = null;
            onComplete?.Invoke(spoken);
        }

        private IEnumerator GenerateAndSpeakReply(string playerText, int generation)
        {
            string reply = string.Empty;
            string failure = string.Empty;
            yield return RequestDeepSeekReply(
                playerText,
                value => reply = value,
                error => failure = error);

            if (!IsCurrentLocalGeneration(generation))
            {
                yield break;
            }

            if (string.IsNullOrWhiteSpace(reply))
            {
                _oralPractice?.SetConversationInputBlocked(false);
                _oralPractice?.ShowConversationStatus(
                    "DeepSeek could not create the opponent reply: " + failure + " Press T to try again.");
                _replyRoutine = null;
                yield break;
            }

            AddHistory("user", playerText);
            AddHistory("assistant", reply);
            _oralPractice?.ShowOpponentReply(GetOpponentName(), reply);
            _oralPractice?.ShowConversationStatus(
                "Generating local Kokoro voice (speaker" + _speakerId + ")...");

            yield return SpeakWithKokoro(reply, generation, error => failure = error);
            if (!IsCurrentLocalGeneration(generation))
            {
                yield break;
            }

            _oralPractice?.SetConversationInputBlocked(false);
            _oralPractice?.ShowConversationStatus(string.IsNullOrWhiteSpace(failure)
                ? "Your turn. Press T to respond."
                : "Kokoro could not play the reply: " + failure + " Press T to continue.");
            _replyRoutine = null;
        }

        private IEnumerator RequestDeepSeekReply(
            string playerText,
            Action<string> onSuccess,
            Action<string> onFailure)
        {
            string apiKey = DebateCommandParser.ResolveApiKey();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                onFailure?.Invoke("DeepSeek API key is missing.");
                yield break;
            }

            string endpoint = DebateCommandParser.ResolveChatCompletionsEndpoint(
                DebateCommandParser.ResolveBaseUrl());
            IEnumerable<KeyValuePair<string, string>> history = _history.Select(message =>
                new KeyValuePair<string, string>(message.Role, message.Content));
            JObject body = BuildRequestJson(
                _roundManager?.DebateTopic,
                GetOpponentName(),
                history,
                playerText,
                GetOpponentPosition());
            byte[] bytes = Encoding.UTF8.GetBytes(body.ToString(Formatting.None));

            using UnityWebRequest request = new(endpoint, UnityWebRequest.kHttpVerbPOST)
            {
                uploadHandler = new UploadHandlerRaw(bytes),
                downloadHandler = new DownloadHandlerBuffer(),
                timeout = RequestTimeoutSeconds
            };
            request.SetRequestHeader("Content-Type", "application/json");
            request.SetRequestHeader("Authorization", "Bearer " + apiKey);
            _activeRequest = request;
            yield return request.SendWebRequest();
            _activeRequest = null;

            if (request.result != UnityWebRequest.Result.Success)
            {
                string response = request.downloadHandler?.text?.Trim();
                onFailure?.Invoke(
                    "HTTP " + request.responseCode + " " + request.error +
                    (string.IsNullOrWhiteSpace(response) ? string.Empty : " - " + LimitText(response, 280)));
                yield break;
            }

            try
            {
                JObject envelope = JObject.Parse(request.downloadHandler.text);
                string content = (string)envelope["choices"]?[0]?["message"]?["content"];
                string normalized = NormalizeSpokenReply(content);
                if (string.IsNullOrWhiteSpace(normalized))
                {
                    onFailure?.Invoke("DeepSeek returned an empty reply.");
                    yield break;
                }

                onSuccess?.Invoke(normalized);
            }
            catch (Exception exception)
            {
                onFailure?.Invoke("DeepSeek response format was invalid: " + exception.Message);
            }
        }

        private IEnumerator SpeakWithKokoro(
            string reply,
            int generation,
            Action<string> onFailure)
        {
            float initDeadline = Time.realtimeSinceStartup + 90f;
            while (!_ttsInitializationCompleted && Time.realtimeSinceStartup < initDeadline)
            {
                if (!IsCurrentLocalGeneration(generation))
                {
                    yield break;
                }

                yield return null;
            }

            if (!_ttsInitializationCompleted || _ttsService == null || !_ttsService.IsReady)
            {
                onFailure?.Invoke(string.IsNullOrWhiteSpace(_ttsInitializationError)
                    ? "the local Kokoro model is not ready"
                    : _ttsInitializationError);
                yield break;
            }

            Task<TtsResult> generationTask;
            try
            {
                generationTask = _ttsService.GenerateAsync(
                    reply,
                    1f,
                    _speakerId,
                    _lifetimeCancellation.Token);
            }
            catch (Exception exception)
            {
                onFailure?.Invoke(exception.Message);
                yield break;
            }

            while (!generationTask.IsCompleted)
            {
                if (!IsCurrentLocalGeneration(generation))
                {
                    yield break;
                }

                yield return null;
            }

            if (generationTask.IsCanceled)
            {
                yield break;
            }

            if (generationTask.IsFaulted)
            {
                onFailure?.Invoke(generationTask.Exception?.GetBaseException().Message ??
                                  "local synthesis failed");
                yield break;
            }

            using TtsResult result = generationTask.Result;
            if (result == null || !result.IsValid || _opponentAudioSource == null)
            {
                onFailure?.Invoke("Kokoro returned no playable audio.");
                yield break;
            }

            DestroyGeneratedClip();
            _generatedClip = result.ToAudioClip("scene03_kokoro_reply");
            _opponentAudioSource.Stop();
            _opponentAudioSource.clip = _generatedClip;
            _opponentAudioSource.Play();
            _oralPractice?.ShowConversationStatus(GetOpponentName() + " is speaking...");

            while (_opponentAudioSource != null && _opponentAudioSource.isPlaying)
            {
                if (!IsCurrentLocalGeneration(generation))
                {
                    yield break;
                }

                yield return null;
            }

            if (_opponentAudioSource != null)
            {
                _opponentAudioSource.clip = null;
            }

            DestroyGeneratedClip();
        }

        private async void InitializeTtsAsync()
        {
            _ttsService = new TtsService();
            try
            {
                await _ttsService.InitializeAsync(ct: _lifetimeCancellation.Token);
                if (_ttsService.IsReady)
                {
                    _ttsInitializationError = string.Empty;
                }
                else
                {
                    _ttsInitializationError =
                        "Kokoro profile or sherpa-onnx native library is not installed.";
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _ttsInitializationError = exception.Message;
                Debug.LogError("Scene 03 Kokoro initialization failed: " + exception, this);
            }
            finally
            {
                _ttsInitializationCompleted = true;
            }
        }

        private void CancelLocalReply()
        {
            _flowGeneration++;
            _activeRequest?.Abort();
            _activeRequest?.Dispose();
            _activeRequest = null;
            if (_replyRoutine != null)
            {
                StopCoroutine(_replyRoutine);
                _replyRoutine = null;
            }

            if (_opponentAudioSource != null)
            {
                _opponentAudioSource.Stop();
                _opponentAudioSource.clip = null;
            }

            DestroyGeneratedClip();
            _oralPractice?.SetConversationInputBlocked(false);
        }

        private void DestroyGeneratedClip()
        {
            if (_generatedClip == null)
            {
                return;
            }

            Destroy(_generatedClip);
            _generatedClip = null;
        }

        private void AddHistory(string role, string content)
        {
            _history.Add(new DialogueMessage(role, content));
            while (_history.Count > MaximumHistoryMessages)
            {
                _history.RemoveAt(0);
            }
        }

        private bool IsCurrentLocalGeneration(int generation)
        {
            return _useLocalFlow && generation == _flowGeneration;
        }

        private string GetOpponentName()
        {
            string characterName = _opponentNpc != null ? _opponentNpc.characterName : string.Empty;
            if (string.IsNullOrWhiteSpace(characterName))
            {
                return "Berance";
            }

            string trimmed = characterName.Trim();
            return char.ToUpperInvariant(trimmed[0]) + trimmed.Substring(1);
        }

        private static string GetOpponentPosition()
        {
            return string.Equals(
                SceneManager.GetActiveScene().name,
                TransferSceneName,
                StringComparison.OrdinalIgnoreCase)
                ? TransferOpponentPosition
                : DefaultOpponentPosition;
        }

        private void SetConvaiLipSyncEnabled(bool enabled)
        {
            foreach (BehaviourState state in _convaiLipSyncStates)
            {
                if (state.Behaviour != null)
                {
                    state.Behaviour.enabled = enabled && state.WasEnabled;
                }
            }

            if (_audioDrivenLipSync != null)
            {
                _audioDrivenLipSync.enabled = !enabled;
            }
        }

        private void RestoreConvaiLipSyncBehaviours()
        {
            foreach (BehaviourState state in _convaiLipSyncStates)
            {
                if (state.Behaviour != null)
                {
                    state.Behaviour.enabled = state.WasEnabled;
                }
            }
        }

        private static string LimitText(string text, int maximumCharacters)
        {
            string value = text?.Trim() ?? string.Empty;
            if (value.Length <= maximumCharacters)
            {
                return value;
            }

            return value.Substring(0, maximumCharacters).TrimEnd();
        }

        private readonly struct DialogueMessage
        {
            public DialogueMessage(string role, string content)
            {
                Role = role;
                Content = content;
            }

            public string Role { get; }
            public string Content { get; }
        }

        private readonly struct BehaviourState
        {
            public BehaviourState(Behaviour behaviour, bool wasEnabled)
            {
                Behaviour = behaviour;
                WasEnabled = wasEnabled;
            }

            public Behaviour Behaviour { get; }
            public bool WasEnabled { get; }
        }
    }
}
