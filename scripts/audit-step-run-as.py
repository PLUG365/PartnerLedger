"""Read-only comparison of packaged PartnerLedger steps and a Dataverse environment.

Usage: python scripts/audit-step-run-as.py --environment https://<org>.crm.dynamics.com/
This script never writes to Dataverse. It does not print user IDs or account names.
"""

import argparse
import json
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path


REPO_ROOT = Path(__file__).resolve().parent.parent
STEP_DIRECTORY = REPO_ROOT / "solutions" / "PartnerLedger" / "SdkMessageProcessingSteps"
CONTEXT = "app=dataverse-skills/1.14.0;skill=dv-query;agent=codex"


def query(table, select, environment, filter_expression):
    command = [
        "dataverse.cmd" if sys.platform == "win32" else "dataverse", "data", "query", "--table", table,
        "--select", select, "--filter", filter_expression,
        "--all", "--max-records", "1000", "--json",
        "--environment", environment, "--context", CONTEXT,
    ]
    result = subprocess.run(command, capture_output=True, text=True, check=False)
    if result.returncode != 0:
        raise RuntimeError(f"Read-only Dataverse query failed for {table}: {result.stderr.strip()}")
    payload = json.loads(result.stdout)
    if not isinstance(payload, dict) or not isinstance(payload.get("value"), list):
        raise RuntimeError(f"Unexpected Dataverse response for {table}")
    if len(payload["value"]) >= 1000:
        raise RuntimeError(f"Query may be truncated for {table}; narrow the filter")
    return payload["value"]


def load_packaged_steps():
    steps = {}
    for path in STEP_DIRECTORY.glob("*.xml"):
        root = ET.parse(path).getroot()
        step_id = root.attrib["SdkMessageProcessingStepId"].strip("{}").lower()
        steps[step_id] = root.attrib["Name"]
    return steps


def load_packaged_configurations():
    configurations = {}
    for path in STEP_DIRECTORY.glob("*.xml"):
        root = ET.parse(path).getroot()
        step_id = root.attrib["SdkMessageProcessingStepId"].strip("{}").lower()
        configurations[step_id] = (root.findtext("Configuration") or "").strip()
    return configurations


def load_packaged_images():
    images = {}
    for path in STEP_DIRECTORY.glob("*.xml"):
        root = ET.parse(path).getroot()
        step_id = root.attrib["SdkMessageProcessingStepId"].strip("{}").lower()
        for image in root.iter("SdkMessageProcessingStepImage"):
            images[image.attrib["Name"]] = (
                image.findtext("SdkMessageProcessingStepImageId").strip("{}").lower(),
                step_id,
            )
    return images


def classify_principals(step_rows, environment):
    principal_ids = sorted({
        row["_impersonatinguserid_value"]
        for row in step_rows if row.get("_impersonatinguserid_value")
    })
    if not principal_ids:
        return {}
    predicates = " or ".join(f"systemuserid eq {principal_id}" for principal_id in principal_ids)
    users = query("systemusers", "systemuserid,applicationid,isdisabled", environment, predicates)
    classifications = {}
    for user in users:
        kind = "ApplicationUser" if user.get("applicationid") else "HumanUser"
        if user.get("isdisabled"):
            kind += "/Disabled"
        classifications[user["systemuserid"].lower()] = kind
    return classifications


def application_user_completion_issues(counts):
    issues = []
    app_user_count = sum(
        value for kind, value in counts.items() if kind.startswith("ApplicationUser")
    )
    disabled_human_count = sum(
        value for kind, value in counts.items() if kind.startswith("HumanUser/Disabled")
    )
    if app_user_count:
        issues.append(f"APP_USER_DEPENDENCY {app_user_count} packaged active steps")
    if disabled_human_count:
        issues.append(f"DISABLED_RUN_AS {disabled_human_count} packaged active steps")
    return issues


def run_as_remaining_issues(counts):
    # 2026-09-27からサーバー処理はSYSTEMで行う。Stepに実行ユーザーが残ると、その人の無効化で処理が止まる。
    remaining = sum(value for kind, value in counts.items() if kind != "Caller")
    return [f"RUN_AS_REMAINS {remaining} packaged active steps"] if remaining else []


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--environment", required=True, help="Target Dataverse environment URL")
    parser.add_argument(
        "--require-no-application-users", action="store_true",
        help="Fail the migration completion gate if an active packaged step runs as an application user or disabled human user",
    )
    parser.add_argument(
        "--require-no-run-as", action="store_true",
        help="Fail if any packaged step still has a Run-as user (plug-ins write through the SYSTEM service)",
    )
    parser.add_argument(
        "--require-package-config-parity", action="store_true",
        help="Fail if packaged Step Configuration differs from the target environment before solution import",
    )
    args = parser.parse_args()

    packaged = load_packaged_steps()
    rows = query(
        "sdkmessageprocessingsteps",
        "sdkmessageprocessingstepid,name,_impersonatinguserid_value,statecode,mode,configuration",
        args.environment,
        "startswith(name,'pl_')",
    )
    cloud = {row["sdkmessageprocessingstepid"].lower(): row for row in rows}
    by_name = {row["name"]: row for row in rows}
    principals = classify_principals(rows, args.environment)
    packaged_images = load_packaged_images()
    packaged_configurations = load_packaged_configurations() if args.require_package_config_parity else {}
    image_filter = " or ".join(
        f"name eq '{name.replace(chr(39), chr(39) * 2)}'" for name in packaged_images
    )
    cloud_images = {
        row["name"]: row for row in query(
            "sdkmessageprocessingstepimages",
            "sdkmessageprocessingstepimageid,name,_sdkmessageprocessingstepid_value",
            args.environment,
            image_filter,
        )
    } if packaged_images else {}
    counts = {"Caller": 0, "ApplicationUser": 0, "HumanUser": 0, "Unknown": 0}
    issues = []

    for step_id, name in sorted(packaged.items(), key=lambda item: item[1]):
        row = cloud.get(step_id)
        if row is None:
            alternate = by_name.get(name)
            issues.append(
                f"ID_MISMATCH {name}: package={step_id} cloud={alternate['sdkmessageprocessingstepid']}"
                if alternate else f"MISSING {name}: package={step_id}"
            )
            continue
        if row["name"] != name:
            issues.append(f"NAME_MISMATCH {step_id}: package={name} cloud={row['name']}")
        if row.get("statecode") != 0 or row.get("mode") != 0:
            issues.append(f"NOT_ACTIVE_SYNC {step_id}: state={row.get('statecode')} mode={row.get('mode')}")
        if args.require_package_config_parity:
            packaged_config = packaged_configurations.get(step_id, "").strip().lower()
            cloud_config = (row.get("configuration") or "").strip().lower()
            if packaged_config != cloud_config:
                issues.append(
                    f"CONFIG_MISMATCH {step_id}: package={'set' if packaged_config else 'empty'} "
                    f"cloud={'set' if cloud_config else 'empty'}"
                )
        principal_id = row.get("_impersonatinguserid_value")
        kind = principals.get(principal_id.lower(), "Unknown") if principal_id else "Caller"
        counts[kind] = counts.get(kind, 0) + 1
        print(f"{step_id[:8]} {kind:18} {name}")

    package_names = set(packaged.values())
    for row in rows:
        if row["name"] not in package_names:
            issues.append(f"CLOUD_ONLY {row['sdkmessageprocessingstepid']}: {row['name']}")

    for name, (image_id, step_id) in packaged_images.items():
        row = cloud_images.get(name)
        if row is None:
            issues.append(f"IMAGE_MISSING {name}: package={image_id}")
        elif row["sdkmessageprocessingstepimageid"].lower() != image_id or row["_sdkmessageprocessingstepid_value"].lower() != step_id:
            issues.append(
                f"IMAGE_MISMATCH {name}: package={image_id}/{step_id} "
                f"cloud={row['sdkmessageprocessingstepimageid']}/{row['_sdkmessageprocessingstepid_value']}"
            )

    if args.require_no_application_users:
        issues.extend(application_user_completion_issues(counts))
    if args.require_no_run_as:
        issues.extend(run_as_remaining_issues(counts))

    print(f"SUMMARY package={len(packaged)} cloud={len(rows)} images={len(packaged_images)} " + " ".join(f"{key}={value}" for key, value in counts.items()))
    for issue in issues:
        print(issue)
    return 1 if issues or counts.get("Unknown", 0) else 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, ValueError, RuntimeError, ET.ParseError) as exc:
        print(f"AUDIT_FAILED {exc}", file=sys.stderr)
        sys.exit(2)
