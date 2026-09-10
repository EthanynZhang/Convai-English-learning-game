using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;

namespace Game.Tests.EditMode
{
    public sealed class ResearchScenePreflightTests
    {
        [Test]
        public void CurrentProjectPassesFormalResearchScenePreflight()
        {
            Type service = typeof(ResearchScenePreflightTests).Assembly.GetType(
                "Game.EditorTools.ResearchScenePreflightService");
            Assert.IsNotNull(service, "The research scene preflight service must exist.");
            if (service == null) return;

            object result = service.GetMethod("EvaluateCurrentProject")?.Invoke(null, null);
            Assert.IsNotNull(result);
            Assert.AreEqual("01>03>04>05",
                result.GetType().GetField("BuildSequence")?.GetValue(result));
            Assert.IsTrue((bool)result.GetType().GetField("Passed")?.GetValue(result));
            CollectionAssert.IsEmpty(
                (string[])result.GetType().GetField("Issues")?.GetValue(result));
        }

        [Test]
        public void ScenePreflightExportsOneReadableCsvRowPerFormalScene()
        {
            string outputPath = Path.Combine(Path.GetTempPath(),
                "research-scene-preflight-" + Guid.NewGuid().ToString("N") + ".csv");
            try
            {
                Type service = typeof(ResearchScenePreflightTests).Assembly.GetType(
                    "Game.EditorTools.ResearchScenePreflightService");
                Assert.IsNotNull(service, "The research scene preflight service must exist.");
                if (service == null) return;

                string exported = (string)service.GetMethod("ExportCsv")?.Invoke(
                    null, new object[] { outputPath });
                Assert.AreEqual(outputPath, exported);
                Assert.IsTrue(File.Exists(outputPath));
                string[] lines = File.ReadAllLines(outputPath);
                Assert.AreEqual(5, lines.Length);
                StringAssert.Contains("project_passed", lines[0]);
                StringAssert.Contains("required_components", lines[0]);
                StringAssert.Contains("01Level_NPCVsNPCDebate", lines[1]);
                StringAssert.Contains("05Level_PlayerVsNPCDebate 1", lines[4]);
            }
            finally
            {
                if (File.Exists(outputPath)) File.Delete(outputPath);
            }
        }
    }
}
