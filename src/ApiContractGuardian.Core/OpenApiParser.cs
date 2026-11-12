using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using ApiContractGuardian.Core.Model;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace ApiContractGuardian.Core.Parsing
{
    /// <summary>Parses Swagger 2.0 and OpenAPI 3.x JSON documents into a normalized, $ref-resolved model.</summary>
    public static class OpenApiParser
    {
        public static ApiSpec Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                throw new SpecParseException("The specification file is empty.");
            JObject root;
            try
            {
                using (var sr = new StringReader(json))
                using (var reader = new JsonTextReader(sr) { MaxDepth = null })
                    root = JObject.Load(reader);
            }
            catch (JsonException ex)
            {
                throw new SpecParseException("File is not a valid JSON object (YAML is not supported yet): " + ex.Message, ex);
            }
            return new Parser(root).Run();
        }

        public static string NormalizePath(string path)
        {
            var p = Regex.Replace(path ?? "", @"\{[^}/]+\}", "{}");
            if (p.Length > 1 && p.EndsWith("/", StringComparison.Ordinal)) p = p.TrimEnd('/');
            return p;
        }

        sealed class Parser
        {
            static readonly string[] Methods = { "get", "put", "post", "delete", "options", "head", "patch", "trace" };
            readonly JObject _root;
            readonly Dictionary<string, SchemaNode> _cache = new Dictionary<string, SchemaNode>(StringComparer.Ordinal);
            bool _v2;

            public Parser(JObject root) { _root = root; }

            public ApiSpec Run()
            {
                string sw = Str(_root["swagger"]), oa = Str(_root["openapi"]);
                if (sw != null && sw.StartsWith("2", StringComparison.Ordinal)) _v2 = true;
                else if (oa != null && oa.StartsWith("3", StringComparison.Ordinal)) _v2 = false;
                else throw new SpecParseException("Unsupported document: expected 'swagger: 2.0' or 'openapi: 3.x'.");

                var spec = new ApiSpec { Format = _v2 ? "Swagger " + sw : "OpenAPI " + oa };
                var info = _root["info"] as JObject;
                spec.Title = Str(info == null ? null : info["title"]) ?? "";
                spec.Version = Str(info == null ? null : info["version"]) ?? "";

                var paths = _root["paths"] as JObject;
                if (paths == null) return spec;
                foreach (var p in paths.Properties())
                {
                    if (p.Name.StartsWith("x-", StringComparison.Ordinal)) continue;
                    var item = ResolveObj(p.Value);
                    if (item == null) continue;
                    foreach (var m in Methods)
                    {
                        var opObj = item[m] as JObject;
                        if (opObj == null) continue;
                        var op = ParseOperation(m, p.Name, item, opObj);
                        spec.Operations[op.Key] = op;
                    }
                }
                return spec;
            }

            ApiOperation ParseOperation(string method, string path, JObject item, JObject opObj)
            {
                var op = new ApiOperation
                {
                    Method = method.ToUpperInvariant(),
                    Path = path,
                    OperationId = Str(opObj["operationId"]) ?? "",
                    Deprecated = Bool(opObj["deprecated"])
                };
                op.Key = op.Method + " " + NormalizePath(path);
                var pathNames = Regex.Matches(path, @"\{([^}/]+)\}").Cast<Match>().Select(m => m.Groups[1].Value).ToList();

                // path-level parameters, overridden by operation-level ones
                var merged = new Dictionary<string, JObject>(StringComparer.Ordinal);
                var order = new List<string>();
                foreach (var arr in new[] { item["parameters"] as JArray, opObj["parameters"] as JArray })
                {
                    if (arr == null) continue;
                    foreach (var t in arr)
                    {
                        var po = ResolveObj(t);
                        if (po == null) continue;
                        var k = (Str(po["in"]) ?? "") + "|" + (Str(po["name"]) ?? "");
                        if (!merged.ContainsKey(k)) order.Add(k);
                        merged[k] = po;
                    }
                }

                var consumes = MediaList(opObj["consumes"] ?? _root["consumes"], null);
                SchemaNode form = null;
                foreach (var k in order)
                {
                    var po = merged[k];
                    string name = Str(po["name"]) ?? "", where = Str(po["in"]) ?? "";
                    bool req = Bool(po["required"]);

                    if (_v2 && where == "body")
                    {
                        op.RequestBody = new ApiBody { Required = req };
                        var bodySchema = ParseSchema(po["schema"]);
                        foreach (var mt in consumes ?? new List<string> { "application/json" })
                            op.RequestBody.Content[mt] = bodySchema;
                        continue;
                    }
                    if (_v2 && where == "formData")
                    {
                        if (form == null) form = new SchemaNode { Type = "object" };
                        form.Properties[name] = ParseSchema(po);
                        if (req) form.Required.Add(name);
                        continue;
                    }

                    JToken st = po["schema"];
                    if (st == null && !_v2)
                    {
                        var pc = po["content"] as JObject;
                        if (pc != null)
                            st = pc.Properties().Select(x => (x.Value as JObject) == null ? null : x.Value["schema"]).FirstOrDefault(x => x != null);
                    }
                    var schema = ParseSchema(_v2 && st == null ? po : st);

                    var prm = new ApiParameter { Name = name, In = where, Required = where == "path" || req, Schema = schema };
                    if (where == "path")
                    {
                        int idx = pathNames.IndexOf(name);
                        prm.Key = idx >= 0 ? "path#" + idx : "path:" + name;
                    }
                    else prm.Key = where + ":" + (where == "header" ? name.ToLowerInvariant() : name);

                    var existing = op.Parameters.FindIndex(x => x.Key == prm.Key);
                    if (existing >= 0) op.Parameters[existing] = prm; else op.Parameters.Add(prm);
                }

                if (form != null)
                {
                    op.RequestBody = new ApiBody { Required = form.Required.Count > 0 };
                    foreach (var mt in consumes ?? new List<string> { "application/x-www-form-urlencoded" })
                        op.RequestBody.Content[mt] = form;
                }

                if (!_v2)
                {
                    var rbo = ResolveObj(opObj["requestBody"]);
                    if (rbo != null)
                    {
                        op.RequestBody = new ApiBody { Required = Bool(rbo["required"]) };
                        var content = rbo["content"] as JObject;
                        if (content != null)
                            foreach (var mt in content.Properties())
                                op.RequestBody.Content[NormMedia(mt.Name)] = ParseSchema((mt.Value as JObject) == null ? null : mt.Value["schema"]);
                    }
                }

                var responses = opObj["responses"] as JObject;
                if (responses != null)
                {
                    foreach (var rp in responses.Properties())
                    {
                        if (rp.Name.StartsWith("x-", StringComparison.Ordinal)) continue;
                        var resp = new ApiResponse();
                        var ro = ResolveObj(rp.Value);
                        if (ro != null)
                        {
                            if (_v2)
                            {
                                if (ro["schema"] is JObject)
                                {
                                    var sn = ParseSchema(ro["schema"]);
                                    foreach (var mt in MediaList(opObj["produces"] ?? _root["produces"], null) ?? new List<string> { "application/json" })
                                        resp.Content[mt] = sn;
                                }
                            }
                            else
                            {
                                var rc = ro["content"] as JObject;
                                if (rc != null)
                                    foreach (var mt in rc.Properties())
                                        resp.Content[NormMedia(mt.Name)] = ParseSchema((mt.Value as JObject) == null ? null : mt.Value["schema"]);
                            }
                        }
                        op.Responses[rp.Name] = resp;
                    }
                }
                return op;
            }

            // ---------- schemas ----------

            SchemaNode ParseSchema(JToken tok)
            {
                var o = tok as JObject;
                if (o == null) return new SchemaNode();
                var rf = Str(o["$ref"]);
                if (rf != null)
                {
                    SchemaNode cached;
                    if (_cache.TryGetValue(rf, out cached)) return cached;
                    var node = new SchemaNode { RefName = rf.Substring(rf.LastIndexOf('/') + 1) };
                    _cache[rf] = node; // registered before filling => cyclic schemas terminate
                    Fill(node, LookupPointer(rf) as JObject);
                    return node;
                }
                var n = new SchemaNode();
                Fill(n, o);
                return n;
            }

            void Fill(SchemaNode n, JObject o)
            {
                if (o == null) return;
                if (Str(o["$ref"]) != null) { Copy(n, ParseSchema(o)); return; }

                var t = o["type"];
                var ta = t as JArray;
                if (ta != null)
                {
                    foreach (var x in ta)
                    {
                        var s = Str(x);
                        if (s == "null") n.Nullable = true;
                        else if (s != null && n.Type == "") n.Type = s;
                    }
                }
                else n.Type = Str(t) ?? "";

                n.Format = Str(o["format"]) ?? "";
                n.Nullable = n.Nullable || Bool(o["nullable"]) || Bool(o["x-nullable"]);
                n.ReadOnly = Bool(o["readOnly"]);
                n.WriteOnly = Bool(o["writeOnly"]);
                n.Pattern = Str(o["pattern"]) ?? "";
                n.Minimum = Num(o["minimum"]);
                n.Maximum = Num(o["maximum"]);
                n.MinLength = Num(o["minLength"]);
                n.MaxLength = Num(o["maxLength"]);

                var ea = o["enum"] as JArray;
                if (ea != null)
                    foreach (var e in ea) n.Enum.Add(e.Type == JTokenType.Null ? "null" : e.ToString(Formatting.None));

                var props = o["properties"] as JObject;
                if (props != null)
                    foreach (var p in props.Properties()) n.Properties[p.Name] = ParseSchema(p.Value);

                var ra = o["required"] as JArray;
                if (ra != null)
                    foreach (var x in ra) { var s = Str(x); if (s != null) n.Required.Add(s); }

                if (o["items"] is JObject) n.Items = ParseSchema(o["items"]);

                var all = o["allOf"] as JArray;
                if (all != null)
                    foreach (var s in all) Merge(n, ParseSchema(s));

                foreach (var key in new[] { "oneOf", "anyOf" })
                {
                    var arr = o[key] as JArray;
                    if (arr == null) continue;
                    foreach (var s in arr)
                    {
                        var v = ParseSchema(s);
                        if (v.Type == "null") n.Nullable = true; else n.OneOf.Add(v);
                    }
                }

                if (n.Type == "" && n.Properties.Count > 0) n.Type = "object";
                if (n.Type == "" && n.Items != null) n.Type = "array";
            }

            static void Merge(SchemaNode n, SchemaNode s)
            {
                foreach (var kv in s.Properties) if (!n.Properties.ContainsKey(kv.Key)) n.Properties[kv.Key] = kv.Value;
                foreach (var r in s.Required) n.Required.Add(r);
                if (n.Type == "") n.Type = s.Type;
                if (n.Format == "") n.Format = s.Format;
                if (n.Items == null) n.Items = s.Items;
                if (n.Enum.Count == 0) n.Enum.AddRange(s.Enum);
                n.Nullable = n.Nullable || s.Nullable;
                if (n.Pattern == "") n.Pattern = s.Pattern;
                if (!n.Minimum.HasValue) n.Minimum = s.Minimum;
                if (!n.Maximum.HasValue) n.Maximum = s.Maximum;
                if (!n.MinLength.HasValue) n.MinLength = s.MinLength;
                if (!n.MaxLength.HasValue) n.MaxLength = s.MaxLength;
                n.OneOf.AddRange(s.OneOf);
            }

            static void Copy(SchemaNode n, SchemaNode s)
            {
                if (ReferenceEquals(n, s)) return;
                Merge(n, s);
                n.ReadOnly = s.ReadOnly;
                n.WriteOnly = s.WriteOnly;
            }

            // ---------- $ref + helpers ----------

            JObject ResolveObj(JToken t)
            {
                for (int i = 0; i < 32; i++)
                {
                    var o = t as JObject;
                    if (o == null) return null;
                    var rf = Str(o["$ref"]);
                    if (rf == null) return o;
                    t = LookupPointer(rf);
                }
                throw new SpecParseException("$ref chain is too deep or circular.");
            }

            JToken LookupPointer(string r)
            {
                if (!r.StartsWith("#/", StringComparison.Ordinal))
                    throw new SpecParseException("Unsupported $ref '" + r + "': only local '#/...' references are supported.");
                JToken cur = _root;
                foreach (var seg in r.Substring(2).Split('/'))
                {
                    var s = Uri.UnescapeDataString(seg).Replace("~1", "/").Replace("~0", "~");
                    var jo = cur as JObject;
                    var ja = cur as JArray;
                    int idx;
                    if (jo != null) cur = jo[s];
                    else if (ja != null && int.TryParse(s, out idx) && idx >= 0 && idx < ja.Count) cur = ja[idx];
                    else cur = null;
                    if (cur == null) throw new SpecParseException("Unresolved $ref '" + r + "'.");
                }
                return cur;
            }

            static string Str(JToken t)
            {
                var v = t as JValue;
                return v != null && v.Type == JTokenType.String ? v.Value as string : null;
            }

            static bool Bool(JToken t)
            {
                var v = t as JValue;
                return v != null && v.Type == JTokenType.Boolean && (bool)v.Value;
            }

            static double? Num(JToken t)
            {
                var v = t as JValue;
                if (v != null && (v.Type == JTokenType.Integer || v.Type == JTokenType.Float))
                {
                    try { return Convert.ToDouble(v.Value, System.Globalization.CultureInfo.InvariantCulture); }
                    catch (OverflowException) { return null; }
                    catch (InvalidCastException) { return null; }
                }
                return null;
            }

            static string NormMedia(string m) { return m.Split(';')[0].Trim().ToLowerInvariant(); }

            static List<string> MediaList(JToken t, string fallback)
            {
                var arr = t as JArray;
                if (arr == null) return fallback == null ? null : new List<string> { fallback };
                var l = arr.Select(Str).Where(s => s != null).Select(NormMedia).ToList();
                if (l.Count == 0) return fallback == null ? null : new List<string> { fallback };
                return l;
            }
        }
    }
}
