using System;
using System.ComponentModel.Design;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using ApiContractGuardian.Core;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Task = System.Threading.Tasks.Task;

namespace ApiContractGuardian.Vsix
{
    /// <summary>
    /// Tools > "API Contract Guardian: Compare Specs...".
    /// 1) pick baseline (what is in production)  2) pick candidate (new swagger.json)
    /// 3) engine runs off the UI thread, report is written next to the candidate and opened
    /// 4) the developer reviews it and explicitly approves (or not) the candidate as the new baseline.
    /// </summary>
    internal sealed class CompareCommand
    {
        public static readonly Guid CommandSet = new Guid("5C8D2F14-3B7A-4E90-9D21-6A4B8C0E1F33");
        public const int CommandId = 0x0100;
        private const string Title = "API Contract Guardian";

        private readonly AsyncPackage _package;

        private CompareCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            _package = package;
            commandService.AddCommand(new MenuCommand(Execute, new CommandID(CommandSet, CommandId)));
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            var service = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            if (service != null) new CompareCommand(package, service);
        }

        private void Execute(object sender, EventArgs e)
        {
            _package.JoinableTaskFactory.RunAsync(RunSafeAsync);
        }

        private async Task RunSafeAsync()
        {
            try { await RunAsync(); }
            catch (Exception ex)
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                Show("Unexpected error: " + ex.Message, OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK);
            }
        }

        private async Task RunAsync()
        {
            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();

            var baseline = Pick("Select the BASELINE spec (the version currently in production)");
            if (baseline == null) return;
            var candidate = Pick("Select the CANDIDATE spec (the new swagger.json / openapi.json)");
            if (candidate == null) return;
            if (string.Equals(Path.GetFullPath(baseline), Path.GetFullPath(candidate), StringComparison.OrdinalIgnoreCase))
            {
                Show("Baseline and candidate are the same file.", OLEMSGICON.OLEMSGICON_WARNING, OLEMSGBUTTON.OLEMSGBUTTON_OK);
                return;
            }

            ComparisonResult result;
            string reportPath;
            try
            {
                await TaskScheduler.Default; // heavy work off the UI thread
                var dir = Path.GetDirectoryName(Path.GetFullPath(candidate)) ?? ".";
                var suppressions = Path.Combine(dir, ".apiguardian.json");
                result = GuardianEngine.CompareFiles(baseline, candidate, suppressions);
                reportPath = Path.Combine(dir, "api-contract-report.md");
                File.WriteAllText(reportPath, MarkdownReport.Build(result), new UTF8Encoding(false));
            }
            catch (Exception ex)
            {
                await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
                Show("Comparison failed:\n\n" + ex.Message, OLEMSGICON.OLEMSGICON_CRITICAL, OLEMSGBUTTON.OLEMSGBUTTON_OK);
                return;
            }

            await _package.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await _package.GetServiceAsync(typeof(SDTE)) as DTE2;
            if (dte != null) dte.ItemOperations.OpenFile(reportPath);

            bool breaking = result.BreakingCount > 0;
            var text = "Breaking: " + result.BreakingCount + "\nWarnings: " + result.WarningCount +
                       "\nInfo: " + result.InfoCount + "\nAccepted: " + result.SuppressedCount +
                       "\n\nReport: " + reportPath + "\n\n" +
                       (breaking
                           ? "BREAKING CHANGES FOUND. Replace the baseline with the candidate anyway?"
                           : "No breaking changes. Approve the candidate as the new baseline?");
            var answer = Show(text, breaking ? OLEMSGICON.OLEMSGICON_WARNING : OLEMSGICON.OLEMSGICON_INFO,
                OLEMSGBUTTON.OLEMSGBUTTON_YESNO, OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_SECOND);
            if (answer == 6) // IDYES
                File.Copy(candidate, baseline, true);
        }

        private static string Pick(string title)
        {
            using (var dlg = new OpenFileDialog
            {
                Title = title,
                Filter = "OpenAPI / Swagger JSON (*.json)|*.json|All files (*.*)|*.*",
                CheckFileExists = true
            })
            {
                return dlg.ShowDialog() == DialogResult.OK ? dlg.FileName : null;
            }
        }

        private int Show(string text, OLEMSGICON icon, OLEMSGBUTTON buttons, OLEMSGDEFBUTTON def = OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return VsShellUtilities.ShowMessageBox(_package, text, Title, icon, buttons, def);
        }
    }
}
