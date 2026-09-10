using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Game.Tests.EditMode
{
    public sealed class MicroCreeiPracticeTests
    {
        private static Type RuntimeType(string name) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(assembly =>
                {
                    try { return assembly.GetTypes(); }
                    catch { return Array.Empty<Type>(); }
                })
                .FirstOrDefault(type => type.Namespace == "Game.Debate" && type.Name == name);

        private static object NewSession(float seconds = 600f) =>
            Activator.CreateInstance(RuntimeType("MicroCreeiPracticeSession"), seconds);

        private static object Component(string name) =>
            Enum.Parse(RuntimeType("CreeiComponent"), name);

        private static object Call(object target, string method, params object[] args)
        {
            MethodInfo info = target.GetType().GetMethod(method);
            Assert.IsNotNull(info, target.GetType().Name + " must expose " + method + ".");
            return info.Invoke(target, args);
        }

        private static string Property(object target, string name)
        {
            PropertyInfo property = target.GetType().GetProperty(name);
            if (property != null) return property.GetValue(target)?.ToString() ?? string.Empty;
            FieldInfo field = target.GetType().GetField(name);
            Assert.IsNotNull(field);
            return field.GetValue(target)?.ToString() ?? string.Empty;
        }

        private static void FillDraft(object session, string suffix = "v1")
        {
            foreach (string part in new[] { "Claim", "Reason", "Evidence", "Explanation", "Impact" })
                Call(session, "SetComponentText", Component(part), part + " " + suffix, "keyboard");
        }

        private static object Diagnosis(string primary = "Evidence")
        {
            Type type = RuntimeType("CreeiArgumentDiagnosisResult");
            object result = Activator.CreateInstance(type);
            type.GetField("Success").SetValue(result, true);
            type.GetField("PrimaryIssue").SetValue(result, Component(primary));
            Array ranked = Array.CreateInstance(RuntimeType("CreeiComponent"), 1);
            ranked.SetValue(Component(primary), 0);
            type.GetField("RankedIssues").SetValue(result, ranked);
            return result;
        }

        private static string PromptFor(string purpose)
        {
            Type requestType = RuntimeType("CoachFeedbackRequest");
            Type purposeType = RuntimeType("CoachFeedbackPurpose");
            object request = Activator.CreateInstance(requestType);
            requestType.GetField("Purpose").SetValue(request, Enum.Parse(purposeType, purpose));
            requestType.GetField("Topic").SetValue(request,
                "Individual practice or interaction: which better supports speaking?");
            requestType.GetField("PlayerSide").SetValue(request,
                "Interaction better supports speaking.");
            requestType.GetField("LearnerRequest").SetValue(request,
                "Can you give me evidence and an example in China?");
            requestType.GetField("AcceptedCriticalFeedback").SetValue(request,
                "The evidence is too general.");
            return ((string)RuntimeType("DebateCoachFeedbackGenerator")
                .GetMethod("BuildPrompt", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { request })).ToLowerInvariant();
        }

        [Test]
        public void InitialDiagnosisRoutesDirectlyToCoachOpportunityWithoutLeo()
        {
            object session = NewSession();
            Call(session, "Begin");
            FillDraft(session);
            Call(session, "SubmitArgumentSnapshot");
            Call(session, "ApplyDiagnosis", Diagnosis());

            Assert.AreEqual("AwaitingCoachDecision", Property(session, "CurrentState"));
            Assert.IsNull(session.GetType().GetProperty("CurrentChallengeFocus"));
            Assert.IsNull(session.GetType().GetMethod("StartChallenge"));
        }

        [Test]
        public void SharedCriticalFeedbackRequiresReviewBeforeCoachSpeech()
        {
            object session = NewSession();
            Call(session, "Begin");
            FillDraft(session);
            Call(session, "SubmitArgumentSnapshot");
            Call(session, "ApplyDiagnosis", Diagnosis("Explanation"));
            Call(session, "BeginCriticalFeedback");
            Assert.AreEqual("CriticalFeedbackGenerating", Property(session, "CurrentState"));
            Call(session, "PresentCriticalFeedback");
            Assert.AreEqual("CriticalFeedbackReview", Property(session, "CurrentState"));
            Call(session, "StartCoach", Component("Explanation"));
            Assert.AreEqual("CoachSpeaking", Property(session, "CurrentState"));
        }

        [Test]
        public void SkippingCoachStillRequiresAndCompletesARevision()
        {
            object session = NewSession();
            Call(session, "Begin");
            FillDraft(session);
            Call(session, "SubmitArgumentSnapshot");
            Call(session, "ApplyDiagnosis", Diagnosis());
            Call(session, "ContinueWithoutCoach");
            Assert.AreEqual("IndependentRevision", Property(session, "CurrentState"));
            Call(session, "SetComponentText", Component("Evidence"), "Evidence v2", "keyboard");
            Call(session, "SubmitArgumentSnapshot");
            Call(session, "ApplyDiagnosis", Diagnosis("Explanation"));
            Assert.AreEqual("IndependentRevision", Property(session, "CurrentState"));
            Assert.AreEqual("1", Property(session, "CompletedRoundCount"));
            Assert.AreEqual("2", Property(session, "SnapshotCount"));
        }

        [Test]
        public void LearnerLedCanAskRepeatedlyBeforeSubmittingRevision()
        {
            object session = NewSession();
            Call(session, "Begin");
            FillDraft(session);
            Call(session, "SubmitArgumentSnapshot");
            Call(session, "ApplyDiagnosis", Diagnosis());
            Call(session, "StartCoach", Component("Evidence"));
            Call(session, "CompleteCoachSpeech");
            Assert.AreEqual("CoachedRevision", Property(session, "CurrentState"));
            Call(session, "StartCoachFollowUp", Component("Explanation"));
            Assert.AreEqual("CoachSpeaking", Property(session, "CurrentState"));
            Call(session, "CompleteCoachSpeech");
            Assert.AreEqual("CoachedRevision", Property(session, "CurrentState"));
        }

        [Test]
        public void WorkbenchRoutesDecisionsThroughTypedCoachActionsOnly()
        {
            Type view = RuntimeType("MicroCreeiWorkbenchView");
            Type action = RuntimeType("CoachWorkbenchAction");
            Assert.IsNotNull(action);
            Assert.IsNotNull(view.GetEvent("CoachActionRequested"));
            Assert.IsNull(view.GetEvent("ChallengeDecisionRequested"));
            Assert.IsNull(view.GetEvent("CoachDecisionRequested"));
            CollectionAssert.IsSubsetOf(new[]
            {
                "AskCoach", "AcceptIssue", "ChangeRequest",
                "ContinueWithoutCoach", "NeedExample", "NeedMoreSuggestions",
                "UseAdviceAndRevise"
            }, Enum.GetNames(action));
            string controller = File.ReadAllText(
                "Assets/Game/Scripts/MicroCreeiPracticeController.cs");
            StringAssert.Contains("case CoachWorkbenchAction.AcceptIssue:", controller);
            StringAssert.Contains("AcceptCriticalFeedback(\"critical_feedback_accepted\")",
                controller);
            StringAssert.DoesNotContain("ChallengeDecisionRequested", controller);
        }

        [Test]
        public void WorkbenchBuildsTheCoachOnlyControlSet()
        {
            GameObject canvasObject = new("Canvas", typeof(RectTransform), typeof(Canvas));
            GameObject viewObject = new("Coach-only Workbench");
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Component view = viewObject.AddComponent(RuntimeType("MicroCreeiWorkbenchView"));
                view.GetType().GetMethod("Build").Invoke(view, new object[] { canvas });
                string[] names = canvasObject.GetComponentsInChildren<Button>(true)
                    .Select(button => button.gameObject.name).ToArray();
                CollectionAssert.IsSubsetOf(new[]
                {
                    "Ask Coach", "Accept Issue", "Change Request",
                    "Need an Example?", "Need More Suggestions?", "Finish Practice 1"
                }, names);
                CollectionAssert.DoesNotContain(names, "Challenge Me");
                CollectionAssert.DoesNotContain(names, "Next Challenge");
                CollectionAssert.DoesNotContain(names, "Continue to Advice");
                CollectionAssert.DoesNotContain(names, "Next Coaching Round");
                Assert.IsFalse(names.Any(name => name.StartsWith("Voice ",
                    StringComparison.Ordinal)));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void CoachFeedbackPurposesDeclareTheWorkedExampleContract()
        {
            Type purpose = RuntimeType("CoachFeedbackPurpose");
            CollectionAssert.AreEquivalent(new[]
            {
                "LearnerSocratic", "CriticalIssue", "TargetedAdvice", "Example",
                "AdditionalSuggestion", "DirectAdvice", "ConversationalFollowUp",
                "CreeiModelAnswer"
            }, Enum.GetNames(purpose));
            Type request = RuntimeType("CoachFeedbackRequest");
            Assert.IsNotNull(request.GetField("Purpose"));
            Assert.IsNotNull(request.GetField("ConversationHistory"));
            Assert.IsNotNull(request.GetField("AcceptedCriticalFeedback"));
            Type result = RuntimeType("CoachFeedbackResult");
            Assert.IsNotNull(RuntimeType("CreeiModelExampleSet"));
            Assert.IsNotNull(result.GetField("CreeiModelExamples"));
        }

        [Test]
        public void PurposePromptsEnforceSentenceAndScaffoldingRules()
        {
            string learner = PromptFor("LearnerSocratic");
            StringAssert.Contains("3 to 6", learner);
            StringAssert.Contains("open-ended", learner);
            StringAssert.Contains("explicitly asks", learner);
            string critical = PromptFor("CriticalIssue");
            StringAssert.Contains("1 to 3", critical);
            StringAssert.Contains("do not give advice", critical);
            StringAssert.DoesNotContain("accepted critical feedback:", critical);
            string advice = PromptFor("TargetedAdvice");
            StringAssert.Contains("4 to 6", advice);
            StringAssert.Contains("accepted critical feedback", advice);
            string direct = PromptFor("DirectAdvice");
            StringAssert.Contains("4 to 6", direct);
            StringAssert.Contains("directly", direct);
            StringAssert.DoesNotContain("accepted critical feedback:", direct);
            string followUp = PromptFor("ConversationalFollowUp");
            StringAssert.Contains("3 to 6", followUp);
            StringAssert.Contains("answer", followUp);
            StringAssert.Contains("one adaptable example sentence", PromptFor("Example"));
            StringAssert.Contains("do not repeat", PromptFor("AdditionalSuggestion"));
            string model = PromptFor("CreeiModelAnswer");
            foreach (string component in new[]
                     {
                         "claim", "reason", "evidence", "explanation", "impact"
                     })
                StringAssert.Contains(component, model);
            StringAssert.Contains("creei_examples", model);
            StringAssert.Contains("one coherent argument", model);
            StringAssert.Contains("individual practice or interaction", model);
            StringAssert.Contains("interaction better supports speaking", model);
            StringAssert.DoesNotContain("can you give me evidence", model);
            StringAssert.DoesNotContain("accepted critical feedback", model);
            StringAssert.DoesNotContain("current_creei_argument", model);
        }

        [Test]
        public void CreeiModelExamplesValidateAndProduceCanonicalHistoryAndSpeech()
        {
            Type setType = RuntimeType("CreeiModelExampleSet");
            object examples = Activator.CreateInstance(setType);
            setType.GetField("Claim").SetValue(examples,
                "Interaction with other people is the best way to improve English speaking.");
            setType.GetField("Reason").SetValue(examples,
                "It gives learners regular practice in responding to different ideas.");
            setType.GetField("Evidence").SetValue(examples,
                "For example, classmates can ask unexpected questions during a group discussion.");
            setType.GetField("Explanation").SetValue(examples,
                "Answering those questions connects speaking practice with real communication.");
            setType.GetField("Impact").SetValue(examples,
                "As a result, learners become more confident in conversations outside class.");

            Type generator = RuntimeType("DebateCoachFeedbackGenerator");
            MethodInfo valid = generator.GetMethod("IsValidCreeiModelExampleSet",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsTrue((bool)valid.Invoke(null, new[] { examples }));

            string history = (string)generator.GetMethod("BuildCreeiModelHistoryText",
                    BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { examples });
            string speech = (string)generator.GetMethod("BuildCreeiModelSpeechText",
                    BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { examples });
            StringAssert.StartsWith("Claim: Interaction", history);
            StringAssert.Contains("\nReason:", history);
            StringAssert.Contains("\nImpact:", history);
            StringAssert.StartsWith("Claim. Interaction", speech);
            Assert.Less(speech.IndexOf("Claim.", StringComparison.Ordinal),
                speech.IndexOf("Reason.", StringComparison.Ordinal));
            Assert.Less(speech.IndexOf("Evidence.", StringComparison.Ordinal),
                speech.IndexOf("Explanation.", StringComparison.Ordinal));
            Assert.Less(speech.IndexOf("Explanation.", StringComparison.Ordinal),
                speech.IndexOf("Impact.", StringComparison.Ordinal));

            setType.GetField("Impact").SetValue(examples, string.Empty);
            Assert.IsFalse((bool)valid.Invoke(null, new[] { examples }));
            setType.GetField("Impact").SetValue(examples,
                "You should explain why this matters.");
            Assert.IsFalse((bool)valid.Invoke(null, new[] { examples }));
            setType.GetField("Impact").SetValue(examples,
                "This matters. It improves confidence.");
            Assert.IsFalse((bool)valid.Invoke(null, new[] { examples }));
            setType.GetField("Impact").SetValue(examples, "12345.");
            Assert.IsFalse((bool)valid.Invoke(null, new[] { examples }));
        }

        [Test]
        public void CreeiModelParserReadsNestedJsonAndLabelledFallback()
        {
            Type generator = RuntimeType("DebateCoachFeedbackGenerator");
            MethodInfo parse = generator.GetMethod("ParseResultJson",
                BindingFlags.NonPublic | BindingFlags.Static);
            JObject nested = JObject.Parse(@"{
                'feedback_type':'CREEI Model Answer',
                'feedback_level':'Level2',
                'creei_examples':{
                    'claim':'Interaction is the best way to improve speaking.',
                    'reason':'It requires learners to respond to other people.',
                    'evidence':'For example, classmates can ask unexpected questions.',
                    'explanation':'These questions turn language knowledge into real communication.',
                    'impact':'Learners become more confident outside the classroom.'
                },
                'next_action':'revise',
                'target_success_criterion':'five connected parts'
            }");
            object nestedResult = parse.Invoke(null, new object[] { nested });
            object nestedExamples = nestedResult.GetType().GetField("CreeiModelExamples")
                .GetValue(nestedResult);
            Assert.AreEqual("Interaction is the best way to improve speaking.",
                nestedExamples.GetType().GetField("Claim").GetValue(nestedExamples));
            StringAssert.Contains("Claim: Interaction",
                nestedResult.GetType().GetField("FeedbackText").GetValue(nestedResult).ToString());

            JObject labelled = new()
            {
                ["feedback_text"] =
                    "Claim: Interaction is the best way to improve speaking.\n" +
                    "Reason: It requires learners to respond to other people.\n" +
                    "Evidence: For example, classmates can ask unexpected questions.\n" +
                    "Explanation: These questions turn language knowledge into real communication.\n" +
                    "Impact: Learners become more confident outside the classroom."
            };
            object fallbackResult = parse.Invoke(null, new object[] { labelled });
            object fallbackExamples = fallbackResult.GetType().GetField("CreeiModelExamples")
                .GetValue(fallbackResult);
            Assert.AreEqual("Learners become more confident outside the classroom.",
                fallbackExamples.GetType().GetField("Impact").GetValue(fallbackExamples));

            nested["creei_examples"]["impact"] =
                "学习者 Learners become more confident outside the classroom.";
            object mixedLanguageResult = parse.Invoke(null, new object[] { nested });
            object mixedLanguageExamples = mixedLanguageResult.GetType()
                .GetField("CreeiModelExamples").GetValue(mixedLanguageResult);
            MethodInfo valid = generator.GetMethod("IsValidCreeiModelExampleSet",
                BindingFlags.Public | BindingFlags.Static);
            Assert.IsFalse((bool)valid.Invoke(null, new[] { mixedLanguageExamples }));

            labelled["feedback_text"] +=
                "\nImpact: A repeated impact must invalidate the result.";
            object duplicateResult = parse.Invoke(null, new object[] { labelled });
            object duplicateExamples = duplicateResult.GetType()
                .GetField("CreeiModelExamples").GetValue(duplicateResult);
            Assert.IsFalse((bool)valid.Invoke(null, new[] { duplicateExamples }));
        }

        [TestCase("LearnerSocratic", "One. Two? Three!", true)]
        [TestCase("LearnerSocratic", "One. Two.", false)]
        [TestCase("CriticalIssue", "One issue.", true)]
        [TestCase("CriticalIssue", "One. Two. Three. Four.", false)]
        [TestCase("TargetedAdvice", "One. Two. Three. Four.", true)]
        [TestCase("TargetedAdvice", "One. Two. Three.", false)]
        [TestCase("DirectAdvice", "One. Two. Three. Four.", true)]
        [TestCase("DirectAdvice", "One. Two. Three.", false)]
        [TestCase("ConversationalFollowUp", "One. Two. Three.", true)]
        [TestCase("ConversationalFollowUp", "One. Two.", false)]
        [TestCase("AdditionalSuggestion", "One. Two.", true)]
        public void PurposeFeedbackValidatorEnforcesSentenceCounts(
            string purpose,
            string feedback,
            bool expected)
        {
            Type purposeType = RuntimeType("CoachFeedbackPurpose");
            bool actual = (bool)RuntimeType("DebateCoachFeedbackGenerator")
                .GetMethod("IsPurposeFeedbackText", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { Enum.Parse(purposeType, purpose), feedback });
            Assert.AreEqual(expected, actual);
        }

        [Test]
        public void ConversationHistoryIsIncludedWithoutConditionMetadata()
        {
            Type requestType = RuntimeType("CoachFeedbackRequest");
            Type purposeType = RuntimeType("CoachFeedbackPurpose");
            Type turnType = RuntimeType("CoachConversationTurn");
            object request = Activator.CreateInstance(requestType);
            requestType.GetField("Purpose").SetValue(request,
                Enum.Parse(purposeType, "LearnerSocratic"));
            Array turns = Array.CreateInstance(turnType, 1);
            object turn = Activator.CreateInstance(turnType);
            turnType.GetField("TurnIndex").SetValue(turn, 1);
            turnType.GetField("LearnerRequest").SetValue(turn, "Why is my evidence weak?");
            turnType.GetField("CoachResponse").SetValue(turn, "What makes it trustworthy?");
            turns.SetValue(turn, 0);
            requestType.GetField("ConversationHistory").SetValue(request, turns);
            string prompt = (string)RuntimeType("DebateCoachFeedbackGenerator")
                .GetMethod("BuildPrompt", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { request });
            StringAssert.Contains("Why is my evidence weak?", prompt);
            StringAssert.Contains("What makes it trustworthy?", prompt);
            StringAssert.DoesNotContain("condition:", prompt.ToLowerInvariant());
        }

        [Test]
        public void AllModesRouteFreeVoiceFollowUpsAfterFeedback()
        {
            MethodInfo route = RuntimeType("MicroCreeiPracticeController")
                .GetMethod("ShouldRouteVoiceToLearnerRequest",
                    BindingFlags.Public | BindingFlags.Static);
            Type state = RuntimeType("MicroCreeiPracticeState");
            Type mode = RuntimeType("CoachOrchestrationMode");
            object revision = Enum.Parse(state, "CoachedRevision");
            Assert.IsTrue((bool)route.Invoke(null, new[]
            {
                revision, Enum.Parse(mode, "LearnerLed"), true, false
            }));
            Assert.IsTrue((bool)route.Invoke(null, new[]
            {
                revision, Enum.Parse(mode, "SharedControl"), true, false
            }));
            Assert.IsTrue((bool)route.Invoke(null, new[]
            {
                revision, Enum.Parse(mode, "AiLed"), true, false
            }));
        }

        [Test]
        public void Scene04EnglishSanitizerRemovesNonEnglishAndDecorativeSymbols()
        {
            Type sanitizer = RuntimeType("EnglishLlmInputSanitizer");
            Assert.IsNotNull(sanitizer);
            MethodInfo sanitize = sanitizer.GetMethod("Sanitize",
                BindingFlags.Public | BindingFlags.Static, null,
                new[] { typeof(string) }, null);
            Assert.IsNotNull(sanitize);
            Assert.AreEqual("individual practice.", sanitize.Invoke(null,
                new object[] { "心理 individual practice。◆ [>]" }));
            Assert.AreEqual("It's useful in 2026, isn't it?", sanitize.Invoke(null,
                new object[] { "It’s useful in 2026， isn’t it？" }));
            Assert.AreEqual(string.Empty, sanitize.Invoke(null, new object[] { "你好◆" }));
        }

        [Test]
        public void Scene04LocalDiagnosisIsConditionBlindAndFindsRepeatedCards()
        {
            object session = NewSession();
            Call(session, "Begin");
            foreach (string part in new[] { "Claim", "Reason", "Evidence", "Explanation", "Impact" })
                Call(session, "SetComponentText", Component(part),
                    "Individual practice is more beneficial for speaking.", "keyboard");
            object snapshot = Call(session, "SubmitArgumentSnapshot");

            Type requestType = RuntimeType("CreeiArgumentDiagnosisRequest");
            object request = Activator.CreateInstance(requestType);
            requestType.GetField("Topic").SetValue(request,
                "Individual practice or interaction with others?");
            requestType.GetField("LearnerSide").SetValue(request,
                "Interaction with others is more beneficial.");
            requestType.GetField("CurrentSnapshot").SetValue(request, snapshot);

            Type engine = RuntimeType("Scene04LocalCreeiDiagnosisEngine");
            Assert.IsNotNull(engine);
            object result = engine.GetMethod("Evaluate", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { request });
            Assert.AreEqual("True", Property(result, "Success"));
            Assert.AreEqual("coach-diagnosis-local-v3", Property(result, "ModelVersion"));
            Assert.AreEqual("Claim", Property(result, "PrimaryIssue"));
            Array components = (Array)result.GetType().GetField("Components").GetValue(result);
            Assert.AreEqual(5, components.Length);
        }

        [Test]
        public void PurposePromptSendsOnlyTheLatestSixConversationTurns()
        {
            Type requestType = RuntimeType("CoachFeedbackRequest");
            Type purposeType = RuntimeType("CoachFeedbackPurpose");
            Type turnType = RuntimeType("CoachConversationTurn");
            object request = Activator.CreateInstance(requestType);
            requestType.GetField("Purpose").SetValue(request,
                Enum.Parse(purposeType, "ConversationalFollowUp"));
            Array turns = Array.CreateInstance(turnType, 8);
            for (int index = 0; index < turns.Length; index++)
            {
                object turn = Activator.CreateInstance(turnType);
                turnType.GetField("TurnIndex").SetValue(turn, index + 1);
                turnType.GetField("LearnerRequest").SetValue(turn, "Question " + (index + 1));
                turnType.GetField("CoachResponse").SetValue(turn, "Answer " + (index + 1));
                turns.SetValue(turn, index);
            }
            requestType.GetField("ConversationHistory").SetValue(request, turns);
            string prompt = (string)RuntimeType("DebateCoachFeedbackGenerator")
                .GetMethod("BuildPrompt", BindingFlags.Public | BindingFlags.Static)
                .Invoke(null, new[] { request });
            StringAssert.DoesNotContain("Question 1", prompt);
            StringAssert.DoesNotContain("Question 2", prompt);
            StringAssert.Contains("Question 3", prompt);
            StringAssert.Contains("Question 8", prompt);
        }

        [Test]
        public void Scene04UsesLocalDiagnosisAndNewModelVersions()
        {
            string controller = File.ReadAllText(
                "Assets/Game/Scripts/ThreeStageDebatePracticeController.cs");
            string config = File.ReadAllText(
                "Assets/Game/Scripts/CoachOrchestrationTypes.cs");
            StringAssert.Contains("new Scene04LocalCreeiDiagnosisEngine", controller);
            StringAssert.DoesNotContain("_structuredDiagnosisEngine = new StructuredCreeiArgumentDiagnosisEngine", controller);
            StringAssert.Contains("three-mode-v4", config);
            StringAssert.Contains("coach-diagnosis-local-v3", config);
            StringAssert.Contains("coach-feedback-v5", config);
        }

        [Test]
        public void WorkbenchFixedLabelsAndCardStatesUseAsciiOnly()
        {
            string view = File.ReadAllText(
                "Assets/Game/Scripts/MicroCreeiWorkbenchView.cs");
            StringAssert.DoesNotContain("[>]", view);
            StringAssert.DoesNotContain("◆", view);
            StringAssert.DoesNotContain("·", view);
            StringAssert.DoesNotContain("“", view);
            StringAssert.DoesNotContain("”", view);
        }

        [Test]
        public void ClickingInputsSelectsTheTypedVoiceTarget()
        {
            GameObject canvasObject = new("Canvas", typeof(RectTransform), typeof(Canvas));
            GameObject viewObject = new("Voice Target Workbench");
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Component view = viewObject.AddComponent(RuntimeType("MicroCreeiWorkbenchView"));
                view.GetType().GetMethod("Build").Invoke(view, new object[] { canvas });
                TMP_InputField claim = canvasObject.GetComponentsInChildren<TMP_InputField>(true)
                    .Single(input => input.gameObject.name == "CREEI Input Claim");
                TMP_InputField request = canvasObject.GetComponentsInChildren<TMP_InputField>(true)
                    .Single(input => input.gameObject.name == "Learner Coach Request");
                claim.onSelect.Invoke(claim.text);
                Assert.AreEqual("Claim", Property(view, "VoiceTarget"));
                request.gameObject.SetActive(true);
                request.onSelect.Invoke(request.text);
                Assert.AreEqual("CoachRequest", Property(view, "VoiceTarget"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void Scene04FeedbackRequestsUseOneSeventeenSecondAttempt()
        {
            Type generator = RuntimeType("DebateCoachFeedbackGenerator");
            Type purpose = RuntimeType("CoachFeedbackPurpose");
            MethodInfo maxTokens = generator.GetMethod("GetMaxTokens",
                BindingFlags.Public | BindingFlags.Static);
            Assert.AreEqual(180, maxTokens.Invoke(null,
                new[] { Enum.Parse(purpose, "CriticalIssue") }));
            Assert.AreEqual(400, maxTokens.Invoke(null,
                new[] { Enum.Parse(purpose, "DirectAdvice") }));
            Assert.AreEqual(400, maxTokens.Invoke(null,
                new[] { Enum.Parse(purpose, "CreeiModelAnswer") }));
            string source = File.ReadAllText(
                "Assets/Game/Scripts/DebateCoachFeedbackGenerator.cs");
            StringAssert.Contains("useScene04Budget ? 18f", source);
            StringAssert.Contains("useScene04Budget ? 1 : 2", source);
            StringAssert.Contains("Mathf.Min(17f, remainingBudget)", source);
            StringAssert.DoesNotContain("Mathf.Min(10f, remainingBudget)", source);
            StringAssert.Contains("safeRequest.Purpose.HasValue ? 1 : 2", source);
        }

        [Test]
        public void Scene04LogsLlmAndTtsLatencySeparately()
        {
            string workbench = File.ReadAllText(
                "Assets/Game/Scripts/MicroCreeiPracticeController.cs");
            string host = File.ReadAllText(
                "Assets/Game/Scripts/ThreeStageDebatePracticeController.cs");
            foreach (string eventName in new[]
                     {
                         "coach_request_started", "coach_request_retried",
                         "coach_request_completed", "coach_request_failed"
                     })
                StringAssert.Contains(eventName, workbench);
            foreach (string eventName in new[]
                     {
                         "coach_tts_requested", "coach_tts_started", "coach_tts_completed"
                     })
                StringAssert.Contains(eventName, host);
        }

        [Test]
        public void DetailedFeedbackAndHistoryRemainScrollable()
        {
            GameObject canvasObject = new("Canvas", typeof(RectTransform), typeof(Canvas));
            GameObject viewObject = new("Scrollable Feedback View");
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Component view = viewObject.AddComponent(RuntimeType("MicroCreeiWorkbenchView"));
                view.GetType().GetMethod("Build").Invoke(view, new object[] { canvas });
                Assert.IsNotNull(canvasObject.transform.Find("CREEI Dialogue Bubble")
                    .GetComponentInChildren<ScrollRect>(true));
                Assert.IsNotNull(canvasObject.transform.Find("Micro CREEI Feedback History")
                    .GetComponentInChildren<ScrollRect>(true));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void WorkedExamplesAppearInsideCardsWithoutChangingLearnerInputs()
        {
            GameObject canvasObject = new("Canvas", typeof(RectTransform), typeof(Canvas));
            GameObject viewObject = new("Worked Example View");
            try
            {
                Canvas canvas = canvasObject.GetComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                Component view = viewObject.AddComponent(RuntimeType("MicroCreeiWorkbenchView"));
                view.GetType().GetMethod("Build").Invoke(view, new object[] { canvas });

                TMP_InputField claimInput = canvasObject.GetComponentsInChildren<TMP_InputField>(true)
                    .Single(input => input.gameObject.name == "CREEI Input Claim");
                claimInput.text = "Learner's original claim.";
                TMP_Text claimExample = canvasObject.GetComponentsInChildren<TMP_Text>(true)
                    .Single(text => text.gameObject.name == "CREEI Model Example Claim");
                Assert.IsFalse(claimExample.gameObject.activeSelf);

                Type setType = RuntimeType("CreeiModelExampleSet");
                object examples = Activator.CreateInstance(setType);
                foreach (string component in new[]
                         {
                             "Claim", "Reason", "Evidence", "Explanation", "Impact"
                         })
                    setType.GetField(component).SetValue(examples,
                        component + " model sentence.");
                view.GetType().GetMethod("ShowCreeiModelExamples")
                    .Invoke(view, new[] { examples });

                Assert.IsTrue(claimExample.gameObject.activeSelf);
                Assert.AreEqual("Anna's example: Claim model sentence.", claimExample.text);
                Assert.AreEqual("Learner's original claim.", claimInput.text);
                Assert.AreEqual(5, canvasObject.GetComponentsInChildren<TMP_Text>(true)
                    .Count(text => text.gameObject.name.StartsWith(
                        "CREEI Model Example ", StringComparison.Ordinal) &&
                                   text.gameObject.activeSelf));

                view.GetType().GetMethod("ClearCreeiModelExamples").Invoke(view, null);
                Assert.IsFalse(claimExample.gameObject.activeSelf);
                Assert.AreEqual("Learner's original claim.", claimInput.text);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(viewObject);
                UnityEngine.Object.DestroyImmediate(canvasObject);
            }
        }

        [Test]
        public void ResponsiveWorkspaceUsesTheFrozenWidthClamp()
        {
            MethodInfo width = RuntimeType("MicroCreeiWorkbenchView")
                .GetMethod("CalculateWorkspaceWidth", BindingFlags.Public | BindingFlags.Static);
            Assert.AreEqual(509.82f, (float)width.Invoke(null, new object[] { 879f }), 0.1f);
            Assert.AreEqual(640f, (float)width.Invoke(null, new object[] { 1600f }), 0.1f);

            MethodInfo dialogue = RuntimeType("MicroCreeiWorkbenchView")
                .GetMethod("CalculateDialogueRect", BindingFlags.Public | BindingFlags.Static);
            Rect narrow = (Rect)dialogue.Invoke(null, new object[] { 879f });
            Rect wide = (Rect)dialogue.Invoke(null, new object[] { 1600f });
            Assert.GreaterOrEqual(narrow.xMin, 514f);
            Assert.LessOrEqual(narrow.xMax, 875f);
            Assert.GreaterOrEqual(wide.xMin, 644f);
            Assert.LessOrEqual(wide.xMax, 956f);
        }

        [Test]
        public void ActiveScene04ContractsContainNoLeoChallengeFlow()
        {
            string controller = File.ReadAllText(
                "Assets/Game/Scripts/MicroCreeiPracticeController.cs");
            string host = File.ReadAllText(
                "Assets/Game/Scripts/ThreeStageDebatePracticeController.cs");
            string types = File.ReadAllText(
                "Assets/Game/Scripts/ThreeStageDebatePracticeTypes.cs");
            StringAssert.DoesNotContain("RequestChallenge(", controller);
            StringAssert.DoesNotContain("SpeakLeo(", controller);
            StringAssert.DoesNotContain("Opponent Leo", controller);
            StringAssert.DoesNotContain("public void RequestChallenge(", host);
            StringAssert.DoesNotContain("public void SpeakLeo(", host);
            StringAssert.DoesNotContain("challenge from Leo", types);
            StringAssert.Contains("three-mode-v4", File.ReadAllText(
                "Assets/Game/Scripts/CoachOrchestrationTypes.cs"));
            StringAssert.Contains("coach-feedback-v5", File.ReadAllText(
                "Assets/Game/Scripts/CoachOrchestrationTypes.cs"));
            StringAssert.Contains(
                "IssueFeedbackRequest(focus, CoachFeedbackPurpose.CreeiModelAnswer",
                controller);
            StringAssert.DoesNotContain(
                "IssueFeedbackRequest(focus, CoachFeedbackPurpose.DirectAdvice",
                controller);
        }

        [Test]
        public void CoachOnlyEventsUseExistingCsvFieldsAndLeaveChallengeColumnsUnassigned()
        {
            string controller = File.ReadAllText(
                "Assets/Game/Scripts/MicroCreeiPracticeController.cs");
            string host = File.ReadAllText(
                "Assets/Game/Scripts/ThreeStageDebatePracticeController.cs");
            foreach (string eventName in new[]
                     {
                         "critical_feedback_presented",
                         "critical_feedback_accepted",
                         "critical_feedback_change_requested",
                         "critical_feedback_updated",
                         "learner_followup_submitted",
                         "coach_example_requested",
                         "coach_more_suggestions_requested",
                         "coach_advice_presented",
                         "ai_creei_model_presented"
                     })
                StringAssert.Contains(eventName, controller);
            StringAssert.Contains("creei-worked-example-v1", controller);
            StringAssert.Contains("aicreeimodelpresented",
                File.ReadAllText(
                    "Assets/Game/Scripts/ResearchEpistemicActionAnnotator.cs")
                    .ToLowerInvariant());
            foreach (string field in new[]
                     {
                         "AgendaText = ReadPayload",
                         "LearnerControlAction = ReadPayload",
                         "ConfirmedLearnerText = ReadPayload",
                         "CoachFeedbackText = ReadPayload",
                         "CoachTurnIndex = ReadPayloadInt"
                     })
                StringAssert.Contains(field, host);
            StringAssert.DoesNotContain("OpponentUtteranceText = ReadPayload", host);
            StringAssert.DoesNotContain("ChallengeCycleIndex = ReadPayload", host);
        }

        [Test]
        public void CompletionGateStillRequiresOneRoundAndTwoSnapshots()
        {
            Type gate = RuntimeType("ResearchSceneCompletionGate");
            object status = gate.GetMethod("EvaluateCreeiWorkbenchPractice")
                .Invoke(null, new object[] { 2, 1, true, true, 2, 2, 2 });
            Assert.AreEqual("True", Property(status, "DataComplete"));
        }
    }
}
