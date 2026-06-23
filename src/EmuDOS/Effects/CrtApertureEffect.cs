using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace EmuDOS.Effects;

/// <summary>Fuller CRT: scanlines + an RGB aperture mask + a soft vignette.</summary>
public sealed class CrtApertureEffect : ShaderEffect
{
    private static readonly PixelShader Shader = new()
    {
        UriSource = new Uri("pack://application:,,,/Shaders/Compiled/CrtAperture.ps"),
    };

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(CrtApertureEffect), 0);

    public static readonly DependencyProperty ScreenWidthProperty =
        DependencyProperty.Register(nameof(ScreenWidth), typeof(double), typeof(CrtApertureEffect),
            new UIPropertyMetadata(320.0, PixelShaderConstantCallback(0)));

    public static readonly DependencyProperty ScreenHeightProperty =
        DependencyProperty.Register(nameof(ScreenHeight), typeof(double), typeof(CrtApertureEffect),
            new UIPropertyMetadata(240.0, PixelShaderConstantCallback(1)));

    public CrtApertureEffect()
    {
        PixelShader = Shader;
        UpdateShaderValue(InputProperty);
        UpdateShaderValue(ScreenWidthProperty);
        UpdateShaderValue(ScreenHeightProperty);
    }

    public Brush Input
    {
        get => (Brush)GetValue(InputProperty);
        set => SetValue(InputProperty, value);
    }

    public double ScreenWidth
    {
        get => (double)GetValue(ScreenWidthProperty);
        set => SetValue(ScreenWidthProperty, value);
    }

    public double ScreenHeight
    {
        get => (double)GetValue(ScreenHeightProperty);
        set => SetValue(ScreenHeightProperty, value);
    }
}
