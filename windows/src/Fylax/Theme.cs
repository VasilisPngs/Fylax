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
    public Brush Error { get; init; } = null!;

    public static readonly Theme Light = new()
    {
        Background = Paint("#FAFAFA"),
        Surface = Paint("#F0F0F0"),
        SurfaceContainerHigh = Paint("#E4E4E4"),
        SurfaceContainerHighest = Paint("#D4D4D4"),
        OnSurface = Paint("#121212"),
        OnSurfaceVariant = Paint("#5E5E5E"),
        Primary = Paint("#1B1B1B"),
        OnPrimary = Paint("#FAFAFA"),
        SecondaryContainer = Paint("#E4E4E4"),
        OnSecondaryContainer = Paint("#1B1B1B"),
        ErrorContainer = Paint("#E4E4E4"),
        OnErrorContainer = Paint("#1B1B1B"),
        Outline = Paint("#767676"),
        OutlineVariant = Paint("#E4E4E4"),
        Error = Paint("#D93025"),
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
        Outline = Paint("#919191"),
        OutlineVariant = Paint("#292929"),
        Error = Paint("#D93025"),
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
