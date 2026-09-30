"""Static integrity checks for packaged Plugin Step references; no cloud access."""

import unittest
import xml.etree.ElementTree as ET
from pathlib import Path


ROOT = Path(__file__).resolve().parent.parent / "solutions" / "PartnerLedger"
STEP_DIRECTORY = ROOT / "SdkMessageProcessingSteps"
ASSEMBLY_DATA = next((ROOT / "PluginAssemblies").glob("*/PartnerLedgerPlugins.dll.data.xml"))


def normalize(value):
    return value.strip("{}").lower()


class StepPackageIntegrityTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        manifest = ET.parse(ROOT / "Other" / "Solution.xml").getroot()
        cls.root_step_ids = {
            normalize(component.attrib["id"])
            for component in manifest.iter("RootComponent")
            if component.attrib.get("type") == "92"
        }
        assembly = ET.parse(ASSEMBLY_DATA).getroot()
        cls.plugin_types = {
            item.attrib["AssemblyQualifiedName"]: normalize(item.attrib["PluginTypeId"])
            for item in assembly.iter("PluginType")
        }

    def test_step_ids_and_plugin_types_match_package(self):
        seen_ids = set()
        seen_names = set()
        for path in STEP_DIRECTORY.glob("*.xml"):
            root = ET.parse(path).getroot()
            step_id = normalize(root.attrib["SdkMessageProcessingStepId"])
            with self.subTest(step=root.attrib["Name"]):
                self.assertEqual(normalize(path.stem), step_id)
                self.assertIn(step_id, self.root_step_ids)
                self.assertNotIn(step_id, seen_ids)
                self.assertNotIn(root.attrib["Name"], seen_names)
                plugin_name = root.findtext("PluginTypeName")
                self.assertIn(plugin_name, self.plugin_types)
                self.assertEqual(normalize(root.findtext("PluginTypeId")), self.plugin_types[plugin_name])
            seen_ids.add(step_id)
            seen_names.add(root.attrib["Name"])
        self.assertEqual(self.root_step_ids, seen_ids)


if __name__ == "__main__":
    unittest.main()
