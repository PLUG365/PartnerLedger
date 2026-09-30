"""配布用zipの検査（export-managed-solution.py）の単体テスト。クラウドには触らない。"""

import importlib.util
import tempfile
import unittest
import zipfile
from pathlib import Path


SCRIPT_PATH = Path(__file__).with_name("export-managed-solution.py")
SPEC = importlib.util.spec_from_file_location("export_managed_solution", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)

CLEAN_STEP = (
    '<SdkMessageProcessingStep Name="pl_partner Update PreOperation: PartnerStandardUpdate">'
    "<ImpersonatingUserIdName></ImpersonatingUserIdName></SdkMessageProcessingStep>"
)


class PackageProblemTests(unittest.TestCase):
    def build_zip(self, customizations, extra=None):
        directory = Path(tempfile.mkdtemp())
        path = directory / "PartnerLedger_managed.zip"
        with zipfile.ZipFile(path, "w") as archive:
            archive.writestr("customizations.xml", customizations)
            archive.writestr("solution.xml", "<ImportExportXml><SolutionManifest /></ImportExportXml>")
            for name, content in (extra or {}).items():
                archive.writestr(name, content)
        return path

    def test_clean_package_has_no_problems(self):
        self.assertEqual(MODULE.find_package_problems(self.build_zip(CLEAN_STEP)), [])

    def test_step_run_as_name_is_rejected(self):
        # 2026-09-27：開発環境から直接exportしたzipに開発者の個人名が13か所入っていた。
        package = self.build_zip(CLEAN_STEP.replace("></ImpersonatingUserIdName>", ">Someone</ImpersonatingUserIdName>"))
        problems = MODULE.find_package_problems(package)
        self.assertEqual(len(problems), 1)
        self.assertIn("RUN_AS_NAME 1", problems[0])
        self.assertNotIn("Someone", problems[0])

    def test_environment_variable_values_are_rejected(self):
        package = self.build_zip(CLEAN_STEP, {"environmentvariabledefinitions/x/environmentvariablevalues.json": "{}"})
        self.assertTrue(any(problem.startswith("ENV_VALUE_FILE") for problem in MODULE.find_package_problems(package)))

    def test_dataverse_environment_url_is_rejected(self):
        package = self.build_zip(CLEAN_STEP + "<x>https://contoso.crm7.dynamics.com/main.aspx</x>")
        self.assertTrue(any(problem.startswith("ENVIRONMENT_URL") for problem in MODULE.find_package_problems(package)))

    def test_api_and_sovereign_cloud_urls_are_rejected(self):
        for url in ("https://contoso.api.crm7.dynamics.com/api/data/v9.2/", "https://contoso.crm.microsoftdynamics.us/", "https://contoso.crm.dynamics.cn/"):
            package = self.build_zip(CLEAN_STEP + f"<x>{url}</x>")
            self.assertTrue(any(problem.startswith("ENVIRONMENT_URL") for problem in MODULE.find_package_problems(package)), url)

    def test_decisionflow_reference_is_rejected(self):
        package = self.build_zip(CLEAN_STEP + '<attribute PhysicalName="ds_applicationid" />')
        self.assertTrue(any(problem.startswith("DECISIONFLOW_REFERENCE") for problem in MODULE.find_package_problems(package)))

    def test_local_path_inside_dll_is_rejected(self):
        # 2026-09-28：DLLにビルドしたPCのパス（PDBの場所）が入っていた。DLLは文字列検査の対象外だった。
        dll = b"MZ\x90\x00" + b"C:\\Users\\someone\\src\\obj\\Release\\net462\\PartnerLedger.Plugins.pdb" + b"\x00" * 8
        package = self.build_zip(CLEAN_STEP, {"PluginAssemblies/x/PartnerLedgerPlugins.dll": dll})
        problems = MODULE.find_package_problems(package)
        self.assertEqual(len(problems), 1)
        self.assertTrue(problems[0].startswith("LOCAL_PATH"), problems)
        self.assertNotIn("someone", problems[0])

    def test_mapped_pdb_path_in_dll_is_allowed(self):
        dll = b"MZ\x90\x00" + b"/_/PartnerLedger.Plugins/obj/Release/net462/PartnerLedger.Plugins.pdb" + b"\x00" * 8
        package = self.build_zip(CLEAN_STEP, {"PluginAssemblies/x/PartnerLedgerPlugins.dll": dll})
        self.assertEqual(MODULE.find_package_problems(package), [])

    def test_similar_words_are_not_mistaken_for_decisionflow(self):
        package = self.build_zip(CLEAN_STEP + "<fields_value>ids_list</fields_value>")
        self.assertEqual(MODULE.find_package_problems(package), [])


if __name__ == "__main__":
    unittest.main()
