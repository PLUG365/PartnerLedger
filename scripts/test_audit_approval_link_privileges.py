"""Unit tests for the read-only approval-result role-definition gate."""

import importlib.util
import unittest
from pathlib import Path


SCRIPT_PATH = Path(__file__).with_name("audit-approval-link-privileges.py")
SPEC = importlib.util.spec_from_file_location("audit_approval_link_privileges", SCRIPT_PATH)
MODULE = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(MODULE)


def privileges(*names_and_depths):
    return [{"PrivilegeName": name, "Depth": depth} for name, depth in names_and_depths]


class ApprovalLinkRoleGateTests(unittest.TestCase):
    def setUp(self):
        self.roles = {
            "PL 利用者": [privileges()],
            "PL 承認": [privileges()],
            "PL システム保守": [privileges((MODULE.WRITE_PRIVILEGE, "Global"))],
        }

    def test_expected_role_boundary_passes(self):
        self.assertEqual([], MODULE.assess(self.roles))

    def test_user_or_approver_write_fails(self):
        for name in ("PL 利用者", "PL 承認"):
            with self.subTest(name=name):
                roles = {key: list(value) for key, value in self.roles.items()}
                roles[name] = [privileges((MODULE.WRITE_PRIVILEGE, "Basic"))]
                self.assertIn(f"UNEXPECTED_APPROVAL_LINK_WRITE {MODULE.ROLE_LABELS[name]}", MODULE.assess(roles))

    def test_second_business_unit_copy_is_checked(self):
        self.roles["PL 承認"].append(privileges((MODULE.WRITE_PRIVILEGE, "Global")))
        self.assertIn("UNEXPECTED_APPROVAL_LINK_WRITE approver", MODULE.assess(self.roles))

    def test_missing_role_or_global_maintenance_write_fails(self):
        self.roles["PL 利用者"] = []
        self.roles["PL システム保守"] = [privileges((MODULE.WRITE_PRIVILEGE, "Basic"))]
        issues = MODULE.assess(self.roles)
        self.assertIn("MISSING_ROLE user", issues)
        self.assertIn("MISSING_GLOBAL_APPROVAL_LINK_WRITE maintenance", issues)


if __name__ == "__main__":
    unittest.main()
