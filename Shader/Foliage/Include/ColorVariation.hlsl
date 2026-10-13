#ifndef _ColorVariationInclude_
#define _ColorVariationInclude_

sampler2D g_PerlinNoise;
float g_PerlinNoiseScale;

float PerlinNoise(float2 uv, float scale)
{
    return tex2Dlod(g_PerlinNoise, float4(uv.xy, 0, 0) * scale * g_PerlinNoiseScale).r;
}

#endif