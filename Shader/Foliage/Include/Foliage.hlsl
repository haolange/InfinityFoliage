#ifndef _Foliage_
#define _Foliage_

#include "Geometry.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

struct FTreeBatch
{
     int lODIndex;
     FBound boundBox;
     FSphere boundSphere;
     float4x4 matrix_World;
};

uint _TreeIndexOffset;
Buffer<uint> _TreeIndexBuffer;
StructuredBuffer<FTreeBatch> _TreeBatchBuffer;


struct FGrassBatch
{
     float3 position;
     float4x4 matrix_World;
};
StructuredBuffer<FGrassBatch> _GrassBatchBuffer;


float LODCrossDither(uint2 fadeMaskSeed, float ditherFactor)
{
    float p = GenerateHashedRandomFloat(fadeMaskSeed);
    return (ditherFactor - CopySign(p, ditherFactor));
    //clip(f);
}

#endif