using System;
using System.Collections.Generic;
using System.Threading;

namespace ApiContractGuardian.Core.Model
{
    public enum Severity { Info = 0, Warning = 1, Breaking = 2 }

    /// <summary>Request = data the client sends. Response = data the client receives.</summary>
    public enum Direction { Request, Response }

    public sealed class SpecParseException : Exception
    {
        public SpecParseException(string message) : base(message) { }
        public SpecParseException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class ApiSpec
    {
        public string Title { get; set; } = "";
        public string Version { get; set; } = "";
        public string Format { get; set; } = "";
        /// <summary>Key = "METHOD /normalized/path/{}".</summary>
        public Dictionary<string, ApiOperation> Operations { get; } = new Dictionary<string, ApiOperation>(StringComparer.Ordinal);
    }

    public sealed class ApiOperation
    {
        public string Key { get; set; } = "";
        public string Method { get; set; } = "";
        public string Path { get; set; } = "";
        public string OperationId { get; set; } = "";
        public bool Deprecated { get; set; }
        public List<ApiParameter> Parameters { get; } = new List<ApiParameter>();
        public ApiBody RequestBody { get; set; }
        public Dictionary<string, ApiResponse> Responses { get; } = new Dictionary<string, ApiResponse>(StringComparer.Ordinal);
    }

    public sealed class ApiParameter
    {
        public string Name { get; set; } = "";
        public string In { get; set; } = "";
        public bool Required { get; set; }
        /// <summary>Path params are matched by position (names are not part of the wire contract).</summary>
        public string Key { get; set; } = "";
        public SchemaNode Schema { get; set; } = new SchemaNode();
    }

    public sealed class ApiBody
    {
        public bool Required { get; set; }
        public Dictionary<string, SchemaNode> Content { get; } = new Dictionary<string, SchemaNode>(StringComparer.Ordinal);
    }

    public sealed class ApiResponse
    {
        public Dictionary<string, SchemaNode> Content { get; } = new Dictionary<string, SchemaNode>(StringComparer.Ordinal);
    }

    public sealed class SchemaNode
    {
        static int _nextId;
        public int Id { get; } = Interlocked.Increment(ref _nextId);

        public string RefName { get; set; }
        public string Type { get; set; } = "";
        public string Format { get; set; } = "";
        public bool Nullable { get; set; }
        public bool ReadOnly { get; set; }
        public bool WriteOnly { get; set; }
        public string Pattern { get; set; } = "";
        public double? Minimum { get; set; }
        public double? Maximum { get; set; }
        public double? MinLength { get; set; }
        public double? MaxLength { get; set; }
        public List<string> Enum { get; } = new List<string>();
        public Dictionary<string, SchemaNode> Properties { get; } = new Dictionary<string, SchemaNode>(StringComparer.Ordinal);
        public HashSet<string> Required { get; } = new HashSet<string>(StringComparer.Ordinal);
        public SchemaNode Items { get; set; }
        public List<SchemaNode> OneOf { get; } = new List<SchemaNode>();
    }

    public sealed class ApiChange
    {
        public Severity Severity { get; set; }
        public string Code { get; set; } = "";
        public string Operation { get; set; } = "";
        public string Location { get; set; } = "";
        public string Message { get; set; } = "";
        public bool Suppressed { get; set; }
        public string SuppressionReason { get; set; } = "";
    }
}
