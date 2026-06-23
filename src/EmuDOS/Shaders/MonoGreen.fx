// Green-phosphor monochrome monitor (early DOS look): luminance -> green tint + scanlines. ps_3_0.
sampler2D input : register(s0);
float screenHeight : register(c0);

float4 main(float2 uv : TEXCOORD) : COLOR
{
    float4 color = tex2D(input, uv);
    float luma = dot(color.rgb, float3(0.299, 0.587, 0.114));
    float beam = frac(uv.y * screenHeight);
    float d = abs(beam - 0.5) * 2.0;
    luma *= lerp(1.0, 0.6, d * d);
    return float4(luma * 0.15, luma, luma * 0.30, color.a);
}
