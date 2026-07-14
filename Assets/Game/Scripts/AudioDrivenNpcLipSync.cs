using System;
using System.Linq;
using UnityEngine;

namespace Game.Debate
{
    public sealed class AudioDrivenNpcLipSync : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private Animator animator;
        [SerializeField] private SkinnedMeshRenderer[] faceRenderers;
        [SerializeField] private string talkParameter = "Talk";
        [SerializeField] private string jawOpenBlendShape = "jawOpen";
        [SerializeField, Min(0f)] private float mouthGain = 1.2f;
        [SerializeField, Range(0f, 100f)] private float maximumJawWeight = 1.5f;
        [SerializeField, Min(0.1f)] private float mouthSmoothing = 18f;

        private readonly float[] _audioSamples = new float[128];
        private int[] _jawOpenIndices = Array.Empty<int>();
        private float _currentJawWeight;
        private bool _wasSpeaking;
        private bool _hasTalkParameter;
        private bool _warnedMissingJaw;

        private void Awake()
        {
            ResolveReferences();
            ResetPresentation();
        }

        private void Update()
        {
            bool isSpeaking = audioSource != null && audioSource.isPlaying;
            if (_wasSpeaking != isSpeaking)
            {
                SetTalkingAnimation(isSpeaking);
            }

            float targetWeight = 0f;
            if (isSpeaking)
            {
                audioSource.GetOutputData(_audioSamples, 0);
                targetWeight = Mathf.Clamp01(CalculateRms(_audioSamples) * mouthGain) * maximumJawWeight;
            }

            float blend = 1f - Mathf.Exp(-mouthSmoothing * Time.deltaTime);
            _currentJawWeight = Mathf.Lerp(_currentJawWeight, targetWeight, blend);
            SetJawWeight(_currentJawWeight);
        }

        private void OnDisable()
        {
            ResetPresentation();
        }

        public void Configure(AudioSource source)
        {
            audioSource = source;
            ResolveReferences();
            ResetPresentation();
        }

        public static float CalculateRms(float[] samples)
        {
            if (samples == null || samples.Length == 0)
            {
                return 0f;
            }

            float sum = 0f;
            for (int i = 0; i < samples.Length; i++)
            {
                sum += samples[i] * samples[i];
            }

            return Mathf.Sqrt(sum / samples.Length);
        }

        private void ResolveReferences()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (animator == null)
            {
                animator = GetComponent<Animator>() ?? GetComponentInChildren<Animator>(true);
            }

            if (faceRenderers == null || faceRenderers.Length == 0)
            {
                faceRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(renderer =>
                        renderer.name.EndsWith("_Head", StringComparison.OrdinalIgnoreCase) ||
                        renderer.name.Equals("CC_Base_Body", StringComparison.OrdinalIgnoreCase))
                    .ToArray();
            }

            _hasTalkParameter = animator != null && animator.parameters.Any(parameter =>
                parameter.type == AnimatorControllerParameterType.Bool && parameter.name == talkParameter);
            CacheJawOpenIndices();
        }

        private void CacheJawOpenIndices()
        {
            _jawOpenIndices = new int[faceRenderers?.Length ?? 0];
            bool foundJaw = false;
            for (int i = 0; i < _jawOpenIndices.Length; i++)
            {
                SkinnedMeshRenderer renderer = faceRenderers[i];
                int index = renderer != null && renderer.sharedMesh != null
                    ? renderer.sharedMesh.GetBlendShapeIndex(jawOpenBlendShape)
                    : -1;
                _jawOpenIndices[i] = index;
                foundJaw |= index >= 0;
            }

            if (!foundJaw && !_warnedMissingJaw)
            {
                _warnedMissingJaw = true;
                Debug.LogWarning($"Audio lip sync on {name} could not find the '{jawOpenBlendShape}' blend shape. Audio and talk animation will continue.", this);
            }
        }

        private void SetTalkingAnimation(bool isTalking)
        {
            _wasSpeaking = isTalking;
            if (_hasTalkParameter)
            {
                animator.SetBool(talkParameter, isTalking);
            }
        }

        private void SetJawWeight(float weight)
        {
            for (int i = 0; i < _jawOpenIndices.Length; i++)
            {
                if (faceRenderers[i] != null && _jawOpenIndices[i] >= 0)
                {
                    faceRenderers[i].SetBlendShapeWeight(_jawOpenIndices[i], weight);
                }
            }
        }

        private void ResetPresentation()
        {
            SetTalkingAnimation(false);
            _currentJawWeight = 0f;
            SetJawWeight(0f);
        }
    }
}
