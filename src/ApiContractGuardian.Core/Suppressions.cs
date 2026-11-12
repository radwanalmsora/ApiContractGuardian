using System;
using System.Collections.Generic;
using System.IO;
using ApiContractGuardian.Core.Model;
using Newtonsoft.Json;

namespace ApiContractGuardian.Core
{
    /// <summary>One accepted (intentional) change. Use "*" or leave empty to match anything.</summary>
    public sealed class SuppressionRule
    {
        public string Code { get; set; }
        /// <summary>Exact operation key ("DELETE /orders/{}"), or a prefix ending with '*' ("GET /legacy*").</summary>
        public string Operation { get; set; }
        public string Reason { get; set; }
    }

    public sealed class SuppressionSet
    {
        public List<SuppressionRule> Suppressions { get; set; } = new List<SuppressionRule>();

        public static SuppressionSet Load(string path)
        {
            try
            {
                var set = JsonConvert.DeserializeObject<SuppressionSet>(File.ReadAllText(path));
                if (set == null) return new SuppressionSet();
                if (set.Suppressions == null) set.Suppressions = new List<SuppressionRule>();
                return set;
            }
            catch (JsonException ex)
            {
                throw new SpecParseException("Invalid suppressions file '" + path + "': " + ex.Message, ex);
            }
        }

        public SuppressionRule Find(ApiChange c)
        {
            foreach (var r in Suppressions)
            {
                if (r == null) continue;
                bool codeOk = string.IsNullOrEmpty(r.Code) || r.Code == "*" || string.Equals(r.Code, c.Code, StringComparison.OrdinalIgnoreCase);
                bool opOk = string.IsNullOrEmpty(r.Operation) || r.Operation == "*" || r.Operation == c.Operation ||
                            (r.Operation.EndsWith("*", StringComparison.Ordinal) &&
                             c.Operation.StartsWith(r.Operation.Substring(0, r.Operation.Length - 1), StringComparison.Ordinal));
                if (codeOk && opOk) return r;
            }
            return null;
        }
    }
}
