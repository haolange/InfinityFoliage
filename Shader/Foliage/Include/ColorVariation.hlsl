#ifndef _ColorVariationInclude_
#define _ColorVariationInclude_

sampler2D g_PerlinNoise;
float g_PerlinNoiseScale;

float PerlinNoise(float2 uv, float scale)
{
    return tex2Dlod(g_PerlinNoise, float4(uv.xy, 0, 0) * scale * g_PerlinNoiseScale).r;
}

float3 Linear_to_HSV(float3 In)
{
    float3 sRGBLo = In * 12.92;
    float3 sRGBHi = (pow(max(abs(In), 1.192092896e-07), float3(1.0 / 2.4, 1.0 / 2.4, 1.0 / 2.4)) * 1.055) - 0.055;
    float3 Linear = float3(In <= 0.0031308) ? sRGBLo : sRGBHi;
    float4 K = float4(0.0, -1.0 / 3.0, 2.0 / 3.0, -1.0);
    float4 P = lerp(float4(Linear.bg, K.wz), float4(Linear.gb, K.xy), step(Linear.b, Linear.g));
    float4 Q = lerp(float4(P.xyw, Linear.r), float4(Linear.r, P.yzx), step(P.x, Linear.r));
    float D = Q.x - min(Q.w, Q.y);
    float E = 1e-10;
    return float3(abs(Q.z + (Q.w - Q.y)/(6.0 * D + E)), D / (Q.x + E), Q.x);
}

float3 HSV_to_Linear(float3 In)
{
    float4 K = float4(1.0, 2.0 / 3.0, 1.0 / 3.0, 3.0);
    float3 P = abs(frac(In.xxx + K.xyz) * 6.0 - K.www);
    float3 RGB = In.z * lerp(K.xxx, saturate(P - K.xxx), In.y);
    float3 linearRGBLo = RGB / 12.92;
    float3 linearRGBHi = pow(max(abs((RGB + 0.055) / 1.055), 1.192092896e-07), float3(2.4, 2.4, 2.4));
    return float3(RGB <= 0.04045) ? linearRGBLo : linearRGBHi;
}

void HSL_float( float4 color, float3 hsl, out float4 colorOut )
{
    float3 hsv = Linear_to_HSV( color.rgb );
    hsv.x += hsl.x;
    hsv.y = saturate(hsv.y + hsl.y * 0.5);
    hsv.z = saturate(hsv.z + hsl.z * 0.5);
    colorOut = float4( HSV_to_Linear(hsv), color.a );
}

void HSL_float( float3 hsv, float3 hsl, out float3 colorOut )
{
    hsv.x += hsl.x;
    hsv.y = saturate(hsv.y + hsl.y * 0.5);
    hsv.z = saturate(hsv.z + hsl.z * 0.5);
    colorOut = HSV_to_Linear(hsv);
}

void ApplyColorCorrection( inout float4 albedo, float noise )
{
    /*#ifdef _COLOR_HSL
        float3 albedoHSV = Linear_to_HSV( albedo.rgb );
        float3 albedo1;
        float3 albedo2;
        HSL_float( albedoHSV, _HSL, albedo1 );
        HSL_float( albedoHSV, _HSLVariation, albedo2 );
        albedo.rgb = lerp(albedo2, albedo1, noise);
    #else*/
        albedo *= lerp(_TintVariation, _TopTint, noise);
    //#endif
}
#endif