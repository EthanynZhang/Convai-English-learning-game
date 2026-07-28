using System;
using System.Collections;
using System.Linq;

namespace Game.Debate
{
    [Serializable]
    public sealed class CreeiArgumentDiagnosisRequest
    {
        public string ParticipantId = string.Empty;
        public string Stage = "MicroCreeiWorkbench";
        public string TopicId = string.Empty;
        public string Topic = string.Empty;
        public string LearnerSide = string.Empty;
        public int RoundIndex;
        public CreeiArgumentSnapshot CurrentSnapshot;
        public CreeiArgumentSnapshot PreviousSnapshot;
    }

    public interface ICreeiArgumentDiagnosisEngine
    {
        IEnumerator Diagnose(
            CreeiArgumentDiagnosisRequest request,
            Action<CreeiArgumentDiagnosisResult> onComplete);
        void Cancel();
    }

    public sealed class StructuredCreeiArgumentDiagnosisEngine : ICreeiArgumentDiagnosisEngine
    {
        private readonly ICoachDiagnosisEngine _inner;

        public StructuredCreeiArgumentDiagnosisEngine(ICoachDiagnosisEngine inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public IEnumerator Diagnose(
            CreeiArgumentDiagnosisRequest request,
            Action<CreeiArgumentDiagnosisResult> onComplete)
        {
            request ??= new CreeiArgumentDiagnosisRequest();
            CoachDiagnosisResult result = null;
            yield return _inner.Diagnose(new CoachDiagnosisRequest
            {
                ParticipantId = request.ParticipantId,
                Stage = request.Stage,
                TopicId = request.TopicId,
                PracticeCycleId = request.RoundIndex,
                Topic = request.Topic,
                LearnerSide = request.LearnerSide,
                CurrentCreeiSnapshot = request.CurrentSnapshot,
                PreviousCreeiSnapshot = request.PreviousSnapshot,
                SelectedStrategy = "Any"
            }, value => result = value);
            onComplete?.Invoke(Convert(result));
        }

        public void Cancel() => _inner.Cancel();

        public static CreeiArgumentDiagnosisResult Convert(CoachDiagnosisResult source)
        {
            if (source == null || !source.Success)
                return new CreeiArgumentDiagnosisResult
                {
                    Success = false,
                    Error = source?.Error ?? "Diagnosis failed."
                };
            CreeiComponent? primary = Enum.TryParse(
                source.RecommendedFocus, true, out CreeiComponent parsed)
                ? parsed
                : source.ComponentDiagnoses
                    .Where(item => !item.CriterionMet)
                    .OrderByDescending(item => item.Severity)
                    .Select(item => (CreeiComponent?)item.Component)
                    .FirstOrDefault();
            CreeiComponent[] ranked = source.RankedSuggestions
                .Select(item => Enum.TryParse(item.Focus, true, out CreeiComponent focus)
                    ? (CreeiComponent?)focus
                    : null)
                .Where(item => item.HasValue)
                .Select(item => item.Value)
                .Distinct()
                .Take(3)
                .ToArray();
            if (ranked.Length == 0 && primary.HasValue) ranked = new[] { primary.Value };
            return new CreeiArgumentDiagnosisResult
            {
                Success = true,
                Components = source.ComponentDiagnoses ?? Array.Empty<CreeiComponentDiagnosis>(),
                RankedIssues = ranked,
                PrimaryIssue = primary,
                RawJson = source.RawJson,
                ModelVersion = source.ModelVersion
            };
        }
    }
}
