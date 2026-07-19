using Game.Debate;
using UnityEditor;
using UnityEngine;

namespace Game.EditorTools
{
    public static class ResearchPilotAuditMenu
    {
        [MenuItem("Tools/Research/Export Pilot Audit Summary")]
        public static void ExportPilotAuditSummary()
        {
            string rootDirectory = ResearchSessionPaths.GetDefaultRootDirectory();
            string csvPath = ResearchPilotAuditService.ExportRootSummary(rootDirectory);
            Debug.Log("Research pilot audit CSV exported: " + csvPath);
            EditorUtility.RevealInFinder(csvPath);
        }
    }
}
