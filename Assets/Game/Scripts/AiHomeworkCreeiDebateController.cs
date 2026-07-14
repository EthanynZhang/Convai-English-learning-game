using System;
using System.Collections;
using System.Diagnostics;
using System.Text;
using Convai.Scripts.Runtime.Core;
using Convai.Scripts.Runtime.Features;
using Convai.Scripts.Runtime.UI;
using Convai.Scripts.Runtime.Utils;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace Game.Debate
{
    public enum AiHomeworkDebatePhase
    {
        Intro,
        PlayerReady,
        PlayerSpeaking,
        PlayerAwaitingTranscript,
        OpponentGenerating,
        OpponentSpeaking,
        Complete
    }

    public sealed class AiHomeworkCreeiDebateController : MonoBehaviour
    {
        [Header("Agent")]
        [SerializeField] private ConvaiNPC leoNPC;

        [Header("Debate")]
        [SerializeField] private string topic = "Should students be allowed to use AI for homework?";
        [SerializeField] private string playerStance = "Students should be allowed to use AI for homework.";
        [SerializeField] private string leoStance = "Students should not be allowed to use AI for homework.";
        [SerializeField, Min(30f)] private float speechTargetSeconds = 180f;

        [Header("UI")]
        [SerializeField] private Canvas uiCanvas;

        [Header("Realtime Transcription")]
        [SerializeField] private XfyunRealtimeTranscriber realtimeTranscriber;

        public AiHomeworkDebatePhase Phase { get; private set; } = AiHomeworkDebatePhase.Intro;

        private Coroutine _transcriptFallbackRoutine;
        private Coroutine _leoInitializationRoutine;
        private Coroutine _leoSpeechStartGuardRoutine;
        private Coroutine _leoSpeechRetryRoutine;
        private Coroutine _localLeoTtsRoutine;
        private GameObject _uiRoot;
        private TMP_Text _phaseText;
        private TMP_Text _statusText;
        private TMP_Text _playerText;
        private TMP_Text _leoText;
        private ScrollRect _playerTranscriptScrollRect;
        private ScrollRect _leoTranscriptScrollRect;
        private TMP_Text _timerText;
        private Button _startButton;
        private bool _playerRecording;
        private bool _playerTranscriptionConnecting;
        private bool _playerStopRequested;
        private float _playerStartedAt;
        private float _playerDuration;
        private bool _leoTimerRunning;
        private float _leoStartedAt;
        private float _leoDuration;
        private string[] _leoSpeechSegments = Array.Empty<string>();
        private int _leoSegmentIndex;
        private int _leoSegmentSendAttempts;
        private bool _leoAudioObservedForCurrentSegment;
        private bool _usingLocalLeoTtsForCurrentSegment;
        private bool _forceLocalLeoTts;
        private string _pendingLeoPrompt = string.Empty;
        private string _pendingLeoSpeechText = string.Empty;
        private Process _localLeoTtsProcess;
        private string _lastPlayerTranscriptUiText = string.Empty;
        private string _lastLeoTranscriptUiText = string.Empty;
        private readonly StringBuilder _leoTranscript = new();
        private readonly StringBuilder _completePlayerTranscript = new();
        private bool _isUiMode;
        private const float LeoInitializationTimeoutSeconds = 12f;
        private const float LeoAudioStartTimeoutSeconds = 18f;
        private const int MaxLeoSegmentSendAttempts = 2;
        private static readonly string[] PresetLeoCreeiSegments =
        {
            "Students should not be allowed to use AI to complete homework for them. Homework is meant to show what students understand and what they can produce independently. When an AI system supplies the ideas, structure, or final wording, teachers can no longer tell whether the submitted work represents genuine learning. AI may be useful for study support, but it should not replace the student's own work on an assessed homework task.",
            "The main reason is that independent practice develops the habits students need for future learning. Working through a difficult question forces learners to recall knowledge, test possible answers, notice mistakes, and revise their thinking. Those steps can feel slow, but they build judgment and persistence. If AI immediately produces a polished response, students may finish the assignment faster while missing the mental practice that the homework was specifically designed to provide.",
            "Consider a student who submits a clear, sophisticated paragraph written with extensive AI assistance. The next day, the teacher asks the student to explain the same argument in class, but the student cannot define the key terms or defend the reasoning. The homework looks excellent, yet the student's performance shows that the underlying idea was never fully understood. This realistic mismatch gives teachers misleading evidence about the student's actual progress.",
            "This example supports the argument because homework is valuable only when the completed work reflects the learner's developing ability. A polished AI-generated answer may create the appearance of success, but appearance is not the same as learning. When teachers receive inaccurate evidence, they cannot identify gaps or give appropriate support. Therefore, unrestricted AI use weakens the connection between practice, feedback, and improvement that makes homework educationally useful.",
            "The impact reaches students, teachers, families, and the wider school community. Students may become dependent on tools and feel less confident during exams, presentations, or real tasks where AI is unavailable. Teachers may assign grades that do not reflect actual ability, and families may receive an inaccurate picture of progress. Clear limits on AI use protect fairness, honest feedback, strong learning habits, and the long-term independence students need in education and work."
        };

        private void Awake()
        {
            if (realtimeTranscriber == null)
            {
                realtimeTranscriber = GetComponent<XfyunRealtimeTranscriber>();
            }

            if (realtimeTranscriber == null)
            {
                realtimeTranscriber = gameObject.AddComponent<XfyunRealtimeTranscriber>();
            }

            BuildUi();
            HideLegacyUi();
        }

        private void OnEnable()
        {
            RegisterInput();
            SubscribeVoice();
            SubscribeLeoAudio();
            SubscribeRealtimeTranscriber();
        }

        private void Start()
        {
            RegisterInput();
            SubscribeVoice();
            SubscribeLeoAudio();
            SubscribeRealtimeTranscriber();
            EnterIntro();
        }

        private void Update()
        {
            realtimeTranscriber?.Tick();

            if (Input.GetMouseButtonDown(1))
            {
                SetCursorMode(!_isUiMode);
            }

            if (_playerRecording)
            {
                _playerDuration = Mathf.Min(
                    speechTargetSeconds,
                    Mathf.Max(0f, Time.realtimeSinceStartup - _playerStartedAt));
                UpdateTimer("Your Speech", _playerDuration);
                if (_playerDuration >= speechTargetSeconds)
                {
                    StopPlayerRecordingAtLimit();
                }
            }

            if (_leoTimerRunning)
            {
                _leoDuration = Mathf.Min(
                    speechTargetSeconds,
                    Mathf.Max(0f, Time.realtimeSinceStartup - _leoStartedAt));
                UpdateTimer("Leo's Speech", _leoDuration);
                if (_leoDuration >= speechTargetSeconds)
                {
                    _leoTimerRunning = false;
                    leoNPC?.InterruptCharacterSpeech();
                    StartPlayerTurn("Leo reached the three-minute limit. Your turn is ready.");
                }
            }
        }

        private void LateUpdate()
        {
            Cursor.lockState = _isUiMode ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = _isUiMode;
            RefreshTranscriptScroll(
                _playerText,
                _playerTranscriptScrollRect,
                ref _lastPlayerTranscriptUiText);
            RefreshTranscriptScroll(
                _leoText,
                _leoTranscriptScrollRect,
                ref _lastLeoTranscriptUiText);
        }

        private void OnDisable()
        {
            UnregisterInput();
            UnsubscribeVoice();
            UnsubscribeLeoAudio();
            UnsubscribeRealtimeTranscriber();
            realtimeTranscriber?.CancelSession();
        }

        private void OnDestroy()
        {
            if (_transcriptFallbackRoutine != null)
            {
                StopCoroutine(_transcriptFallbackRoutine);
            }

            StopLeoSpeechRoutines();
        }

        public void BeginDebate()
        {
            if (Phase != AiHomeworkDebatePhase.Intro && Phase != AiHomeworkDebatePhase.Complete)
            {
                return;
            }

            _playerDuration = 0f;
            _leoDuration = 0f;
            _playerRecording = false;
            _playerTranscriptionConnecting = false;
            _playerStopRequested = false;
            _leoTimerRunning = false;
            StopLeoSpeechRoutines();
            _leoTranscript.Clear();
            _leoSpeechSegments = Array.Empty<string>();
            _leoSegmentIndex = 0;
            _leoSegmentSendAttempts = 0;
            _leoAudioObservedForCurrentSegment = false;
            _usingLocalLeoTtsForCurrentSegment = false;
            _forceLocalLeoTts = false;
            _pendingLeoPrompt = string.Empty;
            _pendingLeoSpeechText = string.Empty;
            _completePlayerTranscript.Clear();
            realtimeTranscriber?.CancelSession();
            _startButton.gameObject.SetActive(false);
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(leoNPC);
            SetCursorMode(false);
            _leoInitializationRoutine = StartCoroutine(StartLeoPresetSpeechWhenReady());
        }

        private IEnumerator StartLeoPresetSpeechWhenReady()
        {
            Phase = AiHomeworkDebatePhase.OpponentGenerating;
            _phaseText.text = "Preparing Leo";
            _statusText.text = "Connecting Leo to Convai audio...";
            UpdateTimer("Leo's Speech", 0f);

            float deadline = Time.realtimeSinceStartup + LeoInitializationTimeoutSeconds;
            while (!IsLeoSpeechPipelineReady() && Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForSecondsRealtime(0.25f);
            }

            if (!IsLeoSpeechPipelineReady())
            {
                _leoInitializationRoutine = null;
                FailLeoSpeech("Leo's Convai audio pipeline did not initialize in time.");
                yield break;
            }

            SubscribeLeoAudio();
            ConvaiNPCManager.Instance.SetActiveConvaiNPC(leoNPC);
            yield return new WaitForSecondsRealtime(0.75f);
            _leoInitializationRoutine = null;
            StartLeoPresetSpeech();
        }

        private bool IsLeoSpeechPipelineReady()
        {
            return leoNPC != null &&
                   leoNPC.AudioManager != null &&
                   ConvaiGRPCAPI.Instance != null &&
                   ConvaiNPCManager.Instance != null;
        }

        private bool ShouldUseTapToTalk()
        {
            return Phase == AiHomeworkDebatePhase.PlayerReady ||
                   Phase == AiHomeworkDebatePhase.PlayerSpeaking;
        }

        private void HandleTapToTalk()
        {
            RunOnMainThread(StartPlayerRecording);
        }

        private void StartPlayerRecording()
        {
            if (Phase != AiHomeworkDebatePhase.PlayerReady ||
                _playerRecording ||
                _playerTranscriptionConnecting)
            {
                if (_playerRecording || _playerTranscriptionConnecting)
                {
                    _statusText.text = "Recording is already running and will stop automatically at 03:00.";
                }

                return;
            }

            DeactivateFocusedInput();
            _playerRecording = false;
            _playerTranscriptionConnecting = true;
            _playerStopRequested = false;
            _playerDuration = 0f;
            Phase = AiHomeworkDebatePhase.PlayerSpeaking;
            _phaseText.text = "Round 1: Your CREEI Argument";
            _completePlayerTranscript.Clear();
            _playerText.text = "You: connecting to English realtime transcription...";
            _statusText.text = "Connecting... recording will continue through pauses until 03:00.";
            UpdateTimer("Your Speech", 0f);

            if (realtimeTranscriber == null)
            {
                _playerTranscriptionConnecting = false;
                Phase = AiHomeworkDebatePhase.PlayerReady;
                _statusText.text = "English realtime transcription is not configured.";
                return;
            }

            string microphoneDevice = MicrophoneManager.Instance?.SelectedMicrophoneName ?? string.Empty;
            realtimeTranscriber.StartSession(microphoneDevice);
        }

        private void StopPlayerRecordingAtLimit()
        {
            if (!_playerRecording || _playerStopRequested)
            {
                return;
            }

            _playerStopRequested = true;
            _playerRecording = false;
            _playerTranscriptionConnecting = false;
            _playerDuration = speechTargetSeconds;
            Phase = AiHomeworkDebatePhase.PlayerAwaitingTranscript;
            UpdateTimer("Your Speech", speechTargetSeconds);
            _statusText.text = "03:00 reached. Recording stopped; waiting for the final transcript...";
            realtimeTranscriber?.StopSession();
            _transcriptFallbackRoutine = StartCoroutine(CompletePlayerTurnAfterTranscriptTimeout());
        }

        private IEnumerator CompletePlayerTurnAfterTranscriptTimeout()
        {
            yield return new WaitForSecondsRealtime(12f);
            _transcriptFallbackRoutine = null;
            if (Phase == AiHomeworkDebatePhase.PlayerAwaitingTranscript)
            {
                string completeTranscript = _completePlayerTranscript.ToString().Trim();
                _playerText.text = string.IsNullOrWhiteSpace(completeTranscript)
                    ? "You: (Final transcript was not returned before timeout.)"
                    : "You: " + BuildRollingTranscriptText(completeTranscript, string.Empty);
                CompleteDebate("Your CREEI argument was recorded. Debate complete.");
            }
        }

        private void StartLeoPresetSpeech()
        {
            if (Phase == AiHomeworkDebatePhase.OpponentSpeaking)
            {
                return;
            }

            Phase = AiHomeworkDebatePhase.OpponentSpeaking;
            _phaseText.text = "Round 1: Leo's CREEI Argument";
            _statusText.text = "Leo is delivering his preset CREEI argument.";
            _leoText.text = "Leo: starting...";
            UpdateTimer("Leo's Speech", 0f);
            _leoTranscript.Clear();
            _leoSpeechSegments = (string[])PresetLeoCreeiSegments.Clone();
            _leoSegmentIndex = 0;
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(leoNPC);
            SendNextLeoSegment();
        }

        private void SendNextLeoSegment()
        {
            if (leoNPC == null || _leoSegmentIndex >= _leoSpeechSegments.Length)
            {
                StartPlayerTurn("Leo finished his CREEI argument. Your turn is ready.");
                return;
            }

            CreeiStage stage = (CreeiStage)Mathf.Clamp(_leoSegmentIndex, 0, 4);
            string segment = _leoSpeechSegments[_leoSegmentIndex].Trim();
            _leoSegmentIndex++;
            string prompt =
                "You are Leo. Speak only in English. Read the prepared debate speech below aloud once. " +
                "Do not answer the learner, add a new argument, ask a question, or discuss these instructions. " +
                "Speak calmly at a clear debate pace. This is the " + stage + " part. Prepared text: [" + segment + "]";
            _pendingLeoPrompt = prompt;
            _pendingLeoSpeechText = segment;
            _leoSegmentSendAttempts = 0;
            if (_forceLocalLeoTts)
            {
                StartLocalLeoTtsFallback("Convai remains unavailable.");
            }
            else
            {
                SendPendingLeoSegment();
            }
        }

        private void SendPendingLeoSegment()
        {
            if (Phase != AiHomeworkDebatePhase.OpponentSpeaking ||
                leoNPC == null ||
                string.IsNullOrWhiteSpace(_pendingLeoPrompt))
            {
                return;
            }

            _leoSegmentSendAttempts++;
            if (!IsLeoSpeechPipelineReady())
            {
                RetryOrFailLeoSegment("Leo's Convai audio pipeline is unavailable.");
                return;
            }

            _leoAudioObservedForCurrentSegment = false;
            ConvaiNPCManager.Instance.SetActiveConvaiNPC(leoNPC);
            if (_leoSegmentSendAttempts > 1)
            {
                _statusText.text = "Leo audio did not start. Retrying this CREEI part...";
            }

            leoNPC.SendTextDataAsync(_pendingLeoPrompt);
            if (_leoSpeechStartGuardRoutine != null)
            {
                StopCoroutine(_leoSpeechStartGuardRoutine);
            }

            _leoSpeechStartGuardRoutine = StartCoroutine(GuardLeoSpeechStart());
        }

        private IEnumerator GuardLeoSpeechStart()
        {
            yield return new WaitForSecondsRealtime(LeoAudioStartTimeoutSeconds);
            _leoSpeechStartGuardRoutine = null;
            if (Phase == AiHomeworkDebatePhase.OpponentSpeaking &&
                !_leoAudioObservedForCurrentSegment)
            {
                RetryOrFailLeoSegment("Convai returned no Leo audio before timeout.");
            }
        }

        private void RetryOrFailLeoSegment(string reason)
        {
            if (Phase != AiHomeworkDebatePhase.OpponentSpeaking ||
                _leoAudioObservedForCurrentSegment ||
                _leoSpeechRetryRoutine != null)
            {
                return;
            }

            if (_leoSpeechStartGuardRoutine != null)
            {
                StopCoroutine(_leoSpeechStartGuardRoutine);
                _leoSpeechStartGuardRoutine = null;
            }

            if (_leoSegmentSendAttempts < MaxLeoSegmentSendAttempts)
            {
                _leoSpeechRetryRoutine = StartCoroutine(RetryLeoSegmentAfterDelay(reason));
                return;
            }

            StartLocalLeoTtsFallback(reason);
        }

        private IEnumerator RetryLeoSegmentAfterDelay(string reason)
        {
            _statusText.text = reason + " Retrying automatically...";
            yield return new WaitForSecondsRealtime(1.5f);
            _leoSpeechRetryRoutine = null;
            SendPendingLeoSegment();
        }

        private void ObserveLeoAudio()
        {
            _leoAudioObservedForCurrentSegment = true;
            if (_leoSpeechStartGuardRoutine != null)
            {
                StopCoroutine(_leoSpeechStartGuardRoutine);
                _leoSpeechStartGuardRoutine = null;
            }
        }

        private void HandleLeoTextSendFailed(ConvaiNPC sendingNpc, string failure)
        {
            if (sendingNpc != leoNPC)
            {
                return;
            }

            string reason = string.IsNullOrWhiteSpace(failure)
                ? "Convai could not deliver Leo's speech request."
                : "Convai could not deliver Leo's speech request: " + failure.Trim();
            RunOnMainThread(() => RetryOrFailLeoSegment(reason));
        }

        private void StartLocalLeoTtsFallback(string reason)
        {
            if (Phase != AiHomeworkDebatePhase.OpponentSpeaking ||
                _usingLocalLeoTtsForCurrentSegment ||
                string.IsNullOrWhiteSpace(_pendingLeoSpeechText))
            {
                return;
            }

            if (_leoSpeechStartGuardRoutine != null)
            {
                StopCoroutine(_leoSpeechStartGuardRoutine);
                _leoSpeechStartGuardRoutine = null;
            }

            if (_leoSpeechRetryRoutine != null)
            {
                StopCoroutine(_leoSpeechRetryRoutine);
                _leoSpeechRetryRoutine = null;
            }

            leoNPC?.InterruptCharacterSpeech();
            _forceLocalLeoTts = true;
            _usingLocalLeoTtsForCurrentSegment = true;
            _leoAudioObservedForCurrentSegment = true;
            _statusText.text = reason + " Leo is continuing with the offline English voice.";
            AppendLeoTranscript(_pendingLeoSpeechText);

            if (!_leoTimerRunning)
            {
                _leoTimerRunning = true;
                _leoStartedAt = Time.realtimeSinceStartup;
                _leoDuration = 0f;
                UpdateTimer("Leo's Speech", 0f);
            }

            _localLeoTtsRoutine = StartCoroutine(PlayLeoSegmentWithWindowsTts(_pendingLeoSpeechText));
        }

        private IEnumerator PlayLeoSegmentWithWindowsTts(string speechText)
        {
            _localLeoTtsProcess = StartWindowsTtsProcess(speechText);
            if (_localLeoTtsProcess == null)
            {
                _localLeoTtsRoutine = null;
                _usingLocalLeoTtsForCurrentSegment = false;
                FailLeoSpeech("Convai is unavailable and the offline Windows voice could not start.");
                yield break;
            }

            while (Phase == AiHomeworkDebatePhase.OpponentSpeaking &&
                   !_localLeoTtsProcess.HasExited)
            {
                yield return new WaitForSecondsRealtime(0.1f);
            }

            DisposeLocalLeoTtsProcess(false);
            _localLeoTtsRoutine = null;
            _usingLocalLeoTtsForCurrentSegment = false;
            if (Phase != AiHomeworkDebatePhase.OpponentSpeaking)
            {
                yield break;
            }

            if (_leoSegmentIndex < _leoSpeechSegments.Length)
            {
                _statusText.text = "Leo is continuing to the next CREEI part with the offline English voice...";
                SendNextLeoSegment();
            }
            else
            {
                StartPlayerTurn("Leo finished his CREEI argument. Your turn is ready.");
            }
        }

        private static Process StartWindowsTtsProcess(string text)
        {
#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            try
            {
                string escapedText = text.Replace("'", "''");
                string command =
                    "Add-Type -AssemblyName System.Speech; " +
                    "$s = New-Object System.Speech.Synthesis.SpeechSynthesizer; " +
                    "$voice = $s.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -like 'en-*' -and $_.VoiceInfo.Gender -eq 'Male' } | Select-Object -First 1; " +
                    "if ($voice) { $s.SelectVoice($voice.VoiceInfo.Name) }; " +
                    "$s.Rate = 1; $s.Volume = 100; $s.Speak('" + escapedText + "');";
                string encodedCommand = Convert.ToBase64String(Encoding.Unicode.GetBytes(command));
                ProcessStartInfo startInfo = new()
                {
                    FileName = "powershell.exe",
                    Arguments = "-NoProfile -NonInteractive -WindowStyle Hidden -EncodedCommand " + encodedCommand,
                    CreateNoWindow = true,
                    UseShellExecute = false
                };
                return Process.Start(startInfo);
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("Leo offline Windows TTS failed: " + exception.Message);
                return null;
            }
#else
            _ = text;
            return null;
#endif
        }

        private void DisposeLocalLeoTtsProcess(bool terminate)
        {
            if (_localLeoTtsProcess == null)
            {
                return;
            }

            try
            {
                if (terminate && !_localLeoTtsProcess.HasExited)
                {
                    _localLeoTtsProcess.Kill();
                }
            }
            catch (Exception exception)
            {
                UnityEngine.Debug.LogWarning("Could not stop Leo offline Windows TTS: " + exception.Message);
            }
            finally
            {
                _localLeoTtsProcess.Dispose();
                _localLeoTtsProcess = null;
            }
        }

        private void AppendLeoTranscript(string speechText)
        {
            string safeText = speechText?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(safeText))
            {
                return;
            }

            if (_leoTranscript.Length > 0)
            {
                _leoTranscript.Append(' ');
            }

            _leoTranscript.Append(safeText);
            _leoText.text = "Leo: " + BuildRollingTranscriptText(_leoTranscript.ToString(), string.Empty);
        }

        private void FailLeoSpeech(string reason)
        {
            StopLeoSpeechRoutines();
            _leoTimerRunning = false;
            Phase = AiHomeworkDebatePhase.Intro;
            _phaseText.text = "Leo Speech Unavailable";
            _statusText.text = reason + " Press Retry Leo.";
            _leoText.text = "Leo: no audio was received.";
            _startButton.gameObject.SetActive(true);
            _startButton.GetComponentInChildren<TMP_Text>().text = "Retry Leo";
            SetCursorMode(true);
        }

        private void StopLeoSpeechRoutines()
        {
            if (_leoInitializationRoutine != null)
            {
                StopCoroutine(_leoInitializationRoutine);
                _leoInitializationRoutine = null;
            }

            if (_leoSpeechStartGuardRoutine != null)
            {
                StopCoroutine(_leoSpeechStartGuardRoutine);
                _leoSpeechStartGuardRoutine = null;
            }

            if (_leoSpeechRetryRoutine != null)
            {
                StopCoroutine(_leoSpeechRetryRoutine);
                _leoSpeechRetryRoutine = null;
            }

            if (_localLeoTtsRoutine != null)
            {
                StopCoroutine(_localLeoTtsRoutine);
                _localLeoTtsRoutine = null;
            }

            DisposeLocalLeoTtsProcess(true);
            _usingLocalLeoTtsForCurrentSegment = false;
        }

        private void CaptureLeoAudio(ConvaiNPCAudioManager.ResponseAudio response)
        {
            if (response == null || response.IsFinal || string.IsNullOrWhiteSpace(response.AudioTranscript))
            {
                return;
            }

            if (_usingLocalLeoTtsForCurrentSegment)
            {
                return;
            }

            ObserveLeoAudio();
            AppendLeoTranscript(response.AudioTranscript);
        }

        private void HandleLeoTalkingChanged(bool isTalking)
        {
            if (Phase != AiHomeworkDebatePhase.OpponentSpeaking)
            {
                return;
            }

            if (_usingLocalLeoTtsForCurrentSegment)
            {
                return;
            }

            if (isTalking && !_leoTimerRunning)
            {
                ObserveLeoAudio();
                _leoTimerRunning = true;
                _leoStartedAt = Time.realtimeSinceStartup;
                _leoDuration = 0f;
                UpdateTimer("Leo's Speech", 0f);
                return;
            }

            if (isTalking)
            {
                ObserveLeoAudio();
                return;
            }

            if (!isTalking && _leoTimerRunning)
            {
                _leoDuration = Mathf.Min(speechTargetSeconds, Time.realtimeSinceStartup - _leoStartedAt);
                UpdateTimer("Leo's Speech", _leoDuration);
                if (_leoSegmentIndex < _leoSpeechSegments.Length)
                {
                    _statusText.text = "Leo is continuing to the next CREEI part...";
                    SendNextLeoSegment();
                }
                else
                {
                    _leoTimerRunning = false;
                    StartPlayerTurn("Leo finished his CREEI argument. Your turn is ready.");
                }
            }
        }

        private void StartPlayerTurn(string transitionMessage)
        {
            if (Phase == AiHomeworkDebatePhase.PlayerReady ||
                Phase == AiHomeworkDebatePhase.PlayerSpeaking ||
                Phase == AiHomeworkDebatePhase.PlayerAwaitingTranscript ||
                Phase == AiHomeworkDebatePhase.Complete)
            {
                return;
            }

            _leoTimerRunning = false;
            StopLeoSpeechRoutines();
            _playerRecording = false;
            _playerTranscriptionConnecting = false;
            _playerStopRequested = false;
            _playerDuration = 0f;
            _completePlayerTranscript.Clear();
            Phase = AiHomeworkDebatePhase.PlayerReady;
            _phaseText.text = "Round 2: Your CREEI Argument";
            _playerText.text = "You: press T once when you are ready.";
            _statusText.text = transitionMessage + " Press T once; recording stops automatically at 03:00.";
            UpdateTimer("Your Speech", 0f);
            ConvaiNPCManager.Instance?.SetActiveConvaiNPC(leoNPC);
        }

        private void HandleRealtimeTranscriptionStarted()
        {
            if (Phase != AiHomeworkDebatePhase.PlayerSpeaking ||
                !_playerTranscriptionConnecting ||
                _playerStopRequested)
            {
                realtimeTranscriber?.CancelSession();
                return;
            }

            _playerTranscriptionConnecting = false;
            _playerRecording = true;
            _playerStartedAt = Time.realtimeSinceStartup;
            _playerDuration = 0f;
            _playerText.text = "You (listening): begin your complete argument...";
            _statusText.text = "Recording continuously through pauses. It stops automatically at 03:00.";
            UpdateTimer("Your Speech", 0f);
        }

        private void HandleRealtimeTranscriptUpdated(string transcript)
        {
            if (Phase != AiHomeworkDebatePhase.PlayerSpeaking &&
                Phase != AiHomeworkDebatePhase.PlayerAwaitingTranscript)
            {
                return;
            }

            string safeTranscript = transcript?.Trim() ?? string.Empty;
            _completePlayerTranscript.Clear();
            _completePlayerTranscript.Append(safeTranscript);
            _playerText.text = string.IsNullOrWhiteSpace(safeTranscript)
                ? "You (listening): waiting for English speech..."
                : "You (listening): " + BuildRollingTranscriptText(safeTranscript, string.Empty);
        }

        private void HandleRealtimeTranscriptionCompleted(string transcript)
        {
            if (Phase != AiHomeworkDebatePhase.PlayerAwaitingTranscript)
            {
                return;
            }

            if (_transcriptFallbackRoutine != null)
            {
                StopCoroutine(_transcriptFallbackRoutine);
                _transcriptFallbackRoutine = null;
            }

            string safeTranscript = transcript?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(safeTranscript))
            {
                _completePlayerTranscript.Clear();
                _completePlayerTranscript.Append(safeTranscript);
            }

            string completeTranscript = _completePlayerTranscript.ToString().Trim();
            _playerText.text = string.IsNullOrWhiteSpace(completeTranscript)
                ? "You: (No speech was transcribed.)"
                : "You: " + BuildRollingTranscriptText(completeTranscript, string.Empty);
            CompleteDebate("Your CREEI argument was recorded. Debate complete.");
        }

        private void HandleRealtimeTranscriptionFailed(string error)
        {
            if (Phase != AiHomeworkDebatePhase.PlayerSpeaking &&
                Phase != AiHomeworkDebatePhase.PlayerAwaitingTranscript)
            {
                return;
            }

            string reason = string.IsNullOrWhiteSpace(error)
                ? "English realtime transcription failed."
                : error.Trim();
            _playerRecording = false;
            _playerTranscriptionConnecting = false;

            if (Phase == AiHomeworkDebatePhase.PlayerAwaitingTranscript &&
                _completePlayerTranscript.Length > 0)
            {
                HandleRealtimeTranscriptionCompleted(_completePlayerTranscript.ToString());
                return;
            }

            _playerStopRequested = false;
            Phase = AiHomeworkDebatePhase.PlayerReady;
            _statusText.text = reason + " Press T once to try the full recording again.";
            _playerText.text = "You: realtime transcription stopped before the debate was complete.";
            UpdateTimer("Your Speech", 0f);
        }

        private void SubscribeRealtimeTranscriber()
        {
            if (realtimeTranscriber == null)
            {
                return;
            }

            realtimeTranscriber.SessionStarted -= HandleRealtimeTranscriptionStarted;
            realtimeTranscriber.TranscriptUpdated -= HandleRealtimeTranscriptUpdated;
            realtimeTranscriber.SessionCompleted -= HandleRealtimeTranscriptionCompleted;
            realtimeTranscriber.SessionFailed -= HandleRealtimeTranscriptionFailed;
            realtimeTranscriber.SessionStarted += HandleRealtimeTranscriptionStarted;
            realtimeTranscriber.TranscriptUpdated += HandleRealtimeTranscriptUpdated;
            realtimeTranscriber.SessionCompleted += HandleRealtimeTranscriptionCompleted;
            realtimeTranscriber.SessionFailed += HandleRealtimeTranscriptionFailed;
        }

        private void UnsubscribeRealtimeTranscriber()
        {
            if (realtimeTranscriber == null)
            {
                return;
            }

            realtimeTranscriber.SessionStarted -= HandleRealtimeTranscriptionStarted;
            realtimeTranscriber.TranscriptUpdated -= HandleRealtimeTranscriptUpdated;
            realtimeTranscriber.SessionCompleted -= HandleRealtimeTranscriptionCompleted;
            realtimeTranscriber.SessionFailed -= HandleRealtimeTranscriptionFailed;
        }

        private static string BuildRollingTranscriptText(string confirmedText, string livePreview)
        {
            string confirmed = confirmedText?.Trim() ?? string.Empty;
            string preview = livePreview?.Trim() ?? string.Empty;
            string combined;
            if (string.IsNullOrWhiteSpace(confirmed))
            {
                combined = preview;
            }
            else if (string.IsNullOrWhiteSpace(preview) || confirmed.EndsWith(preview, StringComparison.Ordinal))
            {
                combined = confirmed;
            }
            else
            {
                combined = confirmed + " " + preview;
            }

            return combined;
        }

        private void CompleteDebate(string message)
        {
            Phase = AiHomeworkDebatePhase.Complete;
            _playerRecording = false;
            _leoTimerRunning = false;
            _phaseText.text = "Debate Complete";
            _statusText.text = message;
            _startButton.gameObject.SetActive(true);
            _startButton.GetComponentInChildren<TMP_Text>().text = "Restart Debate";
            SetCursorMode(true);
        }

        private void RegisterInput()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            ConvaiInputManager.ShouldUseTapToTalk = ShouldUseTapToTalk;
            ConvaiInputManager.TapToTalkRequested = HandleTapToTalk;
            ConvaiInputManager.ShouldSuppressTalkInput = AlwaysSuppressDefaultTalk;
            ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = AlwaysSuppressDefaultTalk;
            ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction = AlwaysSuppressDefaultTalk;
            ConvaiNPCManager.ShouldSuppressAutoActiveNPCUpdate = AlwaysSuppressDefaultTalk;
        }

        private void UnregisterInput()
        {
            if (ConvaiInputManager.ShouldUseTapToTalk == (Func<bool>)ShouldUseTapToTalk)
            {
                ConvaiInputManager.ShouldUseTapToTalk = null;
            }

            if (ConvaiInputManager.TapToTalkRequested == (Action)HandleTapToTalk)
            {
                ConvaiInputManager.TapToTalkRequested = null;
            }

            if (ConvaiInputManager.ShouldSuppressTalkInput == (Func<bool>)AlwaysSuppressDefaultTalk)
            {
                ConvaiInputManager.ShouldSuppressTalkInput = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressTalkInput == (Func<bool>)AlwaysSuppressDefaultTalk)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressTalkInput = null;
            }

            if (ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction == (Func<bool>)AlwaysSuppressDefaultTalk)
            {
                ConvaiPlayerInteractionManager.ShouldSuppressNpcInteraction = null;
            }

            if (ConvaiNPCManager.ShouldSuppressAutoActiveNPCUpdate == (Func<bool>)AlwaysSuppressDefaultTalk)
            {
                ConvaiNPCManager.ShouldSuppressAutoActiveNPCUpdate = null;
            }

        }

        private void SubscribeVoice()
        {
            if (ConvaiGRPCAPI.Instance != null)
            {
                ConvaiGRPCAPI.Instance.OnTextSendFailed -= HandleLeoTextSendFailed;
                ConvaiGRPCAPI.Instance.OnTextSendFailed += HandleLeoTextSendFailed;
            }
        }

        private void UnsubscribeVoice()
        {
            if (ConvaiGRPCAPI.Instance != null)
            {
                ConvaiGRPCAPI.Instance.OnTextSendFailed -= HandleLeoTextSendFailed;
            }
        }

        private void SubscribeLeoAudio()
        {
            if (leoNPC?.AudioManager == null)
            {
                return;
            }

            leoNPC.AudioManager.OnResponseAudioStarted -= CaptureLeoAudio;
            leoNPC.AudioManager.OnResponseAudioStarted += CaptureLeoAudio;
            leoNPC.AudioManager.OnCharacterTalkingChanged -= HandleLeoTalkingChanged;
            leoNPC.AudioManager.OnCharacterTalkingChanged += HandleLeoTalkingChanged;
        }

        private void UnsubscribeLeoAudio()
        {
            if (leoNPC?.AudioManager == null)
            {
                return;
            }

            leoNPC.AudioManager.OnResponseAudioStarted -= CaptureLeoAudio;
            leoNPC.AudioManager.OnCharacterTalkingChanged -= HandleLeoTalkingChanged;
        }

        private static bool AlwaysSuppressDefaultTalk()
        {
            return true;
        }

        private void EnterIntro()
        {
            Phase = AiHomeworkDebatePhase.Intro;
            _phaseText.text = "Two-Round CREEI Debate";
            _statusText.text = "Press Start Debate when you are ready.";
            _playerText.text = "You will speak after Leo and argue for responsible AI use in homework.";
            _leoText.text = "Leo speaks first and argues against allowing AI use in homework.";
            UpdateTimer("Leo's Speech", 0f);
            _startButton.gameObject.SetActive(true);
            SetCursorMode(true);
        }

        private void BuildUi()
        {
            if (uiCanvas == null)
            {
                Canvas[] canvases = FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                foreach (Canvas canvas in canvases)
                {
                    if (canvas.name == "Debate Round UI")
                    {
                        uiCanvas = canvas;
                        break;
                    }
                }
            }

            if (uiCanvas == null)
            {
                GameObject canvasObject = new("AI Homework Debate Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                uiCanvas = canvasObject.GetComponent<Canvas>();
                uiCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasObject.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
                canvasObject.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1280f, 720f);
            }

            _uiRoot = CreateRect("AI Homework Debate UI", uiCanvas.transform);
            RectTransform rootRect = _uiRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = new Vector2(0f, 1f);
            rootRect.anchorMax = new Vector2(0f, 1f);
            rootRect.pivot = new Vector2(0f, 1f);
            rootRect.anchoredPosition = new Vector2(18f, -18f);
            rootRect.sizeDelta = new Vector2(500f, 610f);
            Image rootImage = _uiRoot.AddComponent<Image>();
            rootImage.color = new Color(0.04f, 0.05f, 0.07f, 0.80f);

            VerticalLayoutGroup layout = _uiRoot.AddComponent<VerticalLayoutGroup>();
            layout.padding = new RectOffset(16, 16, 14, 14);
            layout.spacing = 10f;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            TMP_Text title = CreateText(_uiRoot.transform, "AI Homework CREEI Debate", 24, FontStyles.Bold, 34f);
            title.color = new Color(1f, 0.84f, 0.30f);
            TMP_Text topicText = CreateText(_uiRoot.transform, "Topic: " + topic, 16, FontStyles.Normal, 52f);
            topicText.enableWordWrapping = true;
            _phaseText = CreateText(_uiRoot.transform, string.Empty, 18, FontStyles.Bold, 30f);
            _statusText = CreateText(_uiRoot.transform, string.Empty, 15, FontStyles.Bold, 48f);
            _playerText = CreateScrollableTranscript(
                _uiRoot.transform,
                "Player Transcript",
                128f,
                out _playerTranscriptScrollRect);
            _leoText = CreateScrollableTranscript(
                _uiRoot.transform,
                "Leo Transcript",
                128f,
                out _leoTranscriptScrollRect);
            _startButton = CreateButton(_uiRoot.transform, "Start Debate", BeginDebate);

            GameObject timerRoot = CreateRect("Three Minute Debate Timer", uiCanvas.transform);
            RectTransform timerRect = timerRoot.GetComponent<RectTransform>();
            timerRect.anchorMin = new Vector2(0.5f, 1f);
            timerRect.anchorMax = new Vector2(0.5f, 1f);
            timerRect.pivot = new Vector2(0.5f, 1f);
            timerRect.anchoredPosition = new Vector2(0f, -18f);
            timerRect.sizeDelta = new Vector2(480f, 58f);
            Image timerImage = timerRoot.AddComponent<Image>();
            timerImage.color = new Color(0.04f, 0.05f, 0.07f, 0.88f);
            timerImage.raycastTarget = false;
            _timerText = CreateText(timerRoot.transform, string.Empty, 28, FontStyles.Bold, 58f);
            RectTransform timerTextRect = _timerText.rectTransform;
            timerTextRect.anchorMin = Vector2.zero;
            timerTextRect.anchorMax = Vector2.one;
            timerTextRect.offsetMin = new Vector2(12f, 0f);
            timerTextRect.offsetMax = new Vector2(-12f, 0f);
            _timerText.alignment = TextAlignmentOptions.Center;
            _timerText.color = new Color(1f, 0.84f, 0.30f);
        }

        private static GameObject CreateRect(string name, Transform parent)
        {
            GameObject result = new(name, typeof(RectTransform));
            result.transform.SetParent(parent, false);
            return result;
        }

        private static TMP_Text CreateText(Transform parent, string value, int size, FontStyles style, float height)
        {
            GameObject textObject = CreateRect("Text", parent);
            TextMeshProUGUI text = textObject.AddComponent<TextMeshProUGUI>();
            text.text = value;
            text.fontSize = size;
            text.fontStyle = style;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.Left;
            LayoutElement layout = textObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;
            return text;
        }

        private static TMP_Text CreateScrollableTranscript(
            Transform parent,
            string objectName,
            float height,
            out ScrollRect scrollRect)
        {
            GameObject scrollObject = CreateRect(objectName, parent);
            scrollObject.GetComponent<RectTransform>().sizeDelta = new Vector2(0f, height);
            Image background = scrollObject.AddComponent<Image>();
            background.color = new Color(0f, 0f, 0f, 0.48f);
            LayoutElement layout = scrollObject.AddComponent<LayoutElement>();
            layout.preferredHeight = height;
            layout.minHeight = height;

            GameObject viewportObject = CreateRect("Viewport", scrollObject.transform);
            RectTransform viewportRect = viewportObject.GetComponent<RectTransform>();
            viewportRect.anchorMin = Vector2.zero;
            viewportRect.anchorMax = Vector2.one;
            viewportRect.offsetMin = new Vector2(8f, 7f);
            viewportRect.offsetMax = new Vector2(-26f, -7f);
            Image viewportImage = viewportObject.AddComponent<Image>();
            viewportImage.color = new Color(1f, 1f, 1f, 0.001f);
            viewportObject.AddComponent<RectMask2D>();

            GameObject contentObject = CreateRect("Content", viewportObject.transform);
            RectTransform contentRect = contentObject.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = Vector2.one;
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.anchoredPosition = Vector2.zero;
            contentRect.sizeDelta = Vector2.zero;

            TextMeshProUGUI text = contentObject.AddComponent<TextMeshProUGUI>();
            text.text = string.Empty;
            text.fontSize = 14f;
            text.fontStyle = FontStyles.Normal;
            text.color = Color.white;
            text.alignment = TextAlignmentOptions.TopLeft;
            text.enableWordWrapping = true;
            text.overflowMode = TextOverflowModes.Overflow;
            text.raycastTarget = false;
            ContentSizeFitter contentFitter = contentObject.AddComponent<ContentSizeFitter>();
            contentFitter.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
            contentFitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            GameObject scrollbarObject = CreateRect("Scrollbar", scrollObject.transform);
            RectTransform scrollbarRect = scrollbarObject.GetComponent<RectTransform>();
            scrollbarRect.anchorMin = new Vector2(1f, 0f);
            scrollbarRect.anchorMax = Vector2.one;
            scrollbarRect.pivot = new Vector2(1f, 0.5f);
            scrollbarRect.offsetMin = new Vector2(-17f, 7f);
            scrollbarRect.offsetMax = new Vector2(-7f, -7f);
            Image scrollbarBackground = scrollbarObject.AddComponent<Image>();
            scrollbarBackground.color = new Color(1f, 1f, 1f, 0.16f);

            GameObject handleObject = CreateRect("Handle", scrollbarObject.transform);
            RectTransform handleRect = handleObject.GetComponent<RectTransform>();
            handleRect.anchorMin = Vector2.zero;
            handleRect.anchorMax = Vector2.one;
            handleRect.offsetMin = Vector2.zero;
            handleRect.offsetMax = Vector2.zero;
            Image handleImage = handleObject.AddComponent<Image>();
            handleImage.color = new Color(1f, 0.84f, 0.30f, 0.92f);
            Scrollbar scrollbar = scrollbarObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handleRect;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;

            scrollRect = scrollObject.AddComponent<ScrollRect>();
            scrollRect.content = contentRect;
            scrollRect.viewport = viewportRect;
            scrollRect.horizontal = false;
            scrollRect.vertical = true;
            scrollRect.movementType = ScrollRect.MovementType.Clamped;
            scrollRect.inertia = true;
            scrollRect.scrollSensitivity = 22f;
            scrollRect.verticalScrollbar = scrollbar;
            scrollRect.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
            return text;
        }

        private static void RefreshTranscriptScroll(
            TMP_Text text,
            ScrollRect scrollRect,
            ref string lastText)
        {
            if (text == null || scrollRect == null || string.Equals(lastText, text.text, StringComparison.Ordinal))
            {
                return;
            }

            lastText = text.text ?? string.Empty;
            text.ForceMeshUpdate();
            LayoutRebuilder.ForceRebuildLayoutImmediate(text.rectTransform);
            Canvas.ForceUpdateCanvases();
            scrollRect.StopMovement();
            scrollRect.verticalNormalizedPosition = 0f;
        }

        private static Button CreateButton(Transform parent, string label, UnityEngine.Events.UnityAction action)
        {
            GameObject buttonObject = CreateRect(label, parent);
            Image image = buttonObject.AddComponent<Image>();
            image.color = new Color(0.18f, 0.46f, 0.36f, 0.96f);
            Button button = buttonObject.AddComponent<Button>();
            button.onClick.AddListener(action);
            LayoutElement layout = buttonObject.AddComponent<LayoutElement>();
            layout.preferredHeight = 48f;
            layout.minHeight = 48f;
            TMP_Text text = CreateText(buttonObject.transform, label, 16, FontStyles.Bold, 48f);
            RectTransform rect = text.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            text.alignment = TextAlignmentOptions.Center;
            return button;
        }

        private void UpdateTimer(string speaker, float seconds)
        {
            int current = Mathf.Max(0, Mathf.FloorToInt(seconds));
            int target = Mathf.Max(1, Mathf.RoundToInt(speechTargetSeconds));
            _timerText.text = $"{speaker}  {current / 60:00}:{current % 60:00} / {target / 60:00}:{target % 60:00}";
        }

        private static void HideLegacyUi()
        {
            GameObject.Find("Interactive Controls")?.SetActive(false);
            GameObject.Find("Start Debate Button")?.SetActive(false);
            GameObject.Find("Round Timer")?.SetActive(false);
            GameObject.Find("Referee Caption Panel")?.SetActive(false);
        }

        private void SetCursorMode(bool uiMode)
        {
            _isUiMode = uiMode;
            Cursor.lockState = uiMode ? CursorLockMode.None : CursorLockMode.Locked;
            Cursor.visible = uiMode;
        }

        private static void DeactivateFocusedInput()
        {
            if (EventSystem.current != null)
            {
                EventSystem.current.SetSelectedGameObject(null);
            }
        }

        private static void RunOnMainThread(Action action)
        {
            if (MainThreadDispatcher.Instance != null)
            {
                MainThreadDispatcher.Instance.RunOnMainThread(action);
            }
            else
            {
                action?.Invoke();
            }
        }
    }
}
