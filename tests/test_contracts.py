import unittest
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]


class PortableWindowsAppContractTests(unittest.TestCase):
    def test_required_standalone_app_contracts_exist(self):
        expected = [
            "src/PortableSyncthing.Core/PortableSyncthing.Core.csproj",
            "src/PortableSyncthing.Core/PortableRoot.cs",
            "src/PortableSyncthing.Core/SyncthingConfigRemapper.cs",
            "src/PortableSyncthing.Core/SyncthingLaunchPlan.cs",
            "src/PortableSyncthing.Windows/PortableSyncthing.Windows.csproj",
            "src/PortableSyncthing.Windows/MainWindow.xaml",
            "src/PortableSyncthing.Windows/MainWindow.xaml.cs",
            "README.md",
        ]
        missing = [path for path in expected if not (ROOT / path).is_file()]
        self.assertEqual([], missing, f"Missing portable app contracts: {missing}")


if __name__ == "__main__":
    unittest.main()
