#ifndef _FoliageBufferInclude
#define _FoliageBufferInclude

#include "Geometry.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

SamplerState sampler_MainTex, Global_point_clamp_sampler, Global_bilinear_clamp_sampler, Global_trilinear_clamp_sampler, Global_point_repeat_sampler, Global_bilinear_repeat_sampler, Global_trilinear_repeat_sampler;

struct FTreeElement
{
     int meshIndex;
     FBound boundBox;
     FSphere boundSphere;
     float4x4 matrix_World;
};

Buffer<uint> _TreeIndexBuffer;
StructuredBuffer<FTreeElement> _TreeElementBuffer;


struct FGrassElement
{
     float4x4 matrix_World;
};
StructuredBuffer<FGrassElement> _GrassElementBuffer;

#endif