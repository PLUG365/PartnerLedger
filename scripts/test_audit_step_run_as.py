"""Contract tests for the read-only Step Run-as completion gate."""

import contextlib
import importlib.util
import io
import sys
import unittest
from pathlib import Path
from unittest.mock import patch


SCRIPT_PATH = Path(__file__).with_name("audit-step-run-as.py")
SPEC = importlib.util.spec_from_file_location("audit_step_run_as", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)
STEP_ID = "088d1313-74ae-f111-aaab-e4fade057b20"


class RunAsCompletionGateTests(unittest.TestCase):
    def run_audit(self, user_kind, strict=True, package_parity=False, package_config="", cloud_config=None, no_run_as=False):
        step = {
            "sdkmessageprocessingstepid": STEP_ID,
            "name": "pl_partner Create Test",
            "_impersonatinguserid_value": None if user_kind == "Caller" else STEP_ID,
            "statecode": 0,
            "mode": 0,
            "configuration": cloud_config,
        }

        def query(table, *_args):
            if table == "sdkmessageprocessingsteps":
                return [step]
            if table == "systemusers":
                return [{
                    "systemuserid": STEP_ID,
                    "applicationid": STEP_ID if user_kind.startswith("ApplicationUser") else None,
                    "isdisabled": user_kind.endswith("Disabled"),
                }]
            raise AssertionError(f"Unexpected read: {table}")

        argv = ["audit-step-run-as.py", "--environment", "https://example.crm.dynamics.com/"]
        if strict:
            argv.append("--require-no-application-users")
        if package_parity:
            argv.append("--require-package-config-parity")
        if no_run_as:
            argv.append("--require-no-run-as")
        with patch.object(sys, "argv", argv), \
             patch.object(MODULE, "query", side_effect=query), \
             patch.object(MODULE, "load_packaged_steps", return_value={STEP_ID: step["name"]}), \
             patch.object(MODULE, "load_packaged_configurations", return_value={STEP_ID: package_config}), \
             patch.object(MODULE, "load_packaged_images", return_value={}), \
             contextlib.redirect_stdout(io.StringIO()) as output:
            result = MODULE.main()
        return result, output.getvalue()

    def test_application_user_fails_completion_gate(self):
        result, output = self.run_audit("ApplicationUser")
        self.assertEqual(result, 1)
        self.assertIn("APP_USER_DEPENDENCY 1", output)

    def test_existing_inventory_mode_does_not_claim_completion(self):
        result, output = self.run_audit("ApplicationUser", strict=False)
        self.assertEqual(result, 0)
        self.assertIn("ApplicationUser=1", output)
        self.assertNotIn("APP_USER_DEPENDENCY", output)

    def test_calling_user_passes_completion_gate(self):
        result, _output = self.run_audit("Caller")
        self.assertEqual(result, 0)

    def test_active_human_run_as_passes_completion_gate(self):
        result, _output = self.run_audit("HumanUser")
        self.assertEqual(result, 0)

    def test_disabled_human_run_as_fails_completion_gate(self):
        result, output = self.run_audit("HumanUser/Disabled")
        self.assertEqual(result, 1)
        self.assertIn("DISABLED_RUN_AS 1", output)

    def test_unknown_principal_fails_existing_integrity_gate(self):
        with patch.object(MODULE, "classify_principals", return_value={}):
            result, output = self.run_audit("HumanUser")
        self.assertEqual(result, 1)
        self.assertIn("Unknown=1", output)

    def test_remaining_run_as_fails_no_run_as_gate(self):
        # 2026-09-27：サーバー処理はSYSTEMで行うため、Stepに実行ユーザーを残さない（無効化で止まる）。
        result, output = self.run_audit("HumanUser", strict=False, no_run_as=True)
        self.assertEqual(result, 1)
        self.assertIn("RUN_AS_REMAINS 1", output)

    def test_calling_user_passes_no_run_as_gate(self):
        result, output = self.run_audit("Caller", strict=False, no_run_as=True)
        self.assertEqual(result, 0)
        self.assertNotIn("RUN_AS_REMAINS", output)

    def test_package_config_parity_rejects_cloud_only_legacy_value(self):
        result, output = self.run_audit("Caller", package_parity=True, cloud_config="legacy-guid")
        self.assertEqual(result, 1)
        self.assertIn("CONFIG_MISMATCH", output)
        self.assertNotIn("legacy-guid", output)

    def test_package_config_parity_accepts_matching_value(self):
        result, output = self.run_audit(
            "Caller", package_parity=True, package_config="internal-execution", cloud_config="internal-execution"
        )
        self.assertEqual(result, 0)
        self.assertNotIn("CONFIG_MISMATCH", output)


if __name__ == "__main__":
    unittest.main()
