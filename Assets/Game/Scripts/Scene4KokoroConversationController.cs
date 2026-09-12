using System;
using System.Collections;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Convai.Scripts.Runtime.Core;
using PonyuDev.SherpaOnnx.Tts;
using PonyuDev.SherpaOnnx.Tts.Engine;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Game.Debate
{
    /// <summary>
    /// Routes Scene 04's already-generated DeepSeek coach/challenge text to local Kokoro.
    /// The global F5 scene-menu button can restore Convai at runtime.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class Scene4KokoroConversationController : MonoBehaviour
    {
        public const string SceneName = "04 coach Agent";
        public const int CoachSpeakerId = Scene3DeepSeekConversationController.FemaleSpeakerId;
        public const int OpponentSpeakerId = Scene3DeepSeekConversationController.MaleSpeakerId;

        private readonly List<BehaviourState> _convaiLipSyncStates = new();
        private readonly Dictionary<ConvaiNPC, AudioDrivenNpcLipSync> _audioLipSyncs = new();
        private readonly Dictionary<ConvaiNPC, AudioSource> _audioSources = new();

        private ThreeStageDebatePracticeController _practiceController;
        private ConvaiNPC _coachNpc;
        private ConvaiNPC _opponentNpc;
        private TtsService _ttsService;
        private CancellationTokenSource _lifetimeCancellation;
        private CancellationTokenSource _speechCancellation;
        private Coroutine _speechRoutine;
        private ConvaiNPC _activeNpc;
        private ConvaiNPC _failedNpc;
        private AudioClip _generatedClip;
        private string _lastFailure = string.Empty;
        private bool _useLocalFlow;
        private bool _ttsInitializationCompleted;
        private string _ttsInitializationError = string.Empty;
        private int _speechGeneration;

        public static Scene4KokoroConversationController Instance { get; private set; }
        public bool UsesLocalFlow => _useLocalFlow;

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
            if (!scene.IsValid() || !string.Equals(scene.name, SceneName,
                    StringComparison.OrdinalIgnoreCase) ||
                FindFirstObjectByType<Scene4KokoroConversationController>() != null)
            {
                return;
            }

            GameObject host = new("Scene 4 Kokoro Conversation");
            host.AddComponent<Scene4KokoroConversationController>();
        }

        private void Awake()
        {
            Instance = this;
        }

        private void Start()
        {
            ConversationFlowSettings.DisableConvaiChanged += HandleConversationFlowChanged;
            _lifetimeCancellation = new CancellationTokenSource();
            _practiceController = FindFirstObjectByType<ThreeStageDebatePracticeController>();
            _coachNpc = _practiceController != null ? _practiceController.CoachNPC : null;
            _opponentNpc = _practiceController != null ? _practiceController.OpponentNPC : null;
            ConfigureNpc(_coachNpc);
            ConfigureNpc(_opponentNpc);

            ApplyConversationFlow(ConversationFlowSettings.DisableConvai);
            InitializeTtsAsync();
        }

        private void LateUpdate()
        {
            if (!_useLocalFlow)
            {
                return;
            }

            KeepConvaiInactive(_coachNpc);
            KeepConvaiInactive(_opponentNpc);
        }

        private void OnDestroy()
        {
            ConversationFlowSettings.DisableConvaiChanged -= HandleConversationFlowChanged;
            StopAllSpeech();
            RestoreConvaiLipSyncBehaviours();
            _lifetimeCancellation?.Cancel();
            _lifetimeCancellation?.Dispose();
            _lifetimeCancellation = null;
            _ttsService?.Dispose();
            _ttsService = null;
            if (Instance == this)
            {
                Instance = null;
            }
        }

        public bool TrySpeak(ConvaiNPC npc, string text, int speakerId)
        {
            if (!_useLocalFlow || npc == null || string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            StopAllSpeech();
            ConfigureNpc(npc);
            npc.gameObject.SetActive(true);
            npc.isCharacterActive = false;
            _activeNpc = npc;
            _failedNpc = null;
            _lastFailure = string.Empty;
            int generation = ++_speechGeneration;
            _speechCancellation = CancellationTokenSource.CreateLinkedTokenSource(
                _lifetimeCancellation.Token);
            _speechRoutine = StartCoroutine(GenerateAndPlay(
                npc,
                text.Trim(),
                speakerId,
                generation,
                _speechCancellation.Token));
            return true;
        }

        public bool IsSpeaking(ConvaiNPC npc)
        {
            return npc != null && npc == _activeNpc &&
                   _audioSources.TryGetValue(npc, out AudioSource source) &&
                   source != null && source.isPlaying;
        }

        public bool HasQueuedOrPlaying(ConvaiNPC npc)
        {
            return npc != null && npc == _activeNpc &&
                   (_speechRoutine != null || IsSpeaking(npc));
        }

        public bool TryConsumeFailure(ConvaiNPC npc, out string failure)
        {
            if (npc != null && npc == _failedNpc && !string.IsNullOrWhiteSpace(_lastFailure))
            {
                failure = _lastFailure;
                _failedNpc = null;
                _lastFailure = string.Empty;
                return true;
            }

            failure = string.Empty;
            return false;
        }

        public void StopSpeech(ConvaiNPC npc)
        {
            if (npc != null && npc == _activeNpc)
            {
                StopAllSpeech();
            }
        }

        private IEnumerator GenerateAndPlay(
            ConvaiNPC npc,
            string text,
            int speakerId,
            int generation,
            CancellationToken cancellationToken)
        {
            float initDeadline = Time.realtimeSinceStartup + 90f;
            while (!_ttsInitializationCompleted && Time.realtimeSinceStartup < initDeadline)
            {
                if (!IsCurrentSpeech(npc, generation))
                {
                    yield break;
                }

                yield return null;
            }

            if (!_ttsInitializationCompleted || _ttsService == null || !_ttsService.IsReady)
            {
                CompleteWithFailure(npc, generation,
                    string.IsNullOrWhiteSpace(_ttsInitializationError)
                        ? "the local Kokoro model is not ready"
                        : _ttsInitializationError);
                yield break;
            }

            Task<TtsResult> generationTask;
            try
            {
                generationTask = _ttsService.GenerateAsync(
                    text,
                    1f,
                    speakerId,
                    cancellationToken);
            }
            catch (Exception exception)
            {
                CompleteWithFailure(npc, generation, exception.Message);
                yield break;
            }

            while (!generationTask.IsCompleted)
            {
                if (!IsCurrentSpeech(npc, generation))
                {
                    yield break;
                }

                yield return null;
            }

            if (generationTask.IsCanceled || !IsCurrentSpeech(npc, generation))
            {
                yield break;
            }

            if (generationTask.IsFaulted)
            {
                CompleteWithFailure(
                    npc,
                    generation,
                    generationTask.Exception?.GetBaseException().Message ?? "local synthesis failed");
                yield break;
            }

            using TtsResult result = generationTask.Result;
            if (result == null || !result.IsValid ||
                !_audioSources.TryGetValue(npc, out AudioSource source) || source == null)
            {
                CompleteWithFailure(npc, generation, "Kokoro returned no playable audio.");
                yield break;
            }

            DestroyGeneratedClip();
            _generatedClip = result.ToAudioClip("scene04_kokoro_speech");
            source.Stop();
            source.clip = _generatedClip;
            source.Play();

            while (source != null && source.isPlaying)
            {
                if (!IsCurrentSpeech(npc, generation))
                {
                    yield break;
                }

                yield return null;
            }

            if (source != null)
            {
                source.clip = null;
            }

            DestroyGeneratedClip();
            CompleteSpeech(npc, generation);
        }

        private async void InitializeTtsAsync()
        {
            _ttsService = new TtsService();
            try
            {
                await _ttsService.InitializeAsync(ct: _lifetimeCancellation.Token);
                _ttsInitializationError = _ttsService.IsReady
                    ? string.Empty
                    : "Kokoro profile or sherpa-onnx native library is not installed.";
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception exception)
            {
                _ttsInitializationError = exception.Message;
                Debug.LogError("Scene 04 Kokoro initialization failed: " + exception, this);
            }
            finally
            {
                _ttsInitializationCompleted = true;
            }
        }

        private void HandleConversationFlowChanged(bool disableConvai)
        {
            ApplyConversationFlow(disableConvai);
        }

        private void ApplyConversationFlow(bool disableConvai)
        {
            if (_useLocalFlow != disableConvai)
            {
                StopAllSpeech();
            }

            _useLocalFlow = disableConvai;
            _practiceController?.SetUseLocalConversationFlow(disableConvai);
            SetConvaiLipSyncEnabled(!disableConvai);

            if (disableConvai)
            {
                DisableConvaiNpc(_coachNpc);
                DisableConvaiNpc(_opponentNpc);
            }
            else
            {
                if (_coachNpc != null)
                {
                    _coachNpc.isCharacterActive = true;
                    ConvaiNPCManager.Instance?.SetActiveConvaiNPC(_coachNpc);
                }

                if (_opponentNpc != null && _opponentNpc.gameObject.activeInHierarchy)
                {
                    _opponentNpc.isCharacterActive = true;
                }
            }

        }

        private void ConfigureNpc(ConvaiNPC npc)
        {
            if (npc == null || _audioSources.ContainsKey(npc))
            {
                return;
            }

            AudioSource source = npc.GetComponent<AudioSource>();
            if (source == null)
            {
                source = npc.gameObject.AddComponent<AudioSource>();
            }

            source.playOnAwake = false;
            AudioDrivenNpcLipSync audioLipSync = npc.GetComponent<AudioDrivenNpcLipSync>();
            if (audioLipSync == null)
            {
                audioLipSync = npc.gameObject.AddComponent<AudioDrivenNpcLipSync>();
            }

            audioLipSync.Configure(source);
            _audioSources[npc] = source;
            _audioLipSyncs[npc] = audioLipSync;

            foreach (Behaviour behaviour in npc.GetComponentsInChildren<Behaviour>(true))
            {
                if (behaviour == null || behaviour == audioLipSync)
                {
                    continue;
                }

                Type type = behaviour.GetType();
                if ((type.Namespace ?? string.Empty).StartsWith("Convai.", StringComparison.Ordinal) &&
                    type.Name.IndexOf("LipSync", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    _convaiLipSyncStates.Add(new BehaviourState(behaviour, behaviour.enabled));
                }
            }
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

            foreach (AudioDrivenNpcLipSync lipSync in _audioLipSyncs.Values)
            {
                if (lipSync != null)
                {
                    lipSync.enabled = !enabled;
                }
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

        private static void DisableConvaiNpc(ConvaiNPC npc)
        {
            if (npc == null)
            {
                return;
            }

            if (npc.IsCharacterTalking)
            {
                npc.InterruptCharacterSpeech();
            }

            npc.isCharacterActive = false;
        }

        private static void KeepConvaiInactive(ConvaiNPC npc)
        {
            if (npc != null && npc.gameObject.activeInHierarchy && npc.isCharacterActive)
            {
                npc.isCharacterActive = false;
            }
        }

        private bool IsCurrentSpeech(ConvaiNPC npc, int generation)
        {
            return _useLocalFlow && npc != null && npc == _activeNpc &&
                   generation == _speechGeneration;
        }

        private void CompleteSpeech(ConvaiNPC npc, int generation)
        {
            if (npc != _activeNpc || generation != _speechGeneration)
            {
                return;
            }

            _speechCancellation?.Dispose();
            _speechCancellation = null;
            _speechRoutine = null;
            _activeNpc = null;
        }

        private void CompleteWithFailure(ConvaiNPC npc, int generation, string failure)
        {
            if (npc != _activeNpc || generation != _speechGeneration)
            {
                return;
            }

            if (_audioSources.TryGetValue(npc, out AudioSource source) && source != null)
            {
                source.Stop();
                source.clip = null;
            }

            DestroyGeneratedClip();
            _speechCancellation?.Dispose();
            _speechCancellation = null;
            _speechRoutine = null;
            _activeNpc = null;
            _failedNpc = npc;
            _lastFailure = failure?.Trim() ?? "local synthesis failed";
        }

        private void StopAllSpeech()
        {
            _speechGeneration++;
            _speechCancellation?.Cancel();
            _speechCancellation?.Dispose();
            _speechCancellation = null;
            if (_speechRoutine != null)
            {
                StopCoroutine(_speechRoutine);
                _speechRoutine = null;
            }

            foreach (AudioSource source in _audioSources.Values)
            {
                if (source == null)
                {
                    continue;
                }

                source.Stop();
                source.clip = null;
            }

            DestroyGeneratedClip();
            _activeNpc = null;
            _failedNpc = null;
            _lastFailure = string.Empty;
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
