using Microsoft.UI;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

using Windows.UI;

namespace AI_Panel_v2.Helpers;

public static class AccentPaletteHelper
{
    public static readonly IReadOnlyList<AccentPaletteOption> Options =
    [
        new("Toolkit Blue", Colors.DodgerBlue, Color.FromArgb(0xFF, 0xC9, 0xE6, 0xFF), Color.FromArgb(0xFF, 0x99, 0xD0, 0xFF), Color.FromArgb(0xFF, 0x63, 0xB5, 0xFF), Color.FromArgb(0xFF, 0x00, 0x6B, 0xC5), Color.FromArgb(0xFF, 0x00, 0x58, 0xA2), Color.FromArgb(0xFF, 0x00, 0x45, 0x80)),
        new("Aqua Mint", Color.FromArgb(0xFF, 0x13, 0x9B, 0xB3), Color.FromArgb(0xFF, 0xC7, 0xF2, 0xF7), Color.FromArgb(0xFF, 0x9E, 0xE8, 0xEF), Color.FromArgb(0xFF, 0x69, 0xD5, 0xE0), Color.FromArgb(0xFF, 0x0E, 0x75, 0x88), Color.FromArgb(0xFF, 0x0B, 0x5E, 0x6D), Color.FromArgb(0xFF, 0x08, 0x48, 0x53)),
        new("Forest Moss", Color.FromArgb(0xFF, 0x2D, 0x7D, 0x46), Color.FromArgb(0xFF, 0xD2, 0xED, 0xD9), Color.FromArgb(0xFF, 0xAB, 0xDD, 0xB8), Color.FromArgb(0xFF, 0x73, 0xC1, 0x87), Color.FromArgb(0xFF, 0x1F, 0x5B, 0x32), Color.FromArgb(0xFF, 0x18, 0x47, 0x27), Color.FromArgb(0xFF, 0x12, 0x33, 0x1D)),
        new("Amber Coral", Color.FromArgb(0xFF, 0xD9, 0x6D, 0x1F), Color.FromArgb(0xFF, 0xFF, 0xE8, 0xD3), Color.FromArgb(0xFF, 0xFF, 0xD2, 0xAB), Color.FromArgb(0xFF, 0xFF, 0xB9, 0x76), Color.FromArgb(0xFF, 0xB5, 0x53, 0x10), Color.FromArgb(0xFF, 0x8E, 0x40, 0x0C), Color.FromArgb(0xFF, 0x6A, 0x2E, 0x08)),
        new("Violet Plum", Color.FromArgb(0xFF, 0x7E, 0x57, 0xC2), Color.FromArgb(0xFF, 0xE5, 0xDB, 0xF4), Color.FromArgb(0xFF, 0xCE, 0xBE, 0xEA), Color.FromArgb(0xFF, 0xAF, 0x9A, 0xDE), Color.FromArgb(0xFF, 0x61, 0x3F, 0xA5), Color.FromArgb(0xFF, 0x4D, 0x31, 0x84), Color.FromArgb(0xFF, 0x39, 0x24, 0x63))
    ];

    public static AccentPaletteOption GetByName(string? name) =>
        Options.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase), Options[0]);

    public static void ApplyAccentColor(Color accent)
    {
        var light1 = Blend(accent, Colors.White, 0.22);
        var light2 = Blend(accent, Colors.White, 0.42);
        var light3 = Blend(accent, Colors.White, 0.62);
        var dark1 = Blend(accent, Colors.Black, 0.20);
        var dark2 = Blend(accent, Colors.Black, 0.38);
        var dark3 = Blend(accent, Colors.Black, 0.52);

        ApplyPalette(new AccentPaletteOption("Custom", accent, light3, light2, light1, dark1, dark2, dark3));
    }

    public static void ApplyPalette(AccentPaletteOption palette)
    {
        var resources = Application.Current.Resources;
        resources["SystemAccentColor"] = palette.Accent;
        resources["SystemAccentColorLight1"] = palette.Light1;
        resources["SystemAccentColorLight2"] = palette.Light2;
        resources["SystemAccentColorLight3"] = palette.Light3;
        resources["SystemAccentColorDark1"] = palette.Dark1;
        resources["SystemAccentColorDark2"] = palette.Dark2;
        resources["SystemAccentColorDark3"] = palette.Dark3;
        resources["SystemAccentColorBrush"] = new SolidColorBrush(palette.Accent);
    }

    public static string ToHex(Color color) => $"#{color.A:X2}{color.R:X2}{color.G:X2}{color.B:X2}";

    public static bool TryParseHex(string? text, out Color color)
    {
        color = Colors.Transparent;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim().TrimStart('#');
        if (value.Length is not (6 or 8))
        {
            return false;
        }

        var withAlpha = value.Length == 6 ? $"FF{value}" : value;
        if (!uint.TryParse(withAlpha, System.Globalization.NumberStyles.HexNumber, null, out var argb))
        {
            return false;
        }

        color = Color.FromArgb(
            (byte)((argb >> 24) & 0xFF),
            (byte)((argb >> 16) & 0xFF),
            (byte)((argb >> 8) & 0xFF),
            (byte)(argb & 0xFF));
        return true;
    }

    private static Color Blend(Color from, Color to, double ratio)
    {
        ratio = Math.Clamp(ratio, 0, 1);
        var r = (byte)Math.Round(from.R + (to.R - from.R) * ratio);
        var g = (byte)Math.Round(from.G + (to.G - from.G) * ratio);
        var b = (byte)Math.Round(from.B + (to.B - from.B) * ratio);
        return Color.FromArgb(0xFF, r, g, b);
    }
}

public sealed record AccentPaletteOption(
    string Name,
    Color Accent,
    Color Light3,
    Color Light2,
    Color Light1,
    Color Dark1,
    Color Dark2,
    Color Dark3);
