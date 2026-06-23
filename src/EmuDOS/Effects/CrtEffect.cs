using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;

namespace EmuDOS.Effects;

/// <summary>
/// A single-pass WPF pixel-shader effect for the DOS video Image, loading one of the compiled CRT
/// shaders (all share the same s0 input + c0 ScreenHeight signature). One class drives the whole set.
/// </summary>
public sealed class CrtEffect : ShaderEffect
{
    private static readonly Dictionary<string, PixelShader> Cache = new();

    public static readonly DependencyProperty InputProperty =
        RegisterPixelShaderSamplerProperty("Input", typeof(CrtEffect), 0);

    public static readonly DependencyProperty ScreenHeightProperty =
        DependencyProperty.Register(nameof(ScreenHeight), typeof(double), typeof(CrtEffect),
            new UIPropertyMetadata(240.0, PixelShaderConstantCallback(0)));

    public CrtEffect(string compiledShaderUri)
    {
        if (!Cache.TryGetValue(compiledShaderUri, out var shader))
            Cache[compiledShaderUri] = shader = new PixelShader { UriSource = new Uri(compiledShaderUri) };
        PixelShader = shader;
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
