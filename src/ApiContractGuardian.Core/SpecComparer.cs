using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ApiContractGuardian.Core.Model;

namespace ApiContractGuardian.Core.Comparison
{
    /// <summary>
    /// Compares two normalized specs from the point of view of an EXISTING client.
    /// Request data: the server must keep accepting everything it accepted before.
    /// Response data: the server must keep returning only what clients already handle.
    /// </summary>
    public sealed class SpecComparer
    {
        readonly List<ApiChange> _changes = new List<ApiChange>();
        string _op = "";

        public static List<ApiChange> Compare(ApiSpec oldSpec, ApiSpec newSpec)
        {
            return new SpecComparer().Run(oldSpec, newSpec);
        }

        List<ApiChange> Run(ApiSpec o, ApiSpec n)
        {
            var newByOpId = new Dictionary<string, ApiOperation>(StringComparer.Ordinal);
            foreach (var x in n.Operations.Values)
                if (!string.IsNullOrEmpty(x.OperationId) && !newByOpId.ContainsKey(x.OperationId)) newByOpId[x.OperationId] = x;

            foreach (var kv in o.Operations)
            {
                _op = kv.Key;
                ApiOperation nop;
                if (!n.Operations.TryGetValue(kv.Key, out nop))
                {
                    string hint = "";
                    ApiOperation moved;
                    if (!string.IsNullOrEmpty(kv.Value.OperationId) && newByOpId.TryGetValue(kv.Value.OperationId, out moved))
                        hint = " (operationId '" + kv.Value.OperationId + "' now exists at " + moved.Key + ": the route or HTTP method changed)";
                    if (kv.Value.Deprecated)
                        Add(Severity.Warning, "ENDPOINT_REMOVED", "operation", "Deprecated endpoint was removed" + hint);
                    else
                        Add(Severity.Breaking, "ENDPOINT_REMOVED", "operation", "Endpoint was removed; existing clients calling it will fail" + hint);
                    continue;
                }
                CompareOperation(kv.Value, nop);
            }
            foreach (var key in n.Operations.Keys)
            {
                if (o.Operations.ContainsKey(key)) continue;
                _op = key;
                Add(Severity.Info, "ENDPOINT_ADDED", "operation", "New endpoint");
            }

            return _changes
                .GroupBy(c => c.Operation + "|" + c.Code + "|" + c.Location + "|" + c.Message)
                .Select(g => g.First())
                .OrderByDescending(c => c.Severity)
                .ThenBy(c => c.Operation, StringComparer.Ordinal)
                .ThenBy(c => c.Location, StringComparer.Ordinal)
                .ToList();
        }

        void CompareOperation(ApiOperation o, ApiOperation n)
        {
            if (!o.Deprecated && n.Deprecated)
                Add(Severity.Info, "ENDPOINT_DEPRECATED", "operation", "Endpoint marked as deprecated");
            CompareParameters(o, n);
            CompareBody(o, n);
            CompareResponses(o, n);
        }

        // ---------- parameters ----------

        void CompareParameters(ApiOperation o, ApiOperation n)
        {
            var newP = n.Parameters.ToDictionary(p => p.Key, StringComparer.Ordinal);
            var oldP = o.Parameters.ToDictionary(p => p.Key, StringComparer.Ordinal);

            foreach (var op in o.Parameters)
            {
                string loc = "parameter '" + op.Name + "' (" + op.In + ")";
                ApiParameter np;
                if (!newP.TryGetValue(op.Key, out np))
                {
                    Add(Severity.Warning, "PARAM_REMOVED", loc,
                        "Parameter removed; clients that still send it are ignored or rejected depending on server strictness");
                    continue;
                }
                if (op.In == "path" && op.Name != np.Name)
                    Add(Severity.Info, "PATH_PARAM_RENAMED", loc,
                        "Path parameter renamed '" + op.Name + "' -> '" + np.Name + "' (wire-compatible; generated client method signatures may change)");
                if (!op.Required && np.Required)
                    Add(Severity.Breaking, "PARAM_BECAME_REQUIRED", loc, "Optional parameter is now required; existing calls that omit it will fail");
                else if (op.Required && !np.Required)
                    Add(Severity.Info, "PARAM_BECAME_OPTIONAL", loc, "Required parameter is now optional");
                CompareSchema(op.Schema, np.Schema, Direction.Request, loc, new HashSet<string>(), 0);
            }

            foreach (var np in n.Parameters)
            {
                if (oldP.ContainsKey(np.Key)) continue;
                string loc = "parameter '" + np.Name + "' (" + np.In + ")";
                if (np.Required)
                {
                    var removed = o.Parameters.Where(p => p.In == np.In && !newP.ContainsKey(p.Key)).Select(p => "'" + p.Name + "'").ToList();
                    string hint = removed.Count > 0 ? " Possible rename of " + string.Join(", ", removed) + "." : "";
                    Add(Severity.Breaking, "PARAM_ADDED_REQUIRED", loc, "New required parameter; existing calls do not send it." + hint);
                }
                else Add(Severity.Info, "PARAM_ADDED_OPTIONAL", loc, "New optional parameter");
            }
        }

        // ---------- request body ----------

        void CompareBody(ApiOperation o, ApiOperation n)
        {
            var ob = o.RequestBody;
            var nb = n.RequestBody;
            const string loc = "request body";
            if (ob == null && nb == null) return;
            if (ob == null)
            {
                if (nb.Required) Add(Severity.Breaking, "REQUEST_BODY_ADDED_REQUIRED", loc, "A required request body was added; existing calls send none");
                else Add(Severity.Info, "REQUEST_BODY_ADDED", loc, "Optional request body added");
                return;
            }
            if (nb == null)
            {
                Add(Severity.Warning, "REQUEST_BODY_REMOVED", loc, "Request body removed; clients still sending one may be rejected");
                return;
            }
            if (!ob.Required && nb.Required)
                Add(Severity.Breaking, "REQUEST_BODY_BECAME_REQUIRED", loc, "Request body is now required");

            foreach (var m in ob.Content)
            {
                string l = "request body [" + m.Key + "]";
                SchemaNode ns;
                if (!nb.Content.TryGetValue(m.Key, out ns))
                {
                    Add(Severity.Breaking, "REQUEST_MEDIA_TYPE_REMOVED", l, "Media type no longer accepted");
                    continue;
                }
                CompareSchema(m.Value, ns, Direction.Request, l, new HashSet<string>(), 0);
            }
            foreach (var m in nb.Content)
                if (!ob.Content.ContainsKey(m.Key))
                    Add(Severity.Info, "REQUEST_MEDIA_TYPE_ADDED", "request body [" + m.Key + "]", "New accepted media type");
        }

        // ---------- responses ----------

        void CompareResponses(ApiOperation o, ApiOperation n)
        {
            foreach (var r in o.Responses)
            {
                string loc = "response " + r.Key;
                ApiResponse nr;
                if (!n.Responses.TryGetValue(r.Key, out nr))
                {
                    if (r.Key.StartsWith("2", StringComparison.Ordinal))
                        Add(Severity.Breaking, "RESPONSE_REMOVED", loc, "Success response " + r.Key + " no longer documented; clients expecting it may break");
                    else
                        Add(Severity.Info, "RESPONSE_REMOVED", loc, "Non-success response no longer documented");
                    continue;
                }
                foreach (var m in r.Value.Content)
                {
                    string l = loc + " [" + m.Key + "]";
                    SchemaNode ns;
                    if (!nr.Content.TryGetValue(m.Key, out ns))
                    {
                        Add(Severity.Breaking, "RESPONSE_MEDIA_TYPE_REMOVED", l, "Media type is no longer returned");
                        continue;
                    }
                    CompareSchema(m.Value, ns, Direction.Response, l, new HashSet<string>(), 0);
                }
                foreach (var m in nr.Content)
                    if (!r.Value.Content.ContainsKey(m.Key))
                        Add(Severity.Info, "RESPONSE_MEDIA_TYPE_ADDED", loc + " [" + m.Key + "]", "New media type returned");
            }
            foreach (var r in n.Responses)
                if (!o.Responses.ContainsKey(r.Key))
                    Add(Severity.Info, "RESPONSE_ADDED", "response " + r.Key, "New response status documented");
        }

        // ---------- schemas ----------

        void CompareSchema(SchemaNode o, SchemaNode n, Direction d, string loc, HashSet<string> seen, int depth)
        {
            if (o == null || n == null || depth > 40) return;
            if (!seen.Add(o.Id + ":" + n.Id + ":" + d)) return; // cycle / already compared

            if (o.Type != n.Type && !TypeCompatible(o.Type, n.Type, d))
            {
                Add(Severity.Breaking, "TYPE_CHANGED", loc, "Type changed from '" + Show(o.Type) + "' to '" + Show(n.Type) + "'");
                return;
            }

            CompareFormat(o, n, d, loc);

            if (d == Direction.Request && o.Nullable && !n.Nullable)
                Add(Severity.Breaking, "NULLABLE_REMOVED", loc, "null is no longer accepted");
            if (d == Direction.Response && !o.Nullable && n.Nullable)
                Add(Severity.Breaking, "NULLABLE_ADDED", loc, "Value can now be null; clients may not expect that");

            CompareEnum(o, n, d, loc);
            Bound("MINIMUM_CHANGED", "Minimum", o.Minimum, n.Minimum, true, d, loc);
            Bound("MAXIMUM_CHANGED", "Maximum", o.Maximum, n.Maximum, false, d, loc);
            Bound("MIN_LENGTH_CHANGED", "minLength", o.MinLength, n.MinLength, true, d, loc);
            Bound("MAX_LENGTH_CHANGED", "maxLength", o.MaxLength, n.MaxLength, false, d, loc);
            if (o.Pattern != n.Pattern && (o.Pattern != "" || n.Pattern != ""))
                Add(Severity.Warning, "PATTERN_CHANGED", loc, "Pattern changed from '" + o.Pattern + "' to '" + n.Pattern + "'");

            foreach (var kv in o.Properties)
            {
                if (Skip(kv.Value, d)) continue;
                string pl = loc + "." + kv.Key;
                SchemaNode np;
                if (!n.Properties.TryGetValue(kv.Key, out np))
                {
                    if (d == Direction.Request)
                        Add(Severity.Warning, "REQUEST_PROPERTY_REMOVED", pl, "Property removed from request; clients still sending it are ignored or rejected");
                    else
                        Add(Severity.Breaking, "RESPONSE_PROPERTY_REMOVED", pl, "Property removed from response; clients reading it will break");
                    continue;
                }
                bool oReq = o.Required.Contains(kv.Key), nReq = n.Required.Contains(kv.Key);
                if (d == Direction.Request && !oReq && nReq)
                    Add(Severity.Breaking, "PROPERTY_BECAME_REQUIRED", pl, "Optional request property is now required");
                if (d == Direction.Response && oReq && !nReq)
                    Add(Severity.Breaking, "RESPONSE_PROPERTY_BECAME_OPTIONAL", pl, "Response property was guaranteed and may now be missing");
                CompareSchema(kv.Value, np, d, pl, seen, depth + 1);
            }
            foreach (var kv in n.Properties)
            {
                if (o.Properties.ContainsKey(kv.Key) || Skip(kv.Value, d)) continue;
                string pl = loc + "." + kv.Key;
                if (d == Direction.Request)
                {
                    if (n.Required.Contains(kv.Key))
                        Add(Severity.Breaking, "REQUEST_PROPERTY_ADDED_REQUIRED", pl, "New required request property; existing clients do not send it");
                    else
                        Add(Severity.Info, "REQUEST_PROPERTY_ADDED", pl, "New optional request property");
                }
                else Add(Severity.Info, "RESPONSE_PROPERTY_ADDED", pl, "New response property");
            }

            if (o.Items != null && n.Items != null)
                CompareSchema(o.Items, n.Items, d, loc + "[]", seen, depth + 1);

            CompareOneOf(o, n, d, loc, seen, depth);
        }

        void CompareOneOf(SchemaNode o, SchemaNode n, Direction d, string loc, HashSet<string> seen, int depth)
        {
            if (o.OneOf.Count == 0 && n.OneOf.Count == 0) return;
            var os = new Dictionary<string, SchemaNode>(StringComparer.Ordinal);
            var ns = new Dictionary<string, SchemaNode>(StringComparer.Ordinal);
            foreach (var v in o.OneOf) os[Sig(v)] = v;
            foreach (var v in n.OneOf) ns[Sig(v)] = v;
            foreach (var kv in os)
            {
                SchemaNode nv;
                if (ns.TryGetValue(kv.Key, out nv)) { CompareSchema(kv.Value, nv, d, loc + "<" + kv.Key + ">", seen, depth + 1); continue; }
                Add(d == Direction.Request ? Severity.Breaking : Severity.Info, "ONEOF_VARIANT_REMOVED", loc,
                    "oneOf/anyOf variant '" + kv.Key + "' removed" + (d == Direction.Request ? "; clients may still send it" : ""));
            }
            foreach (var kv in ns)
                if (!os.ContainsKey(kv.Key))
                    Add(d == Direction.Response ? Severity.Breaking : Severity.Info, "ONEOF_VARIANT_ADDED", loc,
                        "oneOf/anyOf variant '" + kv.Key + "' added" + (d == Direction.Response ? "; clients may not handle it" : ""));
        }

        void CompareFormat(SchemaNode o, SchemaNode n, Direction d, string loc)
        {
            if (o.Format == n.Format) return;
            int ro = Rank(o), rn = Rank(n);
            if (o.Type == n.Type && ro > 0 && rn > 0)
            {
                bool widening = rn > ro;
                bool bad = d == Direction.Request ? !widening : widening;
                if (bad)
                    Add(Severity.Breaking, "FORMAT_CHANGED", loc,
                        "Format changed from '" + o.Format + "' to '" + n.Format + "' (" +
                        (widening ? "range widened: clients may overflow or mis-parse returned values" : "range narrowed: previously valid values are rejected") + ")");
            }
            else if (o.Format != "" && n.Format != "")
                Add(Severity.Warning, "FORMAT_CHANGED", loc, "Format changed from '" + o.Format + "' to '" + n.Format + "'");
            else if (d == Direction.Request && o.Format == "")
                Add(Severity.Warning, "FORMAT_ADDED", loc, "Format '" + n.Format + "' now required on input");
            else if (d == Direction.Response && n.Format == "")
                Add(Severity.Warning, "FORMAT_REMOVED", loc, "Format '" + o.Format + "' is no longer guaranteed on output");
        }

        void CompareEnum(SchemaNode o, SchemaNode n, Direction d, string loc)
        {
            if (o.Enum.Count == 0 && n.Enum.Count == 0) return;
            if (o.Enum.Count == 0)
            {
                if (d == Direction.Request)
                    Add(Severity.Breaking, "ENUM_RESTRICTED", loc, "Input is now restricted to: " + string.Join(", ", n.Enum));
                return;
            }
            if (n.Enum.Count == 0)
            {
                if (d == Direction.Response)
                    Add(Severity.Warning, "ENUM_REMOVED", loc, "Output is no longer limited to a fixed set of values");
                return;
            }
            var removed = o.Enum.Except(n.Enum).ToList();
            var added = n.Enum.Except(o.Enum).ToList();
            if (removed.Count > 0)
                Add(d == Direction.Request ? Severity.Breaking : Severity.Info, "ENUM_VALUE_REMOVED", loc,
                    "Enum value(s) removed: " + string.Join(", ", removed));
            if (added.Count > 0)
                Add(d == Direction.Response ? Severity.Warning : Severity.Info, "ENUM_VALUE_ADDED", loc,
                    "Enum value(s) added: " + string.Join(", ", added) + (d == Direction.Response ? " (clients with exhaustive switches may fail)" : ""));
        }

        void Bound(string code, string what, double? ov, double? nv, bool isMin, Direction d, string loc)
        {
            if (ov == nv) return;
            bool tighter, looser;
            if (isMin)
            {
                tighter = nv.HasValue && (!ov.HasValue || nv.Value > ov.Value);
                looser = ov.HasValue && (!nv.HasValue || nv.Value < ov.Value);
            }
            else
            {
                tighter = nv.HasValue && (!ov.HasValue || nv.Value < ov.Value);
                looser = ov.HasValue && (!nv.HasValue || nv.Value > ov.Value);
            }
            if (d == Direction.Request && tighter)
                Add(Severity.Breaking, code, loc, what + " tightened from " + Fmt(ov) + " to " + Fmt(nv) + "; previously valid input is rejected");
            if (d == Direction.Response && looser)
                Add(Severity.Warning, code, loc, what + " loosened from " + Fmt(ov) + " to " + Fmt(nv) + "; clients may rely on the old bound");
        }

        // ---------- helpers ----------

        static bool TypeCompatible(string oldT, string newT, Direction d)
        {
            if (oldT == newT) return true;
            if (oldT == "") return d == Direction.Response;  // any -> concrete: output narrower (fine); input narrower (breaking)
            if (newT == "") return d == Direction.Request;   // concrete -> any: input wider (fine); output wider (breaking)
            if (d == Direction.Request) return oldT == "integer" && newT == "number";
            return oldT == "number" && newT == "integer";
        }

        static int Rank(SchemaNode s)
        {
            if (s.Type == "integer") return s.Format == "int32" ? 1 : s.Format == "int64" ? 2 : 0;
            if (s.Type == "number") return s.Format == "float" ? 1 : s.Format == "double" ? 2 : 0;
            return 0;
        }

        static bool Skip(SchemaNode prop, Direction d)
        {
            return d == Direction.Request ? prop.ReadOnly : prop.WriteOnly;
        }

        static string Sig(SchemaNode s)
        {
            if (!string.IsNullOrEmpty(s.RefName)) return s.RefName;
            return s.Type + "{" + string.Join(",", s.Properties.Keys.OrderBy(k => k, StringComparer.Ordinal)) + "}";
        }

        static string Show(string t) { return string.IsNullOrEmpty(t) ? "any" : t; }

        static string Fmt(double? v) { return v.HasValue ? v.Value.ToString(CultureInfo.InvariantCulture) : "none"; }

        void Add(Severity s, string code, string loc, string msg)
        {
            _changes.Add(new ApiChange { Severity = s, Code = code, Operation = _op, Location = loc, Message = msg });
        }
    }
}
