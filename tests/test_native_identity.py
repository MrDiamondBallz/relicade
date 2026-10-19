"""Source-level guards for native product identity and disabled inherited services."""
from pathlib import Path
import unittest
import xml.etree.ElementTree as ET

NATIVE = Path(__file__).resolve().parents[1] / 'native'


class NativeIdentityTests(unittest.TestCase):
    def test_product_identity(self):
        profile = (NATIVE / 'Services/RelicadeProfile.cs').read_text()
        self.assertIn('DisplayName => "Relicade"', profile)
        self.assertIn('Repository => "MrDiamondBallz/relicade"', profile)
        self.assertIn('ApplicationId => "Relicade"', profile)
        self.assertIn('AppName = "Relicade"', (NATIVE / 'Services/RelicadePaths.cs').read_text())
        android = ET.parse(NATIVE / 'Relicade.Android/Relicade.Android.csproj')
        self.assertEqual('io.github.mrdiamondballz.relicade', android.findtext('.//ApplicationId'))

    def test_runtime_has_no_inherited_delivery_destinations(self):
        for name in ['Services/Telemetry.cs', 'Services/VelopackUpdateService.cs', 'Services/AnnouncementDefaults.cs']:
            text = (NATIVE / name).read_text()
            for forbidden in ['https://us.i.posthog.com', 'https://github.com/tgeorgiadis/quiver-launcher',
                              'https://raw.githubusercontent.com/tgeorgiadis/quiver-launcher', 'QUIVER_POSTHOG']:
                self.assertNotIn(forbidden, text, name)
        project = (NATIVE / 'Relicade.App.csproj').read_text()
        self.assertNotIn('$(POSTHOG_PROJECT_TOKEN)', project)

    def test_no_active_workflows_or_inherited_art(self):
        self.assertFalse((NATIVE / '.github').exists())
        self.assertFalse((NATIVE / 'Assets/quiver-icon.png').exists())
        self.assertTrue((NATIVE / 'Assets/relicade-icon.png').is_file())
        self.assertTrue((NATIVE / 'THIRD_PARTY_NOTICES.md').is_file())

    def test_preserves_source_metadata_identity(self):
        client = (NATIVE / 'Services/QuiverCatalogClient.cs').read_text()
        self.assertIn('https://api.quiverlauncher.com/api/v1', client)
        self.assertIn('https://quiverlauncher.com', client)
        self.assertNotIn('api.relicade', client)

    def test_inherited_notices(self):
        license = (NATIVE / 'LICENSE').read_text()
        self.assertIn('Copyright (c) 2026 SirDiabo', license)
        self.assertIn('Copyright (c) 2026 Thomas Georgiadis', license)
        self.assertIn('Copyright (c) 2025 SirDiabo', (NATIVE / 'lib/Relicade.Core/LICENSE').read_text())

    def test_external_verdicts_are_not_rebranded_as_our_own(self):
        for name in ['Views/BrowseView.axaml', 'Views/BrowseDetailsView.axaml',
                     'Views/LibraryView.axaml', 'Services/ReleaseWarnings.cs', 'Services/LauncherUpdateWorkflow.cs']:
            text = (NATIVE / name).read_text()
            for claim in ['Relicade catalog', 'Relicade checked this release', "Relicade hasn't verified"]:
                self.assertNotIn(claim, text, name)

    def test_cross_publish_uses_target_platform_not_build_host(self):
        project = (NATIVE / 'Relicade.App.csproj').read_text()
        self.assertIn("'$(RuntimeIdentifier)' == 'win-x64'", project)
        self.assertIn("'$(RuntimeIdentifier)' == ''", project)

    def test_cli_and_default_palette_belong_to_relicade(self):
        cli = (NATIVE / 'Services/CLIHandler.cs').read_text()
        self.assertNotIn('WriteColor("  __  _', cli)
        self.assertIn('Self-update unavailable', cli)
        self.assertIn('#FF663D', (NATIVE / 'Services/ThemeEditor.cs').read_text())
        self.assertIn('PrimaryColor { get; set; } = "#111211"', (NATIVE / 'Services/AppSettings.cs').read_text())

    def test_orange_primary_actions_use_dark_readable_text(self):
        styles = [e for e in ET.parse(NATIVE / 'App.axaml').iter()
                  if e.tag.endswith('Style') and e.attrib.get('Selector') == 'Button.accent']
        self.assertTrue(styles, 'Orange primary actions cannot inherit white-on-orange text')
        self.assertTrue(any(e.attrib.get('Property') == 'Foreground' and e.attrib.get('Value') == '#111211'
                            for e in styles[0]))


if __name__ == '__main__':
    unittest.main()
