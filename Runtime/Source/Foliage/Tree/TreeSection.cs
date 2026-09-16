using System;
using Unity.Mathematics;

namespace Landscape.FoliagePipeline
{
    [Serializable]
    public struct InstanceTransform
    {
        public float3 position;
        public float3 rotation;
        public float3 scale;

        public InstanceTransform(float3 position, float3 rotation, float3 scale)
        {
            this.scale = scale;
            this.rotation = rotation;
            this.position = position;
        }
    }

    [Serializable]
    public struct TreeCell
    {
        public int boundIndex;
        [UnityEngine.HideInInspector]
        public int offset;
        [UnityEngine.HideInInspector]
        public int count;
    }
}
