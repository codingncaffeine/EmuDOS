// MAME-style single-pass CRT for WPF (ps_3_0). Barrel curvature, vignette and rounded corners are
// adapted from MAME's distortion.fx; the scanline beam profile + RGB shadow mask follow its post.fx
// (reimplemented procedurally, since MAME's real chain is multi-pass + texture-based).
sampler2D input : register(s0);
float screenHeight : register(c0); // source resolution height in pixels

float4 main(float2 uv : TEXCOORD) : COLOR
{
    // Barrel curvature around the screen centre (MAME distortion.fx GetDistortedCoords).
    float2 cc = uv - 0.5;
    float r2 = dot(cc, cc);
    float k = 0.22;
    float f = 1.0 + r2 * (k + 0.10 * sqrt(r2));
    f /= 1.0 + k * 0.25;            // keep the image roughly the same size
    float2 warp = cc * f + 0.5;

    // Outside the curved tube -> black (gives the rounded corners for free).
    if (warp.x < 0.0 || warp.x > 1.0 || warp.y < 0.0 || warp.y > 1.0)
        return float4(0, 0, 0, 1);

    float4 color = tex2D(input, warp);

    // Scanline beam profile: bright at the centre of each source row, dark between.
    float beam = frac(warp.y * screenHeight);
    float d = abs(beam - 0.5) * 2.0;
    color.rgb *= lerp(1.0, 0.55, d * d);

    // RGB shadow mask: a fine vertical aperture grille in screen space (3-phase).
    float ph = fmod(floor(uv.x * 480.0), 3.0);
    float3 mask = ph < 1.0 ? float3(1.0, 0.82, 0.82)
                : ph < 2.0 ? float3(0.82, 1.0, 0.82)
                           : float3(0.82, 0.82, 1.0);
    color.rgb *= mask;

    // Compensate brightness for the scanline + mask darkening.
    color.rgb *= 1.35;

    // Vignette toward the edges (MAME distortion.fx GetVignetteFactor).
    color.rgb *= smoothstep(1.0, 0.25, length(cc) * 1.4);

    return color;
}
