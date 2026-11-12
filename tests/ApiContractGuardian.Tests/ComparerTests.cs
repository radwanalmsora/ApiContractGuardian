using System.Collections.Generic;
using System.Linq;
using ApiContractGuardian.Core;
using ApiContractGuardian.Core.Model;
using Newtonsoft.Json;
using Xunit;

namespace ApiContractGuardian.Tests
{
    public class ComparerTests
    {
        // ---------- tiny OpenAPI 3 builders ----------
        static Dictionary<string, object> D(params (string k, object v)[] kv) => kv.ToDictionary(x => x.k, x => x.v);
        static object Ref(string name) => D(("$ref", "#/components/schemas/" + name));
        static object T(string type, string format = null) => new { type, format };
        static object Obj(object properties, params string[] required) => new { type = "object", properties, required };
        static object Resp(object schema, string code = "200") =>
            D((code, new { description = "ok", content = D(("application/json", new { schema })) }));
        static object Body(object schema, bool required = true) =>
            new { required, content = D(("application/json", new { schema })) };
        static object Q(string name, object schema, bool required = false) =>
            new { name, @in = "query", required, schema };
        static object Op(object responses, object[] parameters = null, object requestBody = null) =>
            new { parameters = parameters ?? new object[0], requestBody, responses };

        static string Spec(object paths, object schemas = null) =>
            JsonConvert.SerializeObject(
                new { openapi = "3.0.1", info = new { title = "t", version = "1" }, paths, components = new { schemas = schemas ?? new { } } },
                new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore });

        static List<ApiChange> Diff(string a, string b) => GuardianEngine.Compare(a, b).Changes;
        static bool Has(List<ApiChange> c, string code, Severity s) => c.Any(x => x.Code == code && x.Severity == s);
        static bool NoBreaking(List<ApiChange> c) => c.All(x => x.Severity != Severity.Breaking);

        static string GetWithQuery(params object[] ps) =>
            Spec(D(("/users", D(("get", Op(Resp(T("string")), ps))))));

        [Fact]
        public void RemovedEndpoint_IsBreaking()
        {
            var a = Spec(D(("/users", D(("get", Op(Resp(T("string"))))))));
            var b = Spec(D());
            Assert.True(Has(Diff(a, b), "ENDPOINT_REMOVED", Severity.Breaking));
        }

        [Fact]
        public void NewEndpoint_IsInfoOnly()
        {
            var a = Spec(D());
            var b = Spec(D(("/users", D(("get", Op(Resp(T("string"))))))));
            var c = Diff(a, b);
            Assert.True(Has(c, "ENDPOINT_ADDED", Severity.Info));
            Assert.True(NoBreaking(c));
        }

        [Fact]
        public void NewRequiredQueryParam_IsBreaking_Optional_IsNot()
        {
            var a = GetWithQuery();
            Assert.True(Has(Diff(a, GetWithQuery(Q("q", T("string"), true))), "PARAM_ADDED_REQUIRED", Severity.Breaking));
            var c = Diff(a, GetWithQuery(Q("q", T("string"))));
            Assert.True(NoBreaking(c));
            Assert.True(Has(c, "PARAM_ADDED_OPTIONAL", Severity.Info));
        }

        [Fact]
        public void OptionalParamBecomingRequired_IsBreaking()
        {
            var a = GetWithQuery(Q("q", T("string")));
            var b = GetWithQuery(Q("q", T("string"), true));
            Assert.True(Has(Diff(a, b), "PARAM_BECAME_REQUIRED", Severity.Breaking));
        }

        [Fact]
        public void ParamTypeChange_IsBreaking()
        {
            var a = GetWithQuery(Q("limit", T("integer")));
            var b = GetWithQuery(Q("limit", T("string")));
            Assert.True(Has(Diff(a, b), "TYPE_CHANGED", Severity.Breaking));
        }

        [Fact]
        public void IntegerToNumber_RequestOk_ResponseBreaking()
        {
            Assert.True(NoBreaking(Diff(GetWithQuery(Q("n", T("integer"))), GetWithQuery(Q("n", T("number"))))));

            var a = Spec(D(("/x", D(("get", Op(Resp(T("integer"))))))));
            var b = Spec(D(("/x", D(("get", Op(Resp(T("number"))))))));
            Assert.True(Has(Diff(a, b), "TYPE_CHANGED", Severity.Breaking));
        }

        [Fact]
        public void Int32ToInt64_RequestOk_ResponseBreaking()
        {
            Assert.True(NoBreaking(Diff(GetWithQuery(Q("n", T("integer", "int32"))), GetWithQuery(Q("n", T("integer", "int64"))))));

            var a = Spec(D(("/x", D(("get", Op(Resp(T("integer", "int32"))))))));
            var b = Spec(D(("/x", D(("get", Op(Resp(T("integer", "int64"))))))));
            Assert.True(Has(Diff(a, b), "FORMAT_CHANGED", Severity.Breaking));
        }

        [Fact]
        public void ResponsePropertyRemoved_IsBreaking_Added_IsInfo()
        {
            var two = Obj(new { id = T("integer"), name = T("string") });
            var one = Obj(new { id = T("integer") });
            string P(object s) => Spec(D(("/u", D(("get", Op(Resp(s)))))));

            Assert.True(Has(Diff(P(two), P(one)), "RESPONSE_PROPERTY_REMOVED", Severity.Breaking));
            var c = Diff(P(one), P(two));
            Assert.True(NoBreaking(c));
            Assert.True(Has(c, "RESPONSE_PROPERTY_ADDED", Severity.Info));
        }

        [Fact]
        public void ResponsePropertyBecomesOptional_IsBreaking()
        {
            string P(object s) => Spec(D(("/u", D(("get", Op(Resp(s)))))));
            var a = P(Obj(new { id = T("integer") }, "id"));
            var b = P(Obj(new { id = T("integer") }));
            Assert.True(Has(Diff(a, b), "RESPONSE_PROPERTY_BECAME_OPTIONAL", Severity.Breaking));
        }

        [Fact]
        public void RequestBody_NewRequiredProperty_IsBreaking_OptionalIsNot()
        {
            string P(object s) => Spec(D(("/u", D(("post", Op(Resp(T("string")), null, Body(s)))))));
            var a = P(Obj(new { name = T("string") }, "name"));
            Assert.True(Has(Diff(a, P(Obj(new { name = T("string"), age = T("integer") }, "name", "age"))), "REQUEST_PROPERTY_ADDED_REQUIRED", Severity.Breaking));
            Assert.True(NoBreaking(Diff(a, P(Obj(new { name = T("string"), age = T("integer") }, "name")))));
        }

        [Fact]
        public void RequestBody_NestedTypeChange_IsFoundWithPath()
        {
            string P(string t) => Spec(D(("/u", D(("post", Op(Resp(T("string")), null,
                Body(Obj(new { items = new { type = "array", items = Obj(new { qty = T(t) }) } }))))))));
            var c = Diff(P("integer"), P("string"));
            Assert.Contains(c, x => x.Code == "TYPE_CHANGED" && x.Severity == Severity.Breaking && x.Location.EndsWith(".items[].qty"));
        }

        [Fact]
        public void PathParamRename_IsNotBreaking()
        {
            string P(string path, string name) => Spec(D((path, D(("get", Op(Resp(T("string")),
                new object[] { new { name, @in = "path", required = true, schema = T("integer") } }))))));
            var c = Diff(P("/users/{id}", "id"), P("/users/{userId}", "userId"));
            Assert.True(NoBreaking(c));
            Assert.True(Has(c, "PATH_PARAM_RENAMED", Severity.Info));
        }

        [Fact]
        public void PathParamTypeChange_IsBreaking()
        {
            string P(string t) => Spec(D(("/users/{id}", D(("get", Op(Resp(T("string")),
                new object[] { new { name = "id", @in = "path", required = true, schema = T(t) } }))))));
            Assert.True(Has(Diff(P("integer"), P("string")), "TYPE_CHANGED", Severity.Breaking));
        }

        [Fact]
        public void RequestEnumValueRemoved_IsBreaking_ResponseEnumValueAdded_IsWarning()
        {
            object E(params string[] v) => new { type = "string", @enum = v };
            Assert.True(Has(Diff(GetWithQuery(Q("s", E("a", "b"))), GetWithQuery(Q("s", E("a")))), "ENUM_VALUE_REMOVED", Severity.Breaking));

            string R(object s) => Spec(D(("/x", D(("get", Op(Resp(s)))))));
            var c = Diff(R(E("a")), R(E("a", "b")));
            Assert.True(NoBreaking(c));
            Assert.True(Has(c, "ENUM_VALUE_ADDED", Severity.Warning));
        }

        [Fact]
        public void RemovedSuccessResponse_IsBreaking()
        {
            var a = Spec(D(("/x", D(("get", Op(D(("200", new { description = "ok" }), ("404", new { description = "nf" }))))))));
            var b = Spec(D(("/x", D(("get", Op(D(("404", new { description = "nf" }))))))));
            Assert.True(Has(Diff(a, b), "RESPONSE_REMOVED", Severity.Breaking));
        }

        [Fact]
        public void CyclicSchemas_TerminateAndProduceNoChanges()
        {
            var schemas = D(("Node", Obj(new { value = T("integer"), child = Ref("Node") })));
            var s = Spec(D(("/n", D(("get", Op(Resp(Ref("Node"))))))), schemas);
            Assert.Empty(Diff(s, s));
        }

        [Fact]
        public void CyclicSchemas_ChangeInsideCycleIsDetected()
        {
            string S(string t) => Spec(D(("/n", D(("get", Op(Resp(Ref("Node"))))))),
                D(("Node", Obj(new { value = T(t), child = Ref("Node") }))));
            Assert.True(Has(Diff(S("integer"), S("string")), "TYPE_CHANGED", Severity.Breaking));
        }

        [Fact]
        public void ReadOnlyRequestProperty_IsIgnoredForRequests()
        {
            string P(bool withId) => Spec(D(("/u", D(("post", Op(Resp(T("string")), null,
                Body(withId
                    ? Obj(new { id = new { type = "integer", readOnly = true }, name = T("string") }, "id", "name")
                    : Obj(new { name = T("string") }, "name"))))))));
            Assert.True(NoBreaking(Diff(P(true), P(false))));
        }

        const string Swagger2 = """
        {"swagger":"2.0","info":{"title":"t","version":"1"},
         "paths":{"/pets":{"post":{"consumes":["application/json"],"produces":["application/json"],
           "parameters":[{"in":"body","name":"body","required":true,"schema":{"$ref":"#/definitions/Pet"}}],
           "responses":{"200":{"description":"ok","schema":{"$ref":"#/definitions/Pet"}}}}}},
         "definitions":{"Pet":{"type":"object","required":[__REQ__],"properties":{"name":{"type":"string"},"tag":{"type":"string"}}}}}
        """;

        [Fact]
        public void Swagger2_BodyParameterAndDefinitionsAreResolved()
        {
            var a = Swagger2.Replace("__REQ__", "\"name\"");
            var b = Swagger2.Replace("__REQ__", "\"name\",\"tag\"");
            Assert.True(Has(Diff(a, b), "PROPERTY_BECAME_REQUIRED", Severity.Breaking));
            Assert.Empty(Diff(a, a));
        }

        [Fact]
        public void Suppression_MovesChangeToAccepted()
        {
            var a = Spec(D(("/users", D(("get", Op(Resp(T("string"))))))));
            var b = Spec(D());
            var set = new SuppressionSet();
            set.Suppressions.Add(new SuppressionRule { Code = "ENDPOINT_REMOVED", Operation = "GET /users", Reason = "retired" });
            var r = GuardianEngine.Compare(a, b, set);
            Assert.Equal(0, r.BreakingCount);
            Assert.Equal(1, r.SuppressedCount);
            Assert.False(r.Fails(FailOn.Breaking));
        }

        [Fact]
        public void Report_ContainsVerdictAndCodes()
        {
            var a = Spec(D(("/users", D(("get", Op(Resp(T("string"))))))));
            var md = MarkdownReport.Build(GuardianEngine.Compare(a, Spec(D())));
            Assert.Contains("Verdict: FAIL", md);
            Assert.Contains("ENDPOINT_REMOVED", md);
        }

        [Fact]
        public void InvalidInput_ThrowsFriendlyException()
        {
            Assert.Throws<SpecParseException>(() => GuardianEngine.Compare("not json", "{}"));
            Assert.Throws<SpecParseException>(() => GuardianEngine.Compare("{}", "{}"));
        }
    }
}
