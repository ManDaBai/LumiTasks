using Color = System.Windows.Media.Color;
using System.Windows;
using System.Windows.Media;
using System.Globalization;

namespace LumiTasks.VisualPrototype;

public static class ThemeManager
{
    private sealed record Palette(string Accent1,string Accent2,string Accent3,string Ink,string Muted,
        string Stroke,string Fill,string Glass2,string Island1,string Island2);
    private static readonly Dictionary<string,Palette> Palettes = new()
    {
        ["purple"] = new("#AE76FA","#7C44DA","#8753E8","#43326F","#8D7FB1","#C7AFFB","#F8F4FF","#EAE2FC","#283868","#3D2B69"),
        ["blue"] = new("#7AB9F7","#3985D7","#438CE0","#28486C","#718FAA","#ADD6F8","#F2F9FF","#E1EFFB","#1F3E65","#245A8B"),
        ["pink"] = new("#EDAFE3","#C45AAE","#D779C6","#633C66","#9B789E","#E6BADD","#FFF5FB","#F9E7F5","#604078","#774C85"),
        ["dark"] = new("#A391D5","#62518E","#7560A1","#44385E","#877D9F","#AA99CB","#EEE9F5","#DBD1EA","#191C36","#2C254B")
    };
    private static Color Parse(string hex) => (Color)ColorConverter.ConvertFromString(hex);
    public static bool TryGetCustomHue(string? key,out int hue)
    {
        hue = 270;
        return key != null && key.StartsWith("custom:",StringComparison.OrdinalIgnoreCase) &&
            int.TryParse(key[7..],NumberStyles.Integer,CultureInfo.InvariantCulture,out hue) && hue is >= 0 and < 360;
    }
    public static string CustomKey(int hue) => "custom:" + ((hue % 360 + 360) % 360).ToString(CultureInfo.InvariantCulture);
    private static string ToHex(Color c) => $"#{c.R:X2}{c.G:X2}{c.B:X2}";
    private static Color Hsl(double h,double s,double l)
    {
        h = ((h % 360) + 360) % 360 / 60.0;
        var chroma = (1-Math.Abs(2*l-1))*s;
        var x = chroma*(1-Math.Abs(h%2-1));
        var m = l-chroma/2;
        // Give all switch arms explicitly double-valued tuples (CS8506 / CS8131).
        // The numerical color calculation remains unchanged.
        (double r,double g,double b) = h switch
        {
            < 1 => (chroma,x,0.0), < 2 => (x,chroma,0.0), < 3 => (0.0,chroma,x),
            < 4 => (0.0,x,chroma), < 5 => (x,0.0,chroma), _ => (chroma,0.0,x)
        };
        return Color.FromRgb((byte)Math.Round((r+m)*255),(byte)Math.Round((g+m)*255),(byte)Math.Round((b+m)*255));
    }
    private static Palette FromHue(int hue)
    {
        string make(double saturation,double lightness) => ToHex(Hsl(hue,saturation,lightness));
        return new Palette(make(.78,.72),make(.65,.43),make(.68,.53),make(.40,.28),
            make(.24,.51),make(.56,.80),make(.72,.973),make(.58,.94),make(.45,.23),make(.40,.29));
    }
    private static Palette Resolve(string? selected)
    {
        if(TryGetCustomHue(selected,out var hue))return FromHue(hue);
        return Palettes.GetValueOrDefault(selected ?? "purple", Palettes["purple"]);
    }
    public static Color Accent(string? selected) => Parse(Resolve(selected).Accent3);
    public static void Apply(string? selected)
    {
        var theme = Resolve(selected);
        var resources = System.Windows.Application.Current?.Resources;
        if (resources == null) return;
        resources["Purple"] = new SolidColorBrush(Parse(theme.Accent3));
        resources["Ink"] = new SolidColorBrush(Parse(theme.Ink));
        resources["Muted"] = new SolidColorBrush(Parse(theme.Muted));
        resources["SoftStroke"] = new SolidColorBrush(Parse(theme.Stroke));
        resources["SoftFill"] = new SolidColorBrush(Parse(theme.Fill));
        resources["VioletGradient"] = Gradient(theme.Accent1,theme.Accent2);
        resources["GlassGradient"] = Gradient("#FDFBFF",theme.Glass2);
        resources["IslandGradient"] = Gradient(theme.Island1,theme.Island2);
        resources["HeaderGradient"] = Gradient(theme.Accent2,theme.Accent3);
    }
    private static LinearGradientBrush Gradient(string a,string b)
    {
        var g = new LinearGradientBrush { StartPoint = new Point(0,0), EndPoint = new Point(1,1) };
        g.GradientStops.Add(new GradientStop(Parse(a),0));
        g.GradientStops.Add(new GradientStop(Parse(b),1));
        return g;
    }
}
