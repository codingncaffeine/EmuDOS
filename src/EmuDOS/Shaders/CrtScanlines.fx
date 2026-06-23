// Light CRT scanlines. ps_3_0 (WPF ShaderEffect).
sampler2D input : register(s0);
float screenHeight : register(c0); // source resolution height in pixels

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 color = tex2D(input, uv);
    float row = frac(uv.y * screenHeight * 0.5);
    float scanline = smoothstep(0.4, 0.5, row) * 0.25 + 0.75; // gentle
    color.rgb *= scanline * 1.08;
    return color;
}
