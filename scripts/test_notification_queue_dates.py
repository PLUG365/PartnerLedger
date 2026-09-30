"""日次通知のキュー作成フローが、対象日を日本時間で数えることを検査する（2026-09-30）。

フローは7:00（日本時間）に動く。その時点のUTCは前日の22:00なので、utcNow()の日付で数えると
「30日前・7日前・1日前」が29日前・6日前・当日に、「確認待ち3日」が4日後にずれていた。
契約の日付はアプリが「その日のUTC 0時」で保存し、UTCの日付として読むので、記録の側はそのまま比べる。
"""

import json
import re
import unittest
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
QUEUE_FLOW = next((REPO_ROOT / "solutions" / "PartnerLedger" / "Workflows").glob(
    "PartnerLedger_NotificationDaily_v1_QueueStopped-*.json"))
TOKYO_TODAY = "convertFromUtc(utcNow(),'Tokyo Standard Time')"


def definition_text() -> str:
    data = json.loads(QUEUE_FLOW.read_text(encoding="utf-8"))
    return json.dumps(data["properties"]["definition"], ensure_ascii=False)


class NotificationQueueDateTests(unittest.TestCase):
    def test_contract_targets_count_days_from_today_in_tokyo(self):
        text = definition_text()
        for days in (30, 7, 1):
            self.assertIn(f"formatDateTime(addDays({TOKYO_TODAY},{days}),'yyyy-MM-dd')", text)

    def test_card_backlog_compares_tokyo_dates(self):
        text = definition_text()
        self.assertIn(f"formatDateTime(addDays({TOKYO_TODAY},-3),'yyyy-MM-dd')", text)
        self.assertIn("formatDateTime(convertFromUtc(items('ForEach_CardCandidate')?['createdon'],'Tokyo Standard Time'),'yyyy-MM-dd')", text)
        # 候補の読み出しは、日本時間で3日前に作られた名刺を取りこぼさない幅にする。
        self.assertIn("createdon lt '@{addDays(utcNow(), -2)}'", text)

    def test_idempotency_keys_use_the_tokyo_date(self):
        text = definition_text()
        self.assertEqual(4, text.count(f"formatDateTime({TOKYO_TODAY},'yyyy-MM-dd')"))

    def test_no_date_is_counted_from_utc_today(self):
        text = definition_text()
        self.assertIsNone(re.search(r"addDays\(utcNow\(\),\s*-?\d+\),'yyyy-MM-dd'", text))
        self.assertNotIn("formatDateTime(utcNow(),'yyyy-MM-dd')", text)


if __name__ == "__main__":
    unittest.main()
