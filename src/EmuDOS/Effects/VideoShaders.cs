using System.Windows.Media.Effects;

namespace EmuDOS.Effects;

/// <summary>The curated CRT shader options the user cycles through in-game.</summary>
public enum VideoShader
{
    Off,
    Scanlines,
    Crt,
    Green,
    Amber,
}

public static class VideoShaders
{
    private const string Base = "pack://application:,,,/Shaders/Compiled/";

    public static VideoShader Parse(string? name) => name switch
    {
        nameof(VideoShader.Scanlines) => VideoShader.Scanlines,
        nameof(VideoShader.Crt) => VideoShader.Crt,
        nameof(VideoShader.Green) => VideoShader.Green,
        nameof(VideoShader.Amber) => VideoShader.Amber,
        _ => VideoShader.Off,
    };

    /// <summary>The next shader in the cycle (wraps back to Off).</summary>
    public static VideoShader Next(VideoShader shader) => shader switch
    {
        VideoShader.Off => VideoShader.Scanlines,
        VideoShader.Scanlines => VideoShader.Crt,
        VideoShader.Crt => VideoShader.Green,
        VideoShader.Green => VideoShader.Amber,
        _ => VideoShader.Off,
    };

    public static string DisplayName(VideoShader shader) => shader switch
    {
        VideoShader.Scanlines => "Scanlines",
        VideoShader.Crt => "CRT",
        VideoShader.Green => "Green monitor",
        VideoShader.Amber => "Amber monitor",
        _ => "No shader",
    };

    /// <summary>Build the effect for a shader (null = no effect), sized to the source resolution.</summary>
    public static ShaderEffect? Create(VideoShader shader, double width, double height)
    {
        var ps = shader switch
        {
            VideoShader.Scanlines => "CrtScanlines.ps",
            VideoShader.Crt => "CrtMame.ps",
            VideoShader.Green => "MonoGreen.ps",
            VideoShader.Amber => "MonoAmber.ps",
            _ => null,
        };
        return ps is null ? null : new CrtEffect(Base + ps) { ScreenHeight = height };
    }
}
