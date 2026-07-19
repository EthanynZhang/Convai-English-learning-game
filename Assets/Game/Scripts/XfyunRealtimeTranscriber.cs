using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace Game.Debate
{
    public sealed class XfyunRealtimeTranscriber : MonoBehaviour
    {
        public const string AppIdPlayerPrefsKey = "XFYUN_RTASR_APP_ID";
        public const string ApiKeyPlayerPrefsKey = "XFYUN_RTASR_API_KEY";

        private const string Endpoint = "wss://rtasr.xfyun.cn/v1/ws";
        private const string ProjectAppId = "";
        private const string ProjectApiKey = "";
        private const int TargetSampleRate = 16000;
        private const int MicrophoneBufferSeconds = 10;
        private const int SamplesPerPacket = 640;
        private const int PacketIntervalMilliseconds = 40;
        private const int ServerStartTimeoutSeconds = 12;
        private const int FinalResultTimeoutSeconds = 8;

        private readonly ConcurrentQueue<Action> _mainThreadActions = new();
        private readonly ConcurrentQueue<byte[]> _audioPackets = new();
        private readonly SortedDictionary<int, string> _segments = new();
        private readonly object _transcriptStateLock = new();
        private readonly List<float> _sourceSamples = new();
        private readonly List<short> _targetSamples = new();
        private readonly List<short> _capturedPcm16 = new();

        private ClientWebSocket _socket;
        private CancellationTokenSource _sessionCancellation;
        private TaskCompletionSource<bool> _serverStarted;
        private TaskCompletionSource<bool> _stopRequested;
        private AudioClip _microphoneClip;
        private string _microphoneDevice;
        private int _lastMicrophonePosition;
        private int _sourceSampleRate;
        private int _sourceChannels;
        private double _sourceSamplePosition;
        private int _lastTickFrame = -1;
        private volatile bool _cancelRequested;
        private volatile bool _failureRaised;
        private bool _microphonePositionReady;
        private int _transcriptUpdateQueued;
        private string _liveSegment = string.Empty;
        private string _pendingTranscriptUpdate = string.Empty;
        private string _latestTranscript = string.Empty;

        public event Action SessionStarted;
        public event Action<string> TranscriptUpdated;
        public event Action<string> SessionCompleted;
        public event Action<string> SessionFailed;

        public bool IsConnecting { get; private set; }
        public bool IsRecording { get; private set; }
        public bool IsSessionActive => IsConnecting || IsRecording || _sessionCancellation != null;
        public XfyunAudioCapture LastAudioCapture { get; private set; }
        public string LatestTranscriptSnapshot
        {
            get
            {
                lock (_transcriptStateLock)
                {
                    return _latestTranscript ?? string.Empty;
                }
            }
        }

        private void Update()
        {
            Tick();
        }

        public void Tick()
        {
            if (_lastTickFrame == Time.frameCount)
            {
                return;
            }

            _lastTickFrame = Time.frameCount;
            while (_mainThreadActions.TryDequeue(out Action action))
            {
                action?.Invoke();
            }

            if (IsRecording)
            {
                CaptureAvailableMicrophoneAudio();
            }
        }

        private void OnDestroy()
        {
            CancelSession();
        }

        public void StartSession(string preferredMicrophoneDevice)
        {
            if (IsConnecting || IsRecording || _sessionCancellation != null)
            {
                SessionFailed?.Invoke("Realtime transcription is already active.");
                return;
            }

            string appId = ReadCredential("XFYUN_RTASR_APP_ID", AppIdPlayerPrefsKey, ProjectAppId);
            string apiKey = ReadCredential("XFYUN_RTASR_API_KEY", ApiKeyPlayerPrefsKey, ProjectApiKey);
            if (string.IsNullOrWhiteSpace(appId) || string.IsNullOrWhiteSpace(apiKey))
            {
                SessionFailed?.Invoke(
                    "iFlytek realtime transcription credentials are missing. Configure the local App ID and API key first.");
                return;
            }

            if (Microphone.devices == null || Microphone.devices.Length == 0)
            {
                SessionFailed?.Invoke("No microphone device is available.");
                return;
            }

            ResetSessionState();
            _microphoneDevice = ResolveMicrophoneDevice(preferredMicrophoneDevice);
            _sessionCancellation = new CancellationTokenSource();
            _serverStarted = NewSignal();
            _stopRequested = NewSignal();
            IsConnecting = true;
            _ = RunSessionAsync(appId.Trim(), apiKey.Trim(), _sessionCancellation.Token);
        }

        public void StopSession()
        {
            if (_sessionCancellation == null || _cancelRequested)
            {
                return;
            }

            CaptureAvailableMicrophoneAudio();
            StopMicrophoneAndFlushAudio();
            _stopRequested?.TrySetResult(true);
        }

        public void CancelSession()
        {
            CancelSessionAndGetLatestTranscript();
        }

        public string CancelSessionAndGetLatestTranscript()
        {
            string transcript;
            lock (_transcriptStateLock)
            {
                _cancelRequested = true;
                transcript = _latestTranscript ?? string.Empty;
            }

            StopMicrophoneAndFlushAudio(false);
            _stopRequested?.TrySetResult(true);
            _sessionCancellation?.Cancel();
            return transcript;
        }

        private async Task RunSessionAsync(string appId, string apiKey, CancellationToken cancellationToken)
        {
            Task receiveTask = null;
            try
            {
                _socket = new ClientWebSocket();
                Task connectTask = _socket.ConnectAsync(BuildEndpointUri(appId, apiKey), cancellationToken);
                Task connectTimeout = Task.Delay(TimeSpan.FromSeconds(ServerStartTimeoutSeconds), cancellationToken);
                Task connected = await Task.WhenAny(connectTask, connectTimeout);
                if (connected != connectTask)
                {
                    throw new TimeoutException("Could not connect to iFlytek realtime transcription in time.");
                }

                await connectTask;
                receiveTask = ReceiveLoopAsync(cancellationToken);

                Task startTimeout = Task.Delay(TimeSpan.FromSeconds(ServerStartTimeoutSeconds), cancellationToken);
                Task started = await Task.WhenAny(_serverStarted.Task, startTimeout);
                if (started != _serverStarted.Task || !_serverStarted.Task.Result)
                {
                    throw new TimeoutException("iFlytek did not start the realtime transcription session in time.");
                }

                QueueOnMainThread(StartMicrophoneCapture);
                Task sendTask = SendLoopAsync(cancellationToken);
                await _stopRequested.Task;
                await sendTask;

                Task finalTimeout = Task.Delay(TimeSpan.FromSeconds(FinalResultTimeoutSeconds), cancellationToken);
                Task finalTask = await Task.WhenAny(receiveTask, finalTimeout);
                if (finalTask != receiveTask)
                {
                    using CancellationTokenSource closeTimeout = new(TimeSpan.FromSeconds(2));
                    await CloseSocketQuietlyAsync(closeTimeout.Token);
                }
                else
                {
                    await receiveTask;
                }

                if (!_cancelRequested && !_failureRaised)
                {
                    string completedTranscript = LatestTranscriptSnapshot;
                    QueueOnMainThread(() => SessionCompleted?.Invoke(completedTranscript));
                }
            }
            catch (OperationCanceledException)
            {
                if (!_cancelRequested)
                {
                    RaiseFailure("Realtime transcription was cancelled before it completed.");
                }
            }
            catch (Exception exception)
            {
                RaiseFailure(BuildNetworkErrorMessage(exception));
            }
            finally
            {
                IsConnecting = false;
                QueueOnMainThread(StopMicrophoneAndFlushAudioWithoutPackets);
                _sessionCancellation?.Dispose();
                _sessionCancellation = null;
                _socket?.Dispose();
                _socket = null;
            }
        }

        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            while (!_stopRequested.Task.IsCompleted || !_audioPackets.IsEmpty)
            {
                if (_audioPackets.TryDequeue(out byte[] packet))
                {
                    await _socket.SendAsync(
                        new ArraySegment<byte>(packet),
                        WebSocketMessageType.Binary,
                        true,
                        cancellationToken);
                    await Task.Delay(PacketIntervalMilliseconds, cancellationToken);
                    continue;
                }

                await Task.Delay(5, cancellationToken);
            }

            byte[] endMarker = Encoding.UTF8.GetBytes("{\"end\": true}");
            await _socket.SendAsync(
                new ArraySegment<byte>(endMarker),
                WebSocketMessageType.Binary,
                true,
                cancellationToken);
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[8192];
            using MemoryStream message = new();
            while (_socket != null && _socket.State == WebSocketState.Open)
            {
                WebSocketReceiveResult result = await _socket.ReceiveAsync(
                    new ArraySegment<byte>(buffer),
                    cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                {
                    break;
                }

                message.Write(buffer, 0, result.Count);
                if (!result.EndOfMessage)
                {
                    continue;
                }

                string json = Encoding.UTF8.GetString(message.ToArray());
                message.SetLength(0);
                HandleServerMessage(json);
            }
        }

        private void HandleServerMessage(string json)
        {
            RtasrEnvelope envelope = JsonUtility.FromJson<RtasrEnvelope>(json);
            if (envelope == null)
            {
                return;
            }

            if (!string.IsNullOrWhiteSpace(envelope.code) && envelope.code != "0")
            {
                string description = string.IsNullOrWhiteSpace(envelope.desc)
                    ? "Unknown iFlytek service error."
                    : envelope.desc;
                RaiseFailure($"iFlytek error {envelope.code}: {description}");
                _stopRequested?.TrySetResult(true);
                return;
            }

            if (string.Equals(envelope.action, "started", StringComparison.OrdinalIgnoreCase))
            {
                _serverStarted?.TrySetResult(true);
                return;
            }

            if (!string.Equals(envelope.action, "result", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(envelope.data))
            {
                return;
            }

            RtasrData data = JsonUtility.FromJson<RtasrData>(envelope.data);
            string segment = ExtractSegmentText(data);
            if (data == null || string.IsNullOrWhiteSpace(segment))
            {
                return;
            }

            string latestTranscript;
            lock (_transcriptStateLock)
            {
                if (_cancelRequested)
                {
                    return;
                }

                bool isFinalResult = string.Equals(data.cn?.st?.type, "0", StringComparison.Ordinal);
                if (isFinalResult)
                {
                    _segments[data.seg_id] = segment;
                    _liveSegment = string.Empty;
                }
                else
                {
                    _liveSegment = segment;
                }

                string confirmedTranscript = JoinSegments(_segments.Values);
                _latestTranscript = JoinEnglishTokens(new[] { confirmedTranscript, _liveSegment });
                latestTranscript = _latestTranscript;
            }

            QueueTranscriptUpdate(latestTranscript);
        }

        private void StartMicrophoneCapture()
        {
            if (_cancelRequested || _sessionCancellation == null)
            {
                return;
            }

            _microphoneClip = Microphone.Start(
                _microphoneDevice,
                true,
                MicrophoneBufferSeconds,
                TargetSampleRate);
            if (_microphoneClip == null)
            {
                RaiseFailure("The selected microphone could not be started.");
                _stopRequested?.TrySetResult(true);
                return;
            }

            _sourceSampleRate = _microphoneClip.frequency;
            _sourceChannels = Mathf.Max(1, _microphoneClip.channels);
            _lastMicrophonePosition = 0;
            _microphonePositionReady = false;
            IsConnecting = false;
            IsRecording = true;
            SessionStarted?.Invoke();
        }

        private void CaptureAvailableMicrophoneAudio()
        {
            if (!IsRecording || _microphoneClip == null)
            {
                return;
            }

            int currentPosition = Microphone.GetPosition(_microphoneDevice);
            if (currentPosition < 0)
            {
                return;
            }

            if (!_microphonePositionReady)
            {
                _lastMicrophonePosition = currentPosition;
                _microphonePositionReady = true;
                return;
            }

            int availableFrames = currentPosition >= _lastMicrophonePosition
                ? currentPosition - _lastMicrophonePosition
                : _microphoneClip.samples - _lastMicrophonePosition + currentPosition;
            if (availableFrames <= 0)
            {
                return;
            }

            int firstFrames = Mathf.Min(availableFrames, _microphoneClip.samples - _lastMicrophonePosition);
            ReadMicrophoneFrames(_lastMicrophonePosition, firstFrames);
            int remainingFrames = availableFrames - firstFrames;
            if (remainingFrames > 0)
            {
                ReadMicrophoneFrames(0, remainingFrames);
            }

            _lastMicrophonePosition = currentPosition;
            ResampleAndQueuePackets();
        }

        private void ReadMicrophoneFrames(int offsetFrames, int frameCount)
        {
            if (frameCount <= 0 || _microphoneClip == null)
            {
                return;
            }

            float[] interleaved = new float[frameCount * _sourceChannels];
            if (!_microphoneClip.GetData(interleaved, offsetFrames))
            {
                return;
            }

            for (int frame = 0; frame < frameCount; frame++)
            {
                float mono = 0f;
                int firstChannel = frame * _sourceChannels;
                for (int channel = 0; channel < _sourceChannels; channel++)
                {
                    mono += interleaved[firstChannel + channel];
                }

                _sourceSamples.Add(mono / _sourceChannels);
            }
        }

        private void ResampleAndQueuePackets()
        {
            if (_sourceSampleRate <= 0 || _sourceSamples.Count < 2)
            {
                return;
            }

            double step = (double)_sourceSampleRate / TargetSampleRate;
            while (_sourceSamplePosition + 1d < _sourceSamples.Count)
            {
                int left = (int)_sourceSamplePosition;
                float blend = (float)(_sourceSamplePosition - left);
                float sample = Mathf.Lerp(_sourceSamples[left], _sourceSamples[left + 1], blend);
                short pcm16 = FloatToPcm16(sample);
                _targetSamples.Add(pcm16);
                _capturedPcm16.Add(pcm16);
                _sourceSamplePosition += step;
            }

            int consumed = Math.Max(0, (int)_sourceSamplePosition - 1);
            if (consumed > 0)
            {
                _sourceSamples.RemoveRange(0, consumed);
                _sourceSamplePosition -= consumed;
            }

            while (_targetSamples.Count >= SamplesPerPacket)
            {
                QueuePcmPacket(SamplesPerPacket);
            }
        }

        private void StopMicrophoneAndFlushAudio(bool queueFinalPacket = true)
        {
            if (_microphoneClip != null && IsRecording)
            {
                CaptureAvailableMicrophoneAudio();
            }

            FinalizeResearchAudioCapture();

            IsRecording = false;
            IsConnecting = false;
            if (_microphoneClip != null)
            {
                Microphone.End(_microphoneDevice);
                _microphoneClip = null;
            }

            if (queueFinalPacket && _targetSamples.Count > 0)
            {
                while (_targetSamples.Count < SamplesPerPacket)
                {
                    _targetSamples.Add(0);
                }

                QueuePcmPacket(SamplesPerPacket);
            }
        }

        private void StopMicrophoneAndFlushAudioWithoutPackets()
        {
            StopMicrophoneAndFlushAudio(false);
        }

        private void QueuePcmPacket(int sampleCount)
        {
            byte[] packet = new byte[sampleCount * sizeof(short)];
            for (int index = 0; index < sampleCount; index++)
            {
                short sample = _targetSamples[index];
                packet[index * 2] = (byte)(sample & 0xff);
                packet[index * 2 + 1] = (byte)((sample >> 8) & 0xff);
            }

            _targetSamples.RemoveRange(0, sampleCount);
            _audioPackets.Enqueue(packet);
        }

        private void FinalizeResearchAudioCapture()
        {
            if (LastAudioCapture != null || _capturedPcm16.Count == 0) return;
            byte[] pcm16Bytes = new byte[_capturedPcm16.Count * sizeof(short)];
            for (int index = 0; index < _capturedPcm16.Count; index++)
            {
                short sample = _capturedPcm16[index];
                pcm16Bytes[index * 2] = (byte)(sample & 0xff);
                pcm16Bytes[index * 2 + 1] = (byte)((sample >> 8) & 0xff);
            }
            LastAudioCapture = new XfyunAudioCapture(pcm16Bytes, TargetSampleRate, 1);
        }

        private void RaiseFailure(string message)
        {
            if (_cancelRequested || _failureRaised)
            {
                return;
            }

            _failureRaised = true;
            QueueOnMainThread(() => SessionFailed?.Invoke(message));
        }

        private async Task CloseSocketQuietlyAsync(CancellationToken cancellationToken)
        {
            if (_socket == null || _socket.State != WebSocketState.Open)
            {
                return;
            }

            try
            {
                await _socket.CloseAsync(WebSocketCloseStatus.NormalClosure, "complete", cancellationToken);
            }
            catch
            {
                // The transcript received before timeout remains usable.
            }
        }

        private void ResetSessionState()
        {
            while (_audioPackets.TryDequeue(out _))
            {
            }

            lock (_transcriptStateLock)
            {
                _segments.Clear();
                _liveSegment = string.Empty;
                _latestTranscript = string.Empty;
                _cancelRequested = false;
            }

            _sourceSamples.Clear();
            _targetSamples.Clear();
            _capturedPcm16.Clear();
            LastAudioCapture = null;
            _sourceSamplePosition = 0d;
            _pendingTranscriptUpdate = string.Empty;
            Interlocked.Exchange(ref _transcriptUpdateQueued, 0);
            _failureRaised = false;
            _microphonePositionReady = false;
        }

        private void QueueOnMainThread(Action action)
        {
            if (action != null)
            {
                _mainThreadActions.Enqueue(action);
            }
        }

        private void QueueTranscriptUpdate(string transcript)
        {
            _pendingTranscriptUpdate = transcript ?? string.Empty;
            if (Interlocked.Exchange(ref _transcriptUpdateQueued, 1) != 0)
            {
                return;
            }

            QueueOnMainThread(() =>
            {
                Interlocked.Exchange(ref _transcriptUpdateQueued, 0);
                TranscriptUpdated?.Invoke(_pendingTranscriptUpdate);
            });
        }

        private static TaskCompletionSource<bool> NewSignal()
        {
            return new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static string ReadCredential(string environmentName, string playerPrefsKey, string projectDefault)
        {
            string environmentValue = Environment.GetEnvironmentVariable(environmentName);
            if (!string.IsNullOrWhiteSpace(environmentValue))
            {
                return environmentValue;
            }

            string playerPrefsValue = PlayerPrefs.GetString(playerPrefsKey, string.Empty);
            return !string.IsNullOrWhiteSpace(playerPrefsValue)
                ? playerPrefsValue
                : projectDefault;
        }

        private static string ResolveMicrophoneDevice(string preferredDevice)
        {
            if (!string.IsNullOrWhiteSpace(preferredDevice))
            {
                foreach (string device in Microphone.devices)
                {
                    if (string.Equals(device, preferredDevice, StringComparison.Ordinal))
                    {
                        return device;
                    }
                }
            }

            return Microphone.devices[0];
        }

        private static Uri BuildEndpointUri(string appId, string apiKey)
        {
            long timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            string baseText = appId + timestamp;
            string checksum;
            using (MD5 md5 = MD5.Create())
            {
                byte[] digest = md5.ComputeHash(Encoding.UTF8.GetBytes(baseText));
                StringBuilder hex = new(digest.Length * 2);
                foreach (byte value in digest)
                {
                    hex.Append(value.ToString("x2"));
                }

                checksum = hex.ToString();
            }

            string signature;
            using (HMACSHA1 hmac = new(Encoding.UTF8.GetBytes(apiKey)))
            {
                signature = Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(checksum)));
            }

            string query =
                "appid=" + Uri.EscapeDataString(appId) +
                "&ts=" + timestamp +
                "&signa=" + Uri.EscapeDataString(signature) +
                "&lang=en&pd=edu&vadMdn=2";
            return new Uri(Endpoint + "?" + query);
        }

        private static short FloatToPcm16(float sample)
        {
            float clamped = Mathf.Clamp(sample, -1f, 1f);
            return clamped < 0f
                ? (short)(clamped * 32768f)
                : (short)(clamped * 32767f);
        }

        private static string ExtractSegmentText(RtasrData data)
        {
            if (data?.cn?.st?.rt == null)
            {
                return string.Empty;
            }

            List<string> words = new();
            foreach (RtasrRt rt in data.cn.st.rt)
            {
                if (rt?.ws == null)
                {
                    continue;
                }

                foreach (RtasrWs ws in rt.ws)
                {
                    if (ws?.cw == null || ws.cw.Length == 0 || string.IsNullOrWhiteSpace(ws.cw[0]?.w))
                    {
                        continue;
                    }

                    words.Add(ws.cw[0].w.Trim());
                }
            }

            return JoinEnglishTokens(words);
        }

        private static string JoinSegments(IEnumerable<string> segments)
        {
            return JoinEnglishTokens(segments);
        }

        private static string JoinEnglishTokens(IEnumerable<string> tokens)
        {
            StringBuilder text = new();
            foreach (string rawToken in tokens)
            {
                string token = rawToken?.Trim() ?? string.Empty;
                if (token.Length == 0)
                {
                    continue;
                }

                bool punctuation = token.Length == 1 && ".,!?;:%)]}".IndexOf(token[0]) >= 0;
                if (text.Length > 0 && !punctuation && text[text.Length - 1] != '\'' && token[0] != '\'')
                {
                    text.Append(' ');
                }

                text.Append(token);
            }

            return text.ToString().Trim();
        }

        private static string BuildNetworkErrorMessage(Exception exception)
        {
            if (exception is WebSocketException webSocketException)
            {
                return "Could not connect to iFlytek realtime transcription: " + webSocketException.Message;
            }

            return "iFlytek realtime transcription failed: " + exception.Message;
        }

        [Serializable]
        private sealed class RtasrEnvelope
        {
            public string action;
            public string code;
            public string data;
            public string desc;
        }

        [Serializable]
        private sealed class RtasrData
        {
            public RtasrCn cn;
            public int seg_id;
        }

        [Serializable]
        private sealed class RtasrCn
        {
            public RtasrSt st;
        }

        [Serializable]
        private sealed class RtasrSt
        {
            public RtasrRt[] rt;
            public string type;
        }

        [Serializable]
        private sealed class RtasrRt
        {
            public RtasrWs[] ws;
        }

        [Serializable]
        private sealed class RtasrWs
        {
            public RtasrCw[] cw;
        }

        [Serializable]
        private sealed class RtasrCw
        {
            public string w;
        }
    }
}
