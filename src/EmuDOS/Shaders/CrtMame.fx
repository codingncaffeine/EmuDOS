// MAME-style single-pass CRT for WPF. Compiled as ps_2_0 so it renders both on the GPU AND through
// WPF's software rasterizer (RenderTargetBitmap / screenshots / video capture) — ps_3_0 has no
// software fallback. Branchless (ps_2_0 has no dynamic flow control). Curvature/vignette adapted from
// MAME's distortion.fx; scanline + shadow mask from its post.fx.
sampler2D input : register(s0);
float screenHeight : register(c0); // source resolution height in pixels

float4 main(float2 uv : TEXCOORD) : COLOR
{
    // Barrel curvature around the screen centre.
    float2 cc = uv - 0.5;
    float r2 = dot(cc, cc);
    float k = 0.22;
    float f = 1.0 + r2 * (k + 0.10 * sqrt(r2));
    f /= 1.0 + k * 0.25;
    float2 warp = cc * f + 0.5;

    // Branchless "inside the tube" factor (1 inside, 0 outside) -> rounded black corners.
    float2 inb = step(0.0, warp) * step(warp, 1.0);
    float inside = inb.x * inb.y;

    float4 color = tex2D(input, saturate(warp));

    // Scanline beam profile.
    float beam = frac(warp.y * screenHeight);
    float d = abs(beam - 0.5) * 2.0;
    color.rgb *= lerp(1.0, 0.55, d * d);

    // RGB aperture grille (screen-space, 3-phase).
    float ph = fmod(floor(uv.x * 480.0), 3.0);
    float3 mask = ph < 1.0 ? float3(1.0, 0.82, 0.82)
                : ph < 2.0 ? float3(0.82, 1.0, 0.82)
                           : float3(0.82, 0.82, 1.0);
    color.rgb *= mask;

    color.rgb *= 1.35;                                   // brightness compensation
    color.rgb *= smoothstep(1.0, 0.25, length(cc) * 1.4); // vignette
    color.rgb *= inside;                                 // black outside the curved screen
    return color;
}
