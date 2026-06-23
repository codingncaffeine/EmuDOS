using System.Windows.Media.Effects;

namespace EmuDOS.Effects;

/// <summary>The curated CRT shader options the user can cycle through in-game.</summary>
public enum VideoShader
{
    Off,
    Scanlines,
    Crt,
}

public static class VideoShaders
{
    public static VideoShader Parse(string? name) => name switch
    {
        nameof(VideoShader.Scanlines) => VideoShader.Scanlines,
        nameof(VideoShader.Crt) => VideoShader.Crt,
        _ => VideoShader.Off,
    };

    /// <summary>The next shader in the cycle (wraps back to Off).</summary>
    public static VideoShader Next(VideoShader shader) => shader switch
    {
        VideoShader.Off => VideoShader.Scanlines,
        VideoShader.Scanlines => VideoShader.Crt,
        _ => VideoShader.Off,
    };

    public static string DisplayName(VideoShader shader) => shader switch
    {
        VideoShader.Scanlines => "Scanlines",
        VideoShader.Crt => "CRT",
        _ => "No shader",
    };

    /// <summary>Build the effect for a shader (null = no effect), sized to the source resolution.</summary>
    public static ShaderEffect? Create(VideoShader shader, double width, double height) => shader switch
    {
        VideoShader.Scanlines => new CrtScanlinesEffect { ScreenHeight = height },
        VideoShader.Crt => new CrtApertureEffect { ScreenWidth = width, ScreenHeight = height },
        _ => null,
    };
}
