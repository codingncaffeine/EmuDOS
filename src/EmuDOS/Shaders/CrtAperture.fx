// Fuller CRT: scanlines + an RGB aperture/shadow mask + a soft vignette. ps_3_0 (WPF ShaderEffect).
sampler2D input : register(s0);
float screenWidth : register(c0);  // source resolution width in pixels
float screenHeight : register(c1); // source resolution height in pixels

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 color = tex2D(input, uv);

    // Scanlines (per source row).
    float row = frac(uv.y * screenHeight * 0.5);
    float scan = smoothstep(0.30, 0.5, row) * 0.40 + 0.60;
    color.rgb *= scan;

    // RGB aperture mask: tint each source column toward one phosphor (3-phase).
    float phase = fmod(floor(uv.x * screenWidth), 3.0);
    float3 mask = phase < 1.0 ? float3(1.0, 0.7, 0.7)
                : phase < 2.0 ? float3(0.7, 1.0, 0.7)
                              : float3(0.7, 0.7, 1.0);
    color.rgb *= mask;

    // Compensate for the darkening of scanlines + mask.
    color.rgb *= 1.30;

    // Soft vignette toward the edges.
    float2 d = uv - 0.5;
    color.rgb *= 1.0 - dot(d, d) * 0.5;

    return color;
}
