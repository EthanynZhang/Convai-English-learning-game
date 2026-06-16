using System.Linq;
using UnityEngine;

namespace Game.Debate
{
    public class RefereePresentationController : MonoBehaviour
    {
        [SerializeField] private AudioSource audioSource;
        [SerializeField] private Animator animator;
        [SerializeField] private SkinnedMeshRenderer[] faceRenderers;
        [SerializeField] private string talkParameter = "Talk";
        [SerializeField] private string jawOpenBlendShape = "jawOpen";
        [SerializeField] private float mouthGain = 1.2f;
        [SerializeField] private float maximumJawWeight = 1.5f;
        [SerializeField] private float mouthSmoothing = 18f;

        private readonly float[] _audioSamples = new float[128];
        private int[] _jawOpenIndices;
        private float _currentJawWeight;
        private bool _wasSpeaking;

        private void Awake()
        {
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>(true);
            }

            if (faceRenderers == null || faceRenderers.Length == 0)
            {
                faceRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true)
                    .Where(renderer =>
                        renderer.name.EndsWith("_Head") ||
                        renderer.name == "CC_Base_Body")
                    .ToArray();
            }

            CacheJawOpenIndices();
            SetSpeaking(false);
            SetJawWeight(0f);
        }

        private void Update()
        {
            bool isSpeaking = audioSource != null && audioSource.isPlaying;
            if (_wasSpeaking != isSpeaking)
            {
                SetSpeaking(isSpeaking);
            }

            float targetWeight = isSpeaking ? MeasureJawWeight() : 0f;
            _currentJawWeight = Mathf.Lerp(
                _currentJawWeight,
                targetWeight,
                1f - Mathf.Exp(-mouthSmoothing * Time.deltaTime));
            SetJawWeight(_currentJawWeight);
        }

        private void OnDisable()
        {
            SetSpeaking(false);
            SetJawWeight(0f);
        }

        public void PlayClip(AudioClip clip)
        {
            if (audioSource == null || clip == null)
            {
                return;
            }

            audioSource.Stop();
            audioSource.PlayOneShot(clip);
            SetSpeaking(true);
        }

        private float MeasureJawWeight()
        {
            audioSource.GetOutputData(_audioSamples, 0);

            float sum = 0f;
            for (int i = 0; i < _audioSamples.Length; i++)
            {
                sum += _audioSamples[i] * _audioSamples[i];
            }

            float rootMeanSquare = Mathf.Sqrt(sum / _audioSamples.Length);
            return Mathf.Clamp01(rootMeanSquare * mouthGain) * maximumJawWeight;
        }

        private void CacheJawOpenIndices()
        {
            _jawOpenIndices = new int[faceRenderers.Length];
            for (int rendererIndex = 0; rendererIndex < faceRenderers.Length; rendererIndex++)
            {
                SkinnedMeshRenderer faceRenderer = faceRenderers[rendererIndex];
                _jawOpenIndices[rendererIndex] = faceRenderer != null && faceRenderer.sharedMesh != null
                    ? faceRenderer.sharedMesh.GetBlendShapeIndex(jawOpenBlendShape)
                    : -1;
            }
        }

        private void SetSpeaking(bool isSpeaking)
        {
            _wasSpeaking = isSpeaking;
            if (animator != null)
            {
                animator.SetBool(talkParameter, isSpeaking);
            }
        }

        private void SetJawWeight(float weight)
        {
            if (_jawOpenIndices == null)
            {
                return;
            }

            for (int rendererIndex = 0; rendererIndex < faceRenderers.Length; rendererIndex++)
            {
                if (faceRenderers[rendererIndex] != null && _jawOpenIndices[rendererIndex] >= 0)
                {
                    faceRenderers[rendererIndex].SetBlendShapeWeight(
                        _jawOpenIndices[rendererIndex],
                        weight);
                }
            }
        }
    }
}
