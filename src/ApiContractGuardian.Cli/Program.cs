using System;
using System.IO;
using System.Text;
using ApiContractGuardian.Core;
using ApiContractGuardian.Core.Model;

namespace ApiContractGuardian.Cli
{
    internal static class Program
    {
        // Exit codes: 0 = ok, 1 = changes at/above --fail-on level, 2 = usage / input error
        private static int Main(string[] args)
        {
            string baseline = null, candidate = null, report = null, suppress = null;
            var failOn = FailOn.Breaking;
            try
            {
                for (int i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--report": report = Next(args, ref i); break;
                        case "--suppress": suppress = Next(args, ref i); break;
                        case "--fail-on":
                            var v = Next(args, ref i).ToLowerInvariant();
                            if (v == "breaking") failOn = FailOn.Breaking;
                            else if (v == "warning") failOn = FailOn.Warning;
                            else if (v == "never") failOn = FailOn.Never;
                            else throw new ArgumentException("--fail-on must be breaking, warning or never");
                            break;
                        case "-h": case "--help": return Usage(0);
                        default:
                            if (baseline == null) baseline = args[i];
                            else if (candidate == null) candidate = args[i];
                            else throw new ArgumentException("Unexpected argument: " + args[i]);
                            break;
                    }
                }
                if (baseline == null || candidate == null) return Usage(2);

                if (suppress == null)
                {
                    var auto = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(candidate)) ?? ".", ".apiguardian.json");
                    if (File.Exists(auto)) suppress = auto;
                }

                var result = GuardianEngine.CompareFiles(baseline, candidate, suppress);
                foreach (var c in result.Active)
                    if (c.Severity != Severity.Info)
                        Console.WriteLine("[" + c.Severity.ToString().ToUpperInvariant() + "] " + c.Code + "  " + c.Operation + "  " + c.Location + " - " + c.Message);
                Console.WriteLine();
                Console.WriteLine("Breaking: " + result.BreakingCount + "  Warnings: " + result.WarningCount +
                                  "  Info: " + result.InfoCount + "  Suppressed: " + result.SuppressedCount);

                if (report != null)
                {
                    File.WriteAllText(report, MarkdownReport.Build(result), new UTF8Encoding(false));
                    Console.WriteLine("Report written to " + report);
                }
                if (result.Fails(failOn))
                {
                    Console.Error.WriteLine("FAILED: API contract check did not pass (--fail-on " + failOn.ToString().ToLowerInvariant() + ").");
                    return 1;
                }
                return 0;
            }
            catch (Exception ex) when (ex is SpecParseException || ex is ArgumentException || ex is IOException)
            {
                Console.Error.WriteLine("Error: " + ex.Message);
                return 2;
            }
        }

        private static string Next(string[] a, ref int i)
        {
            if (i + 1 >= a.Length) throw new ArgumentException(a[i] + " requires a value");
            return a[++i];
        }

        private static int Usage(int code)
        {
            Console.WriteLine("apiguard <baseline.json> <candidate.json> [--report report.md] [--fail-on breaking|warning|never] [--suppress .apiguardian.json]");
            return code;
        }
    }
}
