"""Contrats hors ligne des couleurs, des polices et des commandes vectorielles."""
from pathlib import Path
import re
import unittest
import xml.etree.ElementTree as ET

CLIENT = Path(__file__).resolve().parents[2] / "src" / "Alycia.Client.Avalonia"
KEY = "{http://schemas.microsoft.com/winfx/2006/xaml}Key"


def luminance(value):
    rgb = [int(value[i:i + 2], 16) / 255 for i in (1, 3, 5)]
    linear = [v / 12.92 if v <= 0.04045 else ((v + 0.055) / 1.055) ** 2.4 for v in rgb]
    return sum(a * b for a, b in zip(linear, (0.2126, 0.7152, 0.0722)))


class BorealAssetsTests(unittest.TestCase):
    def test_reading_and_controls_in_both_themes(self):
        themes = ET.parse(CLIENT / "App.axaml").find(".//{*}ResourceDictionary.ThemeDictionaries")
        self.assertEqual({theme.get(KEY) for theme in themes}, {"Light", "Dark"})
        for theme in themes:
            palette = {item.get(KEY).removeprefix("Boreal"): luminance(item.text) for item in theme}
            pairs = [(fg, surface, 4.5) for surface in ("Canvas", "Surface", "Code", "Soft")
                     for fg in ("Ink", "Muted", "Iris", "Jade", "Amber", "Error")]
            pairs += [("OnIris", "Iris", 4.5), ("Outline", "Surface", 3), ("Outline", "Canvas", 3)]
            for fg, bg, minimum in pairs:
                with self.subTest(theme=theme.get(KEY), foreground=fg, background=bg):
                    high, low = sorted((palette[fg], palette[bg]), reverse=True)
                    self.assertGreaterEqual((high + 0.05) / (low + 0.05), minimum)

    def test_icon_only_actions_have_accessible_names(self):
        for button in ET.parse(CLIENT / "ChatWindow.axaml").findall(".//{*}Button"):
            if button.find("{*}PathIcon") is not None:
                self.assertTrue(button.get("AutomationProperties.Name"))
                self.assertTrue(button.get("ToolTip.Tip"))
                self.assertNotEqual(button.get("Focusable"), "False")

    def test_resources_and_embedded_fonts_resolve(self):
        keys = {element.get(KEY) for element in ET.parse(CLIENT / "App.axaml").iter()}
        for view in ("App.axaml", "ChatWindow.axaml"):
            referenced = re.findall(r"\{(?:Static|Dynamic)Resource ((?:Boreal|Glyph)\w+|Luciole|Hack|ReadingSize|WrapCode)\}", (CLIENT / view).read_text())
            self.assertFalse(set(referenced) - keys)
        for family in ("Luciole", "Hack"):
            directory = CLIENT / "Assets" / "Fonts" / family
            self.assertGreater((directory / "NOTICE.txt").stat().st_size, 100)
            for style in ("Regular", "Italic", "Bold", "BoldItalic"):
                with (directory / f"{family}-{style}.ttf").open("rb") as font:
                    self.assertEqual(font.read(4), b"\x00\x01\x00\x00")
