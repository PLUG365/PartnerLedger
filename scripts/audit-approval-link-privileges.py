"""Read-only development-environment role-definition gate for the approval-result ingress.

This checks named role definitions, not a user's cumulative effective privileges.
No IDs, credentials, or privilege payloads are printed.
"""

import argparse
import json
import subprocess
import sys
import uuid


CONTEXT = "app=dataverse-skills/1.14.0;skill=dv-security;agent=codex"
ROLE_NAMES = ("PL 利用者", "PL 承認", "PL システム保守")
ROLE_LABELS = {"PL 利用者": "user", "PL 承認": "approver", "PL システム保守": "maintenance"}
WRITE_PRIVILEGE = "prvWritepl_ApprovalLink"


def dataverse(*args):
    executable = "dataverse.cmd" if sys.platform == "win32" else "dataverse"
    result = subprocess.run(
        [executable, "--context", CONTEXT, *args],
        capture_output=True, text=True, check=False,
    )
    if result.returncode != 0:
        raise RuntimeError("Read-only Dataverse command failed")
    try:
        return json.loads(result.stdout)
    except json.JSONDecodeError as error:
        raise RuntimeError("Dataverse did not return JSON") from error


def load_roles(environment):
    expression = " or ".join(f"name eq '{name}'" for name in ROLE_NAMES)
    payload = dataverse(
        "data", "query", "--table", "roles", "--select", "roleid,name",
        "--filter", expression, "--all", "--max-records", "1000", "--json",
        "--environment", environment,
    )
    rows = payload.get("value") if isinstance(payload, dict) else None
    if not isinstance(rows, list) or len(rows) >= 1000:
        raise RuntimeError("Role query missing or potentially truncated")
    for row in rows:
        if not isinstance(row, dict) or row.get("name") not in ROLE_NAMES:
            raise RuntimeError("Role query returned an unexpected row")
        try:
            uuid.UUID(row["roleid"])
        except (KeyError, TypeError, ValueError) as error:
            raise RuntimeError("Role query returned an invalid ID") from error
    return rows


def load_privileges(environment, role_id):
    # RoleId comes from the Dataverse role query, not from user input.
    payload = dataverse(
        "api", "request", "--target", "dataverse", "--method", "GET",
        "--path", f"/api/data/v9.2/RetrieveRolePrivilegesRole(RoleId={role_id})",
        "--environment", environment,
    )
    rows = payload.get("RolePrivileges") if isinstance(payload, dict) else None
    if not isinstance(rows, list) or any(
        not isinstance(row, dict) or not isinstance(row.get("PrivilegeName"), str)
        for row in rows
    ):
        raise RuntimeError("Role privilege response is incomplete")
    return rows


def assess(rows_by_name):
    issues = []
    for name in ROLE_NAMES:
        rows = rows_by_name.get(name, [])
        if not rows:
            issues.append(f"MISSING_ROLE {ROLE_LABELS[name]}")
            continue
        for privileges in rows:
            writes = [row for row in privileges if row["PrivilegeName"] == WRITE_PRIVILEGE]
            if name != "PL システム保守" and writes:
                issues.append(f"UNEXPECTED_APPROVAL_LINK_WRITE {ROLE_LABELS[name]}")
            if name == "PL システム保守" and not any(row.get("Depth") == "Global" for row in writes):
                issues.append("MISSING_GLOBAL_APPROVAL_LINK_WRITE maintenance")
    return issues


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--environment", required=True, help="Target Dataverse environment URL")
    args = parser.parse_args()
    try:
        rows_by_name = {name: [] for name in ROLE_NAMES}
        for role in load_roles(args.environment):
            rows_by_name[role["name"]].append(load_privileges(args.environment, role["roleid"]))
        issues = assess(rows_by_name)
    except RuntimeError as error:
        print(f"FAIL {error}")
        return 1
    for name, rows in rows_by_name.items():
        print(f"{ROLE_LABELS[name]}: role definitions={len(rows)}")
    for issue in issues:
        print(issue)
    if not issues:
        print("PASS: approval-result role-definition boundary (effective user access not checked)")
    return 1 if issues else 0


if __name__ == "__main__":
    sys.exit(main())
