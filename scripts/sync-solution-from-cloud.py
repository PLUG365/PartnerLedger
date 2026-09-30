"""PartnerLedger SolutionをクラウドからexportしてリポジトリのSolutionフォルダへ反映する。

Usage: python scripts/sync-solution-from-cloud.py
- PAC CLIの有効な認証プロファイル（開発環境）からUnmanagedでexport／unpackする。
- 環境固有の値はリポジトリへ入れない：environmentvariablevalues.json を除外し、
  Plugin StepのRun-as利用者名（ImpersonatingUserIdName）を空にする。
- 人向け4 RoleのXMLはリポジトリ側（設計上の権限）を正とし、上書きしない。
  クラウドへ未適用の権限差分は開発記録（Git管理外）で管理する。
Dataverseへは書き込まない（exportは読み取り操作）。
"""

import re
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
SOLUTION_DIR = REPO_ROOT / "solutions" / "PartnerLedger"
DESIGNED_ROLES = ("PL システム保守.xml", "PL システム管理.xml", "PL 利用者.xml", "PL 承認.xml")
RUN_AS_NAME = re.compile(r"<ImpersonatingUserIdName>[^<]*</ImpersonatingUserIdName>")


def long_path(path: Path) -> str:
    # Windowsの260文字制限を避ける。
    text = str(path.resolve())
    return "\\\\?\\" + text if sys.platform == "win32" and not text.startswith("\\\\?\\") else text


def run(args):
    result = subprocess.run(args, capture_output=True, text=True, encoding="utf-8", errors="replace")
    if result.returncode != 0:
        raise RuntimeError(f"{args[0]} failed: {result.stdout}\n{result.stderr}")


def main() -> None:
    pac = "pac.cmd" if sys.platform == "win32" and shutil.which("pac.cmd") else "pac"
    with tempfile.TemporaryDirectory() as work:
        zip_path = Path(work) / "PartnerLedger.zip"
        unpacked = Path(work) / "unpacked"
        run([pac, "solution", "export", "--name", "PartnerLedger", "--path", str(zip_path), "--managed", "false", "--overwrite"])
        run([pac, "solution", "unpack", "--zipfile", str(zip_path), "--folder", str(unpacked), "--packagetype", "Unmanaged"])

        designed = {name: (SOLUTION_DIR / "Roles" / name).read_bytes() for name in DESIGNED_ROLES}
        shutil.rmtree(long_path(SOLUTION_DIR))
        shutil.copytree(
            long_path(unpacked),
            long_path(SOLUTION_DIR),
            ignore=lambda _dir, names: [n for n in names if n == "environmentvariablevalues.json"],
        )

    for name, content in designed.items():
        (SOLUTION_DIR / "Roles" / name).write_bytes(content)

    blanked = 0
    for step in (SOLUTION_DIR / "SdkMessageProcessingSteps").glob("*.xml"):
        raw = step.read_bytes()
        bom = raw.startswith(b"\xef\xbb\xbf")
        text = raw.decode("utf-8-sig")
        cleaned = RUN_AS_NAME.sub("<ImpersonatingUserIdName></ImpersonatingUserIdName>", text)
        if cleaned != text:
            step.write_bytes((b"\xef\xbb\xbf" if bom else b"") + cleaned.encode("utf-8"))
            blanked += 1

    print(f"synced {SOLUTION_DIR.relative_to(REPO_ROOT)}; run-as names blanked in {blanked} steps")


if __name__ == "__main__":
    main()
