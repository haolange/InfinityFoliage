#ifndef _FoliageBufferInclude
#define _FoliageBufferInclude

#include "Geometry.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

SamplerState sampler_MainTex, Global_point_clamp_sampler, Global_bilinear_clamp_sampler, Global_trilinear_clamp_sampler, Global_point_repeat_sampler, Global_bilinear_repeat_sampler, Global_trilinear_repeat_sampler;

struct TreeElement
{
     float4x4 matrix_World;
};

StructuredBuffer<uint> _TreeIndexBuffer;
StructuredBuffer<TreeElement> _TreeElementBuffer;
float _LODFactor;
float _LodFadeEnable;

struct GrassElement
{
     float4x4 matrix_World;
};
int _InstanceOffset;
StructuredBuffer<GrassElement> _GrassElementBuffer;

float4 _FoliageSHAr, _FoliageSHAg, _FoliageSHAb;
float4 _FoliageSHBr, _FoliageSHBg, _FoliageSHBb, _FoliageSHC;

float3 SampleFoliageSH(float3 normalWS)
{
    float3 irradiance = SHEvalLinearL0L1(normalWS, _FoliageSHAr, _FoliageSHAg, _FoliageSHAb);
    irradiance += SHEvalLinearL2(normalWS, _FoliageSHBr, _FoliageSHBg, _FoliageSHBb, _FoliageSHC);
#ifdef UNITY_COLORSPACE_GAMMA
    irradiance = LinearToSRGB(irradiance);
#endif
    return max(irradiance, 0.0);
}

float3 TransformFoliageNormal(float4x4 matrixWorld, float3 normalOS)
{
    float3 x = matrixWorld._m00_m10_m20;
    float3 y = matrixWorld._m01_m11_m21;
    float3 z = matrixWorld._m02_m12_m22;
    float3 normalWS = cross(y, z) * normalOS.x + cross(z, x) * normalOS.y + cross(x, y) * normalOS.z;
    return normalize(normalWS * (dot(x, cross(y, z)) < 0.0 ? -1.0 : 1.0));
}

#endif
