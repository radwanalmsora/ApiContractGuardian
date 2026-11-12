using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ApiContractGuardian.Core.Comparison;
using ApiContractGuardian.Core.Model;
using ApiContractGuardian.Core.Parsing;

namespace ApiContractGuardian.Core
{
    public enum FailOn { Breaking, Warning, Never }

    public sealed class ComparisonResult
    {
        public ApiSpec OldSpec { get; set; }
        public ApiSpec NewSpec { get; set; }
        public List<ApiChange> Changes { get; set; } = new List<ApiChange>();

        public IEnumerable<ApiChange> Active { get { return Changes.Where(c => !c.Suppressed); } }
        public int BreakingCount { get { return Active.Count(c => c.Severity == Severity.Breaking); } }
        public int WarningCount { get { return Active.Count(c => c.Severity == Severity.Warning); } }
        public int InfoCount { get { return Active.Count(c => c.Severity == Severity.Info); } }
        public int SuppressedCount { get { return Changes.Count(c => c.Suppressed); } }

        public bool Fails(FailOn level)
        {
            if (level == FailOn.Never) return false;
            if (level == FailOn.Warning) return BreakingCount + WarningCount > 0;
            return BreakingCount > 0;
        }
    }

    public static class GuardianEngine
    {
        public static ComparisonResult Compare(string oldJson, string newJson, SuppressionSet suppressions = null)
        {
            var oldSpec = OpenApiParser.Parse(oldJson);
            var newSpec = OpenApiParser.Parse(newJson);
            var changes = SpecComparer.Compare(oldSpec, newSpec);
            if (suppressions != null)
            {
                foreach (var c in changes)
                {
                    var rule = suppressions.Find(c);
                    if (rule == null) continue;
                    c.Suppressed = true;
                    c.SuppressionReason = rule.Reason ?? "";
                }
            }
            return new ComparisonResult { OldSpec = oldSpec, NewSpec = newSpec, Changes = changes };
        }

        public static ComparisonResult CompareFiles(string oldPath, string newPath, string suppressionsPath = null)
        {
            if (!File.Exists(oldPath)) throw new SpecParseException("Baseline file not found: " + oldPath);
            if (!File.Exists(newPath)) throw new SpecParseException("Candidate file not found: " + newPath);
            SuppressionSet set = null;
            if (!string.IsNullOrEmpty(suppressionsPath) && File.Exists(suppressionsPath))
                set = SuppressionSet.Load(suppressionsPath);
            return Compare(File.ReadAllText(oldPath), File.ReadAllText(newPath), set);
        }
    }
}
