#ifndef _Foliage_
#define _Foliage_

#include "Geometry.hlsl"

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

#endif