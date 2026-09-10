using System;
using System.Collections.Generic;
using System.Linq;

namespace Game.Debate
{
    public enum CreeiComponent
    {
        Claim,
        Reason,
        Evidence,
        Explanation,
        Impact
    }

    public enum MicroCreeiPracticeState
    {
        Drafting,
        ReadyToSubmit,
        Diagnosing,
        AwaitingCoachDecision,
        CriticalFeedbackGenerating,
        CriticalFeedbackReview,
        CoachSpeaking,
        IndependentRevision,
        CoachedRevision,
        Reassessing,
        RoundComplete,
        TechnicalError,
        Completed,
        TimedOut
    }

    public enum CoachWorkbenchAction
    {
        AskCoach,
        AcceptIssue,
        ChangeRequest,
        ContinueWithoutCoach,
        NeedExample,
        NeedMoreSuggestions,
        UseAdviceAndRevise,
        FinishPractice,
        Retry,
        TechnicalSkip
    }

    public enum WorkbenchVoiceTarget
    {
        None,
        Claim,
        Reason,
        Evidence,
        Explanation,
        Impact,
        CoachRequest
    }

    public enum CreeiRevisionKind
    {
        InitialStructure,
        IndependentRevision,
        CoachedRevision,
        TimeoutCommit
    }

    [Serializable]
    public sealed class CreeiArgumentDraft
    {
        private readonly Dictionary<CreeiComponent, string> _text = new();
        private readonly Dictionary<CreeiComponent, string> _modalities = new();

        public bool IsComplete => MissingComponents.Count == 0;
        public IReadOnlyList<CreeiComponent> MissingComponents =>
            Enum.GetValues(typeof(CreeiComponent)).Cast<CreeiComponent>()
                .Where(component => string.IsNullOrWhiteSpace(GetText(component)))
                .ToArray();

        public void SetText(CreeiComponent component, string text, string modality)
        {
            _text[component] = text ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(modality))
                _modalities[component] = modality.Trim();
        }

        public string GetText(CreeiComponent component) =>
            _text.TryGetValue(component, out string value) ? value : string.Empty;

        public string GetInputModality(CreeiComponent component) =>
            _modalities.TryGetValue(component, out string value) ? value : string.Empty;

        public void CopyFrom(CreeiArgumentSnapshot snapshot)
        {
            if (snapshot == null) return;
            foreach (CreeiComponent component in Enum.GetValues(typeof(CreeiComponent)))
                SetText(component, snapshot.GetText(component), snapshot.GetInputModality(component));
        }
    }

    [Serializable]
    public sealed class CreeiArgumentSnapshot
    {
        private readonly Dictionary<CreeiComponent, string> _text;
        private readonly Dictionary<CreeiComponent, string> _modalities;
        private readonly CreeiComponent[] _changedComponents;

        private CreeiArgumentSnapshot(
            string snapshotId,
            string parentSnapshotId,
            int roundIndex,
            CreeiRevisionKind revisionKind,
            Dictionary<CreeiComponent, string> text,
            Dictionary<CreeiComponent, string> modalities,
            CreeiComponent[] changedComponents,
            DateTimeOffset submittedAt,
            float stageElapsedSeconds,
            bool timeoutCommitted)
        {
            SnapshotId = snapshotId;
            ParentSnapshotId = parentSnapshotId;
            RoundIndex = roundIndex;
            RevisionKind = revisionKind;
            _text = text;
            _modalities = modalities;
            _changedComponents = changedComponents;
            SubmittedAt = submittedAt;
            StageElapsedSeconds = Math.Max(0f, stageElapsedSeconds);
            TimeoutCommitted = timeoutCommitted;
        }

        public string SnapshotId { get; }
        public string ParentSnapshotId { get; }
        public int RoundIndex { get; }
        public CreeiRevisionKind RevisionKind { get; }
        public IReadOnlyList<CreeiComponent> ChangedComponents =>
            Array.AsReadOnly((CreeiComponent[])_changedComponents.Clone());
        public DateTimeOffset SubmittedAt { get; }
        public float StageElapsedSeconds { get; }
        public bool TimeoutCommitted { get; }

        public string Claim => GetText(CreeiComponent.Claim);
        public string Reason => GetText(CreeiComponent.Reason);
        public string Evidence => GetText(CreeiComponent.Evidence);
        public string Explanation => GetText(CreeiComponent.Explanation);
        public string Impact => GetText(CreeiComponent.Impact);

        public string GetText(CreeiComponent component) =>
            _text.TryGetValue(component, out string value) ? value : string.Empty;

        public string GetInputModality(CreeiComponent component) =>
            _modalities.TryGetValue(component, out string value) ? value : string.Empty;

        public static CreeiArgumentSnapshot Create(
            CreeiArgumentDraft draft,
            CreeiArgumentSnapshot parent,
            int roundIndex,
            CreeiRevisionKind revisionKind,
            float stageElapsedSeconds,
            bool timeoutCommitted)
        {
            if (draft == null) throw new ArgumentNullException(nameof(draft));
            if (!draft.IsComplete)
                throw new InvalidOperationException("All five CREEI components are required.");

            Dictionary<CreeiComponent, string> text = new();
            Dictionary<CreeiComponent, string> modalities = new();
            List<CreeiComponent> changed = new();
            foreach (CreeiComponent component in Enum.GetValues(typeof(CreeiComponent)))
            {
                string value = draft.GetText(component).Trim();
                text[component] = value;
                modalities[component] = draft.GetInputModality(component);
                if (parent == null || !string.Equals(
                        parent.GetText(component), value, StringComparison.Ordinal))
                    changed.Add(component);
            }

            return new CreeiArgumentSnapshot(
                Guid.NewGuid().ToString("N"),
                parent?.SnapshotId ?? string.Empty,
                Math.Max(1, roundIndex),
                revisionKind,
                text,
                modalities,
                changed.ToArray(),
                DateTimeOffset.UtcNow,
                stageElapsedSeconds,
                timeoutCommitted);
        }
    }

    [Serializable]
    public sealed class CreeiComponentDiagnosis
    {
        public CreeiComponent Component;
        public bool CriterionMet;
        public int Severity;
        public float Confidence;
        public string IssueCode = string.Empty;
        public string EvidenceSpan = string.Empty;
        public string RecommendedNextAction = string.Empty;
    }

    [Serializable]
    public sealed class CreeiArgumentDiagnosisResult
    {
        public bool Success;
        public CreeiComponentDiagnosis[] Components = Array.Empty<CreeiComponentDiagnosis>();
        public CreeiComponent[] RankedIssues = Array.Empty<CreeiComponent>();
        public CreeiComponent? PrimaryIssue;
        public string RawJson = string.Empty;
        public string ModelVersion = string.Empty;
        public string Error = string.Empty;
    }

    public sealed class MicroCreeiPracticeSession
    {
        private readonly float _timeBudgetSeconds;
        private readonly List<CreeiArgumentSnapshot> _snapshots = new();
        private MicroCreeiPracticeState _resumeState;
        private bool _revisionPendingAssessment;
        private CreeiRevisionKind _pendingRevisionKind;

        public MicroCreeiPracticeSession(float timeBudgetSeconds)
        {
            if (timeBudgetSeconds <= 0f)
                throw new ArgumentOutOfRangeException(nameof(timeBudgetSeconds));
            _timeBudgetSeconds = timeBudgetSeconds;
            RemainingSeconds = timeBudgetSeconds;
        }

        public MicroCreeiPracticeState CurrentState { get; private set; } =
            MicroCreeiPracticeState.Drafting;
        public float RemainingSeconds { get; private set; }
        public int CurrentRoundIndex { get; private set; } = 1;
        public int CompletedRoundCount { get; private set; }
        public CreeiComponent? ActiveComponent { get; private set; }
        public CreeiComponent? CurrentCoachFocus { get; private set; }
        public CreeiArgumentDraft CurrentDraft { get; } = new();
        public CreeiArgumentSnapshot LastCommittedSnapshot { get; private set; }
        public CreeiArgumentDiagnosisResult LatestDiagnosis { get; private set; }
        public IReadOnlyList<CreeiArgumentSnapshot> Snapshots => _snapshots;
        public int SnapshotCount => _snapshots.Count;
        public int TimeoutTriggerCount { get; private set; }
        public bool CanFinishPractice => CompletedRoundCount >= 1;
        public IReadOnlyList<CreeiComponent> ChangedComponents =>
            CalculateChangedComponents();

        public void Begin()
        {
            RemainingSeconds = _timeBudgetSeconds;
            CurrentRoundIndex = 1;
            CompletedRoundCount = 0;
            TimeoutTriggerCount = 0;
            _snapshots.Clear();
            CurrentState = MicroCreeiPracticeState.Drafting;
        }

        public void SetActiveComponent(CreeiComponent component)
        {
            if (!IsEditableState() && CurrentState !=
                    MicroCreeiPracticeState.AwaitingCoachDecision)
                throw new InvalidOperationException(
                    $"A CREEI focus cannot be selected while state is {CurrentState}.");
            ActiveComponent = component;
        }

        public void SetComponentText(CreeiComponent component, string text, string modality)
        {
            RequireEditable();
            CurrentDraft.SetText(component, text, modality);
            if (LastCommittedSnapshot == null &&
                CurrentState is MicroCreeiPracticeState.Drafting or
                    MicroCreeiPracticeState.ReadyToSubmit)
                CurrentState = CurrentDraft.IsComplete
                    ? MicroCreeiPracticeState.ReadyToSubmit
                    : MicroCreeiPracticeState.Drafting;
        }

        public CreeiArgumentSnapshot SubmitArgumentSnapshot()
        {
            CreeiRevisionKind kind;
            switch (CurrentState)
            {
                case MicroCreeiPracticeState.ReadyToSubmit:
                    kind = CreeiRevisionKind.InitialStructure;
                    break;
                case MicroCreeiPracticeState.IndependentRevision:
                    kind = CreeiRevisionKind.IndependentRevision;
                    break;
                case MicroCreeiPracticeState.CoachedRevision:
                    kind = CreeiRevisionKind.CoachedRevision;
                    break;
                default:
                    throw new InvalidOperationException(
                        $"A snapshot cannot be submitted while state is {CurrentState}.");
            }

            CreeiArgumentSnapshot snapshot = CreeiArgumentSnapshot.Create(
                CurrentDraft,
                LastCommittedSnapshot,
                CurrentRoundIndex,
                kind,
                _timeBudgetSeconds - RemainingSeconds,
                false);
            if (LastCommittedSnapshot != null && snapshot.ChangedComponents.Count == 0)
                throw new InvalidOperationException("Revise at least one CREEI component before submitting.");
            LastCommittedSnapshot = snapshot;
            _snapshots.Add(snapshot);
            _revisionPendingAssessment = kind != CreeiRevisionKind.InitialStructure;
            _pendingRevisionKind = kind;
            CurrentState = kind == CreeiRevisionKind.InitialStructure
                ? MicroCreeiPracticeState.Diagnosing
                : MicroCreeiPracticeState.Reassessing;
            return snapshot;
        }

        public void ApplyDiagnosis(CreeiArgumentDiagnosisResult diagnosis)
        {
            if (diagnosis == null || !diagnosis.Success)
                throw new InvalidOperationException("A successful diagnosis is required.");
            if (CurrentState == MicroCreeiPracticeState.Diagnosing)
            {
                LatestDiagnosis = diagnosis;
                CurrentState = MicroCreeiPracticeState.AwaitingCoachDecision;
                return;
            }
            if (CurrentState != MicroCreeiPracticeState.Reassessing)
                throw new InvalidOperationException(
                    $"Diagnosis cannot be applied while state is {CurrentState}.");
            LatestDiagnosis = diagnosis;
            if (_revisionPendingAssessment)
            {
                CompletedRoundCount++;
                _revisionPendingAssessment = false;
                CurrentState = _pendingRevisionKind == CreeiRevisionKind.IndependentRevision
                    ? MicroCreeiPracticeState.IndependentRevision
                    : MicroCreeiPracticeState.CoachedRevision;
            }
            else
            {
                CurrentState = MicroCreeiPracticeState.AwaitingCoachDecision;
            }
        }

        public void BeginCriticalFeedback()
        {
            Require(MicroCreeiPracticeState.AwaitingCoachDecision);
            CurrentState = MicroCreeiPracticeState.CriticalFeedbackGenerating;
        }

        public void PresentCriticalFeedback()
        {
            Require(MicroCreeiPracticeState.CriticalFeedbackGenerating);
            CurrentState = MicroCreeiPracticeState.CriticalFeedbackReview;
        }

        public void RegenerateCriticalFeedback()
        {
            Require(MicroCreeiPracticeState.CriticalFeedbackReview);
            CurrentState = MicroCreeiPracticeState.CriticalFeedbackGenerating;
        }

        public void StartCoach(CreeiComponent focus)
        {
            if (CurrentState is not (MicroCreeiPracticeState.AwaitingCoachDecision or
                MicroCreeiPracticeState.CriticalFeedbackGenerating or
                MicroCreeiPracticeState.CriticalFeedbackReview))
                throw new InvalidOperationException(
                    $"Coach feedback cannot start while state is {CurrentState}.");
            CurrentCoachFocus = focus;
            CurrentState = MicroCreeiPracticeState.CoachSpeaking;
        }

        public void CompleteCoachSpeech()
        {
            Require(MicroCreeiPracticeState.CoachSpeaking);
            CurrentState = MicroCreeiPracticeState.CoachedRevision;
        }

        public void StartCoachFollowUp(CreeiComponent focus)
        {
            if (CurrentState is not (MicroCreeiPracticeState.CoachedRevision or
                MicroCreeiPracticeState.IndependentRevision))
                throw new InvalidOperationException(
                    $"Coach follow-up cannot start while state is {CurrentState}.");
            CurrentCoachFocus = focus;
            CurrentState = MicroCreeiPracticeState.CoachSpeaking;
        }

        public void ContinueWithoutCoach()
        {
            if (CurrentState is not (MicroCreeiPracticeState.AwaitingCoachDecision or
                MicroCreeiPracticeState.CriticalFeedbackReview))
                throw new InvalidOperationException(
                    $"Coach support cannot be skipped while state is {CurrentState}.");
            CurrentCoachFocus = null;
            CurrentState = MicroCreeiPracticeState.IndependentRevision;
        }

        public void BeginNextRound()
        {
            Require(MicroCreeiPracticeState.RoundComplete);
            CurrentRoundIndex++;
            CurrentCoachFocus = null;
            CurrentState = MicroCreeiPracticeState.AwaitingCoachDecision;
        }

        public void FinishPractice()
        {
            if (!CanFinishPractice)
                throw new InvalidOperationException("Complete at least one coaching round first.");
            CurrentState = MicroCreeiPracticeState.Completed;
        }

        public void Tick(float deltaSeconds)
        {
            if (deltaSeconds <= 0f || CurrentState == MicroCreeiPracticeState.TechnicalError ||
                CurrentState is MicroCreeiPracticeState.Completed or MicroCreeiPracticeState.TimedOut)
                return;
            RemainingSeconds = Math.Max(0f, RemainingSeconds - deltaSeconds);
            if (RemainingSeconds > 0f || TimeoutTriggerCount > 0) return;
            TimeoutTriggerCount++;
            CommitTimeoutDraftWhenEligible();
            CurrentState = MicroCreeiPracticeState.TimedOut;
        }

        public void EnterTechnicalError()
        {
            if (CurrentState == MicroCreeiPracticeState.TechnicalError) return;
            _resumeState = CurrentState;
            CurrentState = MicroCreeiPracticeState.TechnicalError;
        }

        public void RetryTechnicalOperation()
        {
            Require(MicroCreeiPracticeState.TechnicalError);
            CurrentState = _resumeState;
        }

        private void CommitTimeoutDraftWhenEligible()
        {
            if (!CurrentDraft.IsComplete) return;
            CreeiArgumentSnapshot snapshot = CreeiArgumentSnapshot.Create(
                CurrentDraft,
                LastCommittedSnapshot,
                CurrentRoundIndex,
                CreeiRevisionKind.TimeoutCommit,
                _timeBudgetSeconds,
                true);
            if (LastCommittedSnapshot == null || snapshot.ChangedComponents.Count > 0)
            {
                LastCommittedSnapshot = snapshot;
                _snapshots.Add(snapshot);
            }
        }

        private IReadOnlyList<CreeiComponent> CalculateChangedComponents()
        {
            List<CreeiComponent> changed = new();
            foreach (CreeiComponent component in Enum.GetValues(typeof(CreeiComponent)))
            {
                if (LastCommittedSnapshot == null || !string.Equals(
                        CurrentDraft.GetText(component).Trim(),
                        LastCommittedSnapshot.GetText(component),
                        StringComparison.Ordinal))
                    changed.Add(component);
            }
            return changed.AsReadOnly();
        }

        private void RequireEditable()
        {
            if (IsEditableState()) return;
            throw new InvalidOperationException(
                $"CREEI cards are locked while state is {CurrentState}.");
        }

        private bool IsEditableState() => CurrentState is
            MicroCreeiPracticeState.Drafting or
            MicroCreeiPracticeState.ReadyToSubmit or
            MicroCreeiPracticeState.IndependentRevision or
            MicroCreeiPracticeState.CoachedRevision or
            MicroCreeiPracticeState.CoachSpeaking;

        private void Require(MicroCreeiPracticeState expected)
        {
            if (CurrentState != expected)
                throw new InvalidOperationException(
                    $"Transition requires {expected}, but current state is {CurrentState}.");
        }
    }
}
