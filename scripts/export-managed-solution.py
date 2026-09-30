"""配布用のマネージドSolution（zip）を開発環境からexportし、移送元の環境固有の値が入っていないか検査する。

Usage: python scripts/export-managed-solution.py --out <出力フォルダ> [--version 0.1.0.3]
- PAC CLIの有効な認証プロファイル（開発環境）から、PartnerLedgerをマネージドでexportする。
- `--version`を付けたときだけ、export前に開発環境のSolutionの版を設定する（開発環境への書込み）。
- 検査で問題が見つかったzipは削除して終了コード1を返す（配布物として残さない）。
  検査するもの：Plugin Stepの実行ユーザー名（個人名）、環境変数の値ファイル、Dataverse環境のURL、DecisionFlow（`ds_`）への参照、
  ビルドしたPCのパス（DLLを含む全ファイル）。
2026-09-27、開発環境から直接exportしたzipに開発者の個人名が入っていた問題を受けて追加した。
"""

import argparse
import re
import shutil
import subprocess
import sys
import zipfile
from pathlib import Path

RUN_AS_NAME = re.compile(r"<ImpersonatingUserIdName>[^<]+</ImpersonatingUserIdName>")
ENVIRONMENT_URL = re.compile(
    r"https?://[a-z0-9.-]+\.(?:crm\d*\.dynamics\.com|crm\d*\.dynamics\.cn|crm\d*\.microsoftdynamics\.(?:us|de)|crm\d*\.appsplatform\.us)",
    re.IGNORECASE,
)
DECISIONFLOW_REFERENCE = re.compile(r"\bds_[a-z]", re.IGNORECASE)
# ビルドしたPCのパス。DLLにはPDBの場所として入る（2026-09-28に見つけた。csprojのPathMapで消す）。
LOCAL_PATH = re.compile(rb"[A-Za-z]:\\(?:Users|home)\\", re.IGNORECASE)
BINARY_SUFFIXES = (".dll", ".png", ".jpg", ".jpeg", ".gif", ".ico", ".woff", ".woff2")


def find_package_problems(zip_path):
    """zipの中身を検査し、問題の一覧を返す（値そのものは出さない）。"""
    problems = []
    with zipfile.ZipFile(zip_path) as archive:
        for name in sorted(archive.namelist()):
            if name.lower().endswith("environmentvariablevalues.json"):
                problems.append(f"ENV_VALUE_FILE {name}")
                continue
            data = archive.read(name)
            if LOCAL_PATH.search(data):
                problems.append(f"LOCAL_PATH in {name}")
            if name.lower().endswith(BINARY_SUFFIXES):
                continue
            text = data.decode("utf-8", errors="ignore")
            run_as = len(RUN_AS_NAME.findall(text))
            if run_as:
                problems.append(f"RUN_AS_NAME {run_as} steps in {name}")
            if ENVIRONMENT_URL.search(text):
                problems.append(f"ENVIRONMENT_URL in {name}")
            if DECISIONFLOW_REFERENCE.search(text):
                problems.append(f"DECISIONFLOW_REFERENCE in {name}")
    return problems


def run(args):
    result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        raise RuntimeError(f"{args[0]} failed: {result.stdout}\n{result.stderr}")
    return result.stdout


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--out", required=True, help="zipを置くフォルダ（リポジトリの外を推奨）")
    parser.add_argument("--version", help="export前に開発環境のSolutionへ設定する版（例：0.1.0.3）")
    args = parser.parse_args()

    pac = "pac.cmd" if sys.platform == "win32" and shutil.which("pac.cmd") else "pac"
    out_dir = Path(args.out)
    out_dir.mkdir(parents=True, exist_ok=True)
    if args.version:
        run([pac, "solution", "online-version", "--solution-name", "PartnerLedger", "--solution-version", args.version])
    zip_path = out_dir / "PartnerLedger_managed.zip"
    run([pac, "solution", "export", "--name", "PartnerLedger", "--path", str(zip_path), "--managed", "true", "--overwrite"])

    problems = find_package_problems(zip_path)
    if problems:
        zip_path.unlink()
        for problem in problems:
            print(problem)
        print("NG：問題のあるzipは削除しました。開発環境の設定を直してから、もう一度exportしてください。")
        return 1

    with zipfile.ZipFile(zip_path) as archive:
        manifest = archive.read("solution.xml").decode("utf-8", errors="ignore")
    version = re.search(r"<Version>([^<]+)</Version>", manifest)
    final_path = out_dir / f"PartnerLedger_{(version.group(1) if version else 'unknown').replace('.', '_')}_managed.zip"
    zip_path.replace(final_path)
    print(f"OK：{final_path}（環境固有の値は見つかりませんでした）")
    return 0


if __name__ == "__main__":
    try:
        sys.exit(main())
    except (OSError, RuntimeError, zipfile.BadZipFile) as exc:
        print(f"EXPORT_FAILED {exc}", file=sys.stderr)
        sys.exit(2)
