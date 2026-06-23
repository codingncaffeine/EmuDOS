using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace EmuDOS.Effects;

/// <summary>Light CRT: horizontal scanlines + a touch of bloom. Applies to the DOS video Image.</summary>
public sealed class CrtScanlinesEffect : ShaderEffect
{
    private static readonly PixelShader Shader = new()
    {
        UriSource = new Uri("pack://application:,,,/Shaders/Compiled/CrtScanlines.ps"),
    };

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(CrtScanlinesEffect), 0);

    public static readonly DependencyProperty ScreenHeightProperty =
        DependencyProperty.Register(nameof(ScreenHeight), typeof(double), typeof(CrtScanlinesEffect),
            new UIPropertyMetadata(240.0, PixelShaderConstantCallback(0)));

    public CrtScanlinesEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(ScreenHeightProperty);
    }

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public double ScreenHeight
    {
        get => (double)GetValue(ScreenHeightProperty);
        set => SetValue(ScreenHeightProperty, value);
    }
}
