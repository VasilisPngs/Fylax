using System.Windows.Media;

namespace Fylax;

public sealed class Theme
{
    public Brush Background { get; init; } = null!;
    public Brush Surface { get; init; } = null!;
    public Brush SurfaceContainerHigh { get; init; } = null!;
    public Brush SurfaceContainerHighest { get; init; } = null!;
    public Brush OnSurface { get; init; } = null!;
    public Brush OnSurfaceVariant { get; init; } = null!;
    public Brush Primary { get; init; } = null!;
    public Brush OnPrimary { get; init; } = null!;
    public Brush SecondaryContainer { get; init; } = null!;
    public Brush OnSecondaryContainer { get; init; } = null!;
    public Brush ErrorContainer { get; init; } = null!;
    public Brush OnErrorContainer { get; init; } = null!;
    public Brush Outline { get; init; } = null!;
    public Brush OutlineVariant { get; init; } = null!;
    public Color ToggleOn { get; init; }
    public Color ToggleOff { get; init; }

    public static readonly Theme Light = new()
    {
        Background = Paint("#FAFAFA"),
        Surface = Paint("#F3F3F3"),
        SurfaceContainerHigh = Paint("#F3F3F3"),
        SurfaceContainerHighest = Paint("#E2E2E2"),
        OnSurface = Paint("#121212"),
        OnSurfaceVariant = Paint("#5E5E5E"),
        Primary = Paint("#1B1B1B"),
        OnPrimary = Paint("#FAFAFA"),
        SecondaryContainer = Paint("#E2E2E2"),
        OnSecondaryContainer = Paint("#1B1B1B"),
        ErrorContainer = Paint("#E2E2E2"),
        OnErrorContainer = Paint("#1B1B1B"),
        Outline = Paint("#D6D6D6"),
        OutlineVariant = Paint("#E2E2E2"),
        ToggleOn = FromHex("#1B1B1B"),
        ToggleOff = FromHex("#D6D6D6")
    };

    public static readonly Theme Dark = new()
    {
        Background = Paint("#121212"),
        Surface = Paint("#1B1B1B"),
        SurfaceContainerHigh = Paint("#292929"),
        SurfaceContainerHighest = Paint("#464646"),
        OnSurface = Paint("#FAFAFA"),
        OnSurfaceVariant = Paint("#D6D6D6"),
        Primary = Paint("#E2E2E2"),
        OnPrimary = Paint("#121212"),
        SecondaryContainer = Paint("#292929"),
        OnSecondaryContainer = Paint("#FAFAFA"),
        ErrorContainer = Paint("#292929"),
        OnErrorContainer = Paint("#FAFAFA"),
        Outline = Paint("#464646"),
        OutlineVariant = Paint("#292929"),
        ToggleOn = FromHex("#E2E2E2"),
        ToggleOff = FromHex("#464646")
    };

    private static SolidColorBrush Paint(string hex)
    {
        var brush = new SolidColorBrush(FromHex(hex));
        brush.Freeze();
        return brush;
    }

    private static Color FromHex(string hex)
    {
        return (Color)ColorConverter.ConvertFromString(hex);
    }
}
