using System;
using System.IO;
using System.Linq;
using Xunit;

namespace PartnerLedger.Plugins.Tests
{
    /// <summary>
    /// 他環境へimportするSolution成果物に、DecisionFlowへの依存と環境固有の値が入らないことを固定する。
    /// 対象はリポジトリのunpack済みSolution（クラウドからexportした正本）。
    /// </summary>
    public sealed class SolutionPackageBoundaryTests
    {
        [Fact]
        public void SolutionはDecisionFlowのテーブルと参照列を含まない()
        {
            var solution = SolutionDirectory();
            var offending = Directory.EnumerateFiles(solution, "*", SearchOption.AllDirectories)
                .Where(path => IsText(path))
                .Where(path =>
                {
                    var text = File.ReadAllText(path);
                    return text.IndexOf("ds_application", StringComparison.OrdinalIgnoreCase) >= 0
                        || text.IndexOf("pl_applicationlookup", StringComparison.OrdinalIgnoreCase) >= 0;
                })
                .Select(path => Path.GetRelativePath(solution, path))
                .ToList();

            Assert.Empty(offending);
            Assert.False(Directory.Exists(Path.Combine(solution, "Entities", "ds_application")));
        }

        [Fact]
        public void Solution_xmlのRootComponentsとMissingDependenciesにDecisionFlowが無い()
        {
            var solutionXml = File.ReadAllText(Path.Combine(SolutionDirectory(), "Other", "Solution.xml"));

            Assert.DoesNotContain("schemaName=\"ds_", solutionXml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("DecisionFlow", solutionXml, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void 環境変数の値ファイルをSolutionへ含めない()
        {
            var values = Directory.EnumerateFiles(SolutionDirectory(), "environmentvariablevalues.json", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(SolutionDirectory(), path))
                .ToList();

            Assert.Empty(values);
        }

        [Fact]
        public void Plugin_StepのRun_as利用者名をSolutionへ含めない()
        {
            // Run-asは移送先で管理者が設定する環境固有の値。exportに付く個人名は正本へ残さない。
            var withRunAsName = Directory.EnumerateFiles(Path.Combine(SolutionDirectory(), "SdkMessageProcessingSteps"), "*.xml")
                .Where(path => System.Text.RegularExpressions.Regex.IsMatch(
                    File.ReadAllText(path),
                    "<ImpersonatingUserIdName>[^<]+</ImpersonatingUserIdName>"))
                .Select(Path.GetFileName)
                .ToList();

            Assert.Empty(withRunAsName);
        }

        private static bool IsText(string path)
        {
            var extension = Path.GetExtension(path).ToLowerInvariant();
            return extension == ".xml" || extension == ".json";
        }

        private static string SolutionDirectory()
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "solutions", "PartnerLedger")))
            {
                directory = directory.Parent;
            }

            return directory == null
                ? throw new InvalidOperationException("リポジトリのルートが見つからない。")
                : Path.Combine(directory.FullName, "solutions", "PartnerLedger");
        }
    }
}
