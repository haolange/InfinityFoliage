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

#endif
