import subprocess
import sys
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
            ".github/workflows/ci.yml",
            ".github/workflows/release.yml",
            "scripts/next-date-build-version.py",
            "scripts/package-windows.ps1",
        ]
        missing = [path for path in expected if not (ROOT / path).is_file()]
        self.assertEqual([], missing, f"Missing portable app contracts: {missing}")
        self.assertFalse((ROOT / "VERSION.txt").is_file(), "VERSION.txt is stamped into release zips, not committed")

    def test_version_helper_self_test_and_from_tag(self):
        script = ROOT / "scripts/next-date-build-version.py"
        subprocess.run([sys.executable, str(script), "--self-test"], check=True)
        version = subprocess.check_output(
            [sys.executable, str(script), "--from-tag", "v2026.9.23.1"],
            text=True,
        ).strip()
        self.assertEqual("2026.9.23.1", version)
        rejected = subprocess.run(
            [sys.executable, str(script), "--from-tag", "v2026.09.23.1"],
            capture_output=True,
            text=True,
        )
        self.assertNotEqual(0, rejected.returncode)

    def test_ci_workflow_verifies_without_publishing_a_date_build(self):
        text = (ROOT / ".github/workflows/ci.yml").read_text(encoding="utf-8")
        self.assertIn("windows-latest", text)
        self.assertIn("contents: read", text)
        self.assertIn("python tests/test_contracts.py", text)
        self.assertIn("dotnet run --project tests/PortableSyncthing.ContractTests", text)
        self.assertIn(
            "dotnet build src/PortableSyncthing.Windows/PortableSyncthing.Windows.csproj -c Release",
            text,
        )
        self.assertIn("PortableSyncthing-ci-${{ github.sha }}-win-x64", text)
        self.assertNotIn("gh release", text)
        for line in text.splitlines():
            if "next-date-build-version.py" in line:
                self.assertIn("--self-test", line)

    def test_release_workflow_gates_publish_and_pr_verification(self):
        text = (ROOT / ".github/workflows/release.yml").read_text(encoding="utf-8")
        self.assertIn("pull_request:", text)
        self.assertIn("workflow_dispatch:", text)
        self.assertIn('- "v*"', text)
        self.assertIn("--from-tag", text)
        self.assertIn("contents: write", text)
        self.assertIn("contents: read", text)
        self.assertIn("cancel-in-progress:", text)
        self.assertIn("gh release create", text)
        self.assertIn("Portable Syncthing", text)
        package = (ROOT / "scripts/package-windows.ps1").read_text(encoding="utf-8")
        self.assertIn("CompileOnly", package)
        self.assertIn("PublishSingleFile", package)
        self.assertIn("InformationalVersion", package)
        self.assertNotIn("gh release", package)


if __name__ == "__main__":
    unittest.main()
