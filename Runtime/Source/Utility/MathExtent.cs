using System;
using static Unity.Mathematics.math;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;

namespace Unity.Mathematics
{
    public static partial class mathExtent
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float sqr(float x) { return x * x; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 sqr(float2 x) { return new float2(sqr(x.x), sqr(x.y)); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float3 sqr(float3 x) { return new float3(sqr(x.x), sqr(x.y), sqr(x.z)); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float4 sqr(float4 x) { return new float4(sqr(x.x), sqr(x.y), sqr(x.z), sqr(x.w)); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double sqr(double x) { return x * x; }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double2 sqr(double2 x) { return new double2(sqr(x.x), sqr(x.y)); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double3 sqr(double3 x) { return new double3(sqr(x.x), sqr(x.y), sqr(x.z)); }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double4 sqr(double4 x) { return new double4(sqr(x.x), sqr(x.y), sqr(x.z), sqr(x.w)); }

        //
        public static readonly float hashScale1 = 0.1031f;
        public static readonly float3 hashScale2 = new float3(0.1031f, 0.1030f, 0.0973f);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float randomFloat(float p)
        {
            float3 p3 = frac(abs(float3(p, p, p)) * hashScale1);
            p3 += dot(p3, p3.yzx + 19.19f);
            return frac((p3.x + p3.y) * p3.z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float2 randomFloat2(float2 p)
        {
            float3 p3 = frac(abs(p.xyx) * hashScale2);
            p3 += dot(p3, p3.yzx + 19.19f);
            return frac((p3.xx + p3.yz) * p3.zy);
        }
    }
}
