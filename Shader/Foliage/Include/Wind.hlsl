#ifndef _WindInclude_
#define _WindInclude_

float2 g_Wind;
float2 g_Turbulence;
float3 g_WindDirection;
float4 g_SmoothTime;
//float4 _Time, _SinTime, _CosTime, unity_DeltaTime;
sampler2D g_GustNoise;

float pow2( float x )
{
    return x*x;
}

float4 SmoothCurve( float4 x )
{
    return x * x *( 3.0 - 2.0 * x );
}
float4 TriangleWave( float4 x )
{
    return abs( frac( x + 0.5 ) * 2.0 - 1.0 );
}
float4 SmoothTriangleWave( float4 x )
{
    return SmoothCurve( TriangleWave( x ) );
}

float4 FastSin( float4 x )
{
    #ifndef Pi
        #define Pi 3.14159265
    #endif
    #define DIVIDE_BY_PI 1.0 / (2.0 * Pi)
    return (SmoothTriangleWave( x * DIVIDE_BY_PI ) - 0.5) * 2;
}

float3 FixStretching( float3 vertex, float3 original, float3 center )
{
    return center + normalize(vertex - center) * length(original - center);
}

float3 RotateAroundAxis( float3 center, float3 original, float3 u, float angle )
{
    original -= center;
    float C = cos(angle);
    float S = sin(angle);
    float t = 1 - C;
    float m00 = t * u.x * u.x + C;
    float m01 = t * u.x * u.y - S * u.z;
    float m02 = t * u.x * u.z + S * u.y;
    float m10 = t * u.x * u.y + S * u.z;
    float m11 = t * u.y * u.y + C;
    float m12 = t * u.y * u.z - S * u.x;
    float m20 = t * u.x * u.z - S * u.y;
    float m21 = t * u.y * u.z + S * u.x;
    float m22 = t * u.z * u.z + C;
    float3x3 finalMatrix = float3x3( m00, m01, m02, m10, m11, m12, m20, m21, m22 );
    return mul( finalMatrix, original ) + center;
}

float3 RotateAroundAxisFast( float3 center, float3 original, float3 direction )
{
    return original + direction;
}

struct FWindInput
{
    // Global
    float speed;
    float3 direction;

    // Per-Object
    float fade;
    float3 objectPivot;

    // Per-Vertex
    float mask;
    float flutter;
    float phaseOffset;
    float3 normalWS;
};

float GetSmoothAmbientOffset()
{
    return g_SmoothTime.x;
}

/// <summary>
/// Returns the time for gusts.
/// Smoothly adjusted for changes in wind speed.
/// </summary>
float GetSmoothGustOffset()
{
    return g_SmoothTime.y;
}

/// <summary>
/// Returns the time for turbulence.
/// Smoothly adjusted for changes in wind speed.
/// </summary>
float GetSmoothTurbulenceOffset()
{
    return g_SmoothTime.z;
}

/// <summary>
/// Returns the global wind direction.
/// </summary>
float3 GetWindDirection()
{
    return g_WindDirection != 0 
        ? normalize(float3(g_WindDirection.x, 0, g_WindDirection.z)) 
        : float3(1, 0, 0);
}

/// <summary>
/// Returns the global wind speed.
/// </summary>
float GetWindSpeed()
{
    return g_Wind.x;
}

/// <summary>
/// Returns the global wind strength.
/// </summary>
float GetWindStrength()
{
    return g_Wind.y * _WindStrength;
}

/// <summary>
/// Returns a random windVariation value based on the object pivot.
/// </summary>
float GetWindVariation( 
    float3 objectPivot ) // The object pivot in world space.
{
    return 1.0 - frac( objectPivot.x * objectPivot.z * 10.0 ) * _WindVariation;
}

/// <summary>
/// Returns the global turbulence strength.
/// </summary>
float GetTurbulenceStrength()
{
    return g_Turbulence.y * _TurbulenceStrength;
}

float4 AmbientFrequency(
    float3 objectPivot,         // The object pivot in world space.
    float3 vertexWorldPosition, // The vertex position in world space.
    float3 windDirection,       // The wind direction in world space.
    float phaseOffset,          // The wind phase offset. (Range: 0-1)
    float speed )               // The wind speed.
{
    // Constant offset moving the noise texture slowly to the left to prevent
    // the same gust repeating at the same location.
    #ifdef PER_OBJECT_VALUES_CALCULATED
        float3 constantOffset = g_ConstantWindOffset * _Time.x * 0.5;
    #else
        float3 constantOffset = cross( windDirection.xyz, float3(0,1,0) ) * _Time.x * 0.5;
    #endif

    float footprint = 3;
    float time = GetSmoothAmbientOffset() - phaseOffset * footprint;

    #ifdef PER_OBJECT_VALUES_CALCULATED
        float pivotOffset = g_PivotOffset;
    #else
        float pivotOffset = length( float3(objectPivot.x, 0, objectPivot.z) );
    #endif

    float scale = 0.5;
    float frequency = pivotOffset * scale - time;
    return FastSin(
        float4(
            frequency + constantOffset.x, 
            frequency*0.5 + constantOffset.z, 
            frequency*0.25, 
            frequency*0.125) * speed );
}

/// <summary>
/// Calculates the ambient wind.
/// </summary>
float3 AmbientWind( 
    float3 objectPivot,         // The object pivot in world space.
    float3 vertexWorldPosition, // The vertex position in world space.
    float3 windDirection,       // The wind direction in world space.
    float phaseOffset )         // The wind phase offset. (Range: 0-1)
{
    float4 sine = AmbientFrequency( objectPivot, vertexWorldPosition, windDirection, phaseOffset, 1 );
    sine.w = abs(sine.w) + 0.5;
    float xz = 1.5 * sine.x * sine.z + sine.w + 1;
    float y  = 1 * sine.y * sine.z + sine.w;
    return windDirection * float3(xz, 0, xz) + float3(0, y, 0);
}

float3 SampleGust(
    float3 objectPivot, 
    float3 vertexWorldPosition,
    float3 windDirection,
    float phaseOffset, 
    float edgeFlutter,
    float lod )
{
    #if defined(_TYPE_TREE_LEAVES) || defined(_TYPE_TREE_BARK)
        float time = GetSmoothGustOffset() - phaseOffset * 0.05;
        lod = 5;
    #else
        float time = GetSmoothGustOffset() - phaseOffset * 0.05;
    #endif

    // Constant offset moving the noise texture slowly to the left to prevent
    // the same gust repeating at the same location.
    #ifdef PER_OBJECT_VALUES_CALCULATED
        float3 constantOffset = g_ConstantWindOffset * _Time.x * 0.5;
    #else
        float3 constantOffset = cross( windDirection.xyz, float3(0,1,0) ) * _Time.x * 0.5;
    #endif

    float2 windOffsetOverTime = windDirection.xz * time + constantOffset.xz;
    #if defined(_TYPE_TREE_LEAVES)
        float3 vertexOffset = vertexWorldPosition - objectPivot;
        float2 offset = objectPivot.xz * 0.02 - windOffsetOverTime + vertexOffset.xz * 0.0075 * edgeFlutter;
    #else
        float2 offset = objectPivot.xz * 0.02 - windOffsetOverTime;
    #endif
    float strength  = tex2Dlod( g_GustNoise, float4(offset, 0, lod) ).r;
    return strength * windDirection;
}

float3 Turbulence( 
    float3 objectPivot,         // The object pivot in world space.
    float3 vertexWorldPosition, // The vertex position in world space.
    float3 worldNormal,         // The direction of the turbulence in world space (usually vertex normal).
    float phaseOffset,          // The wind phase offset.
    float edgeFlutter,          // The amount of edge flutter for tree leaves. (Range: 0-1)
    float speed )               // The wind speed.
{
    #if defined(_TYPE_TREE_BARK)
        return float3(0, 0, 0);
    #else
        float time = GetSmoothTurbulenceOffset() - phaseOffset;
        float frequency = (objectPivot.x + objectPivot.y + objectPivot.z) * 2.5 - time;

        // TODO: Add a secondary frequency.
        float4 sine = 
            FastSin( 
                float4(
                    (1.65 * frequency) * speed, 
                    (2 * 1.65 * frequency) * speed, 
                    0,
                    0) );

        float x = 1 * sine.x + 1;
        float z = 1 * sine.y + 1;
        float y = (x + z) * 0.5;

        #if defined(_TYPE_TREE_LEAVES)
            return worldNormal * float3(x, y, z) * float3(1, .6, 1) * edgeFlutter;
        #else
            return worldNormal * float3(x, y, z) * float3(1, 0.35, 1);
        #endif
    #endif
}

float3 CombineWind(
    float3 ambient,     // Large constant ambient wind motion.
    float3 gust,        // Local gust based on noise texture.
    float3 turbulence,  // Constant turbulence.
    float3 shiver,      // High frequency shivering during gust.
    float4 strength     // The wind strength for each wind component.
    )
{
    ambient *= strength.x;
    gust *= strength.y;
    turbulence *= strength.z;
    shiver *= strength.w;

    // Trees require more displacement for the wind to be visible because the objects are larger.
    // These are magic numbers that give a nice balance between the grass/plants and trees,
    // based on a common tree size.
    #if defined(_TYPE_TREE_LEAVES) || defined(_TYPE_TREE_BARK)
        ambient *= 3;
        gust *= 1;
        turbulence *= 3;
        shiver *= 3;
    #endif

    float gustLength = length( gust );
    float increaseTurbelenceWithGust = smoothstep(0, 1, gustLength) + 1;

    // Calculate the balance between different wind types. 
    // If we do it here then we can keep the input parameters in a 0-1 range.
    ambient *= 0.1;
    gust *= 1.5;
    turbulence *= 0.15;
    shiver *= 0.15;

    #if defined(_DEBUG_AMBIENT)
        return ambient;
    #elif defined(_DEBUG_GUST)
        return gust;
    #elif defined(_DEBUG_TURBULENCE)
        return lerp(
            turbulence * increaseTurbelenceWithGust,
            shiver * increaseTurbelenceWithGust,
            gustLength);
    #else
        return
            ambient
                + gust
                + lerp(
                    turbulence * increaseTurbelenceWithGust,
                    shiver * increaseTurbelenceWithGust,
                    gustLength);
    #endif
}


float3 ComputeWind(FWindInput input, float3 positionWS)
{
    #if defined(_TYPE_GRASS) || defined(_TYPE_PLANT)
        input.phaseOffset += dot( input.direction, (positionWS - input.objectPivot) );
        input.phaseOffset += input.mask * 0.3;
    #endif

    float3 ambient =
        AmbientWind( 
            input.objectPivot, 
            positionWS, 
            input.direction, 
            input.phaseOffset );

    float3 gust = 
        SampleGust(
            input.objectPivot,
            positionWS,
            input.direction,
            input.phaseOffset,
            input.flutter,
            0 );

    // Add a bit of a random phase offset to the tree leaves. Phase Offset is calculated
    // per-branch and we don't want to have the same turbulence for the entire branch.
    #if defined(_TYPE_TREE_LEAVES)
        input.phaseOffset += 
            dot( input.direction, (positionWS - input.objectPivot) ) * input.flutter;
    #endif

    float3 turbulence1 = 
        Turbulence(
            input.objectPivot.xyz,
            positionWS.xyz,
            input.normalWS.xyz,
            input.phaseOffset,
            input.flutter,
            1 );

    float3 turbulence2 = 
        Turbulence(
            input.objectPivot.xyz,
            positionWS.xyz,
            input.normalWS.xyz,
            input.phaseOffset,
            input.flutter,
            2 );

    return CombineWind( 
        ambient, 
        gust, 
        turbulence1, 
        turbulence2, 
        float4(GetWindStrength().xx, GetTurbulenceStrength().xx) );
}

float3 ApplyWind(
    float3 positionWS, // Vertex position in world space.
    float3 objectPivot,         // Object Pivot in world space.
    float3 combinedWind,        // Combined Wind vector in world space.
    float mask,                 // Wind mask. (Range: 0-1)
    float distanceFade)         // Wind distance fade. (Range: 0-1)
{
    #if defined(_TYPE_GRASS)
        return FixStretching( 
                positionWS + combinedWind * mask * distanceFade, 
                positionWS, 
                float3( positionWS.x, objectPivot.y, positionWS.z ) ); // TODO: This does not work correctly if the grass is a larger patch and it is rotated. Ideally we would use  vertexOS.y transformed into world space instead of objectPivot.y.
    #elif defined(_TYPE_TREE_LEAVES) || defined(_TYPE_TREE_BARK)
        return FixStretching( 
                positionWS + combinedWind * mask * distanceFade * 4, 
                positionWS, 
                objectPivot);
    #else
        return FixStretching( 
                positionWS + combinedWind * mask * mask * distanceFade, 
                positionWS, 
                objectPivot);
    #endif
}

void Wind(FWindInput input, inout float3 positionWS, inout float3 normalWS)
{
    // Adjust the pivot for grass to use the XZ position of the vertex.
    // This is a decent workaround to get a per-grass-blade pivot until
    // we have proper pivot support.
    #ifdef _TYPE_GRASS
        input.objectPivot = float3(positionWS.x, input.objectPivot.y, positionWS.z);
    #endif

    // Compute wind.
    float3 wind = ComputeWind( input, positionWS );

    // Apply wind to vertex.
    float3 outputWS = ApplyWind(positionWS, input.objectPivot, wind, input.mask, input.fade);

    // Recalculate normals for grass
    #if defined(_TYPE_GRASS)
        float3 delta = outputWS - positionWS;
        normalWS = lerp(normalWS, normalWS + normalize( delta + float3(0, 0.1, 0) ), length(delta) * _RecalculateWindNormals * input.fade );
    #endif

    positionWS = outputWS;
}

/// <summary>
/// Returns the bend factor for the tree trunk.
/// X contains the bend factor for the entire tree, Y for the base.
/// </summary>
float2 GetTrunkBendFactor()
{
    return _TrunkBendFactor.xy;
}

float GetTrunkMask( 
    float3 vertex, float2 uv1, float treeHeight, float bendFactor, float baseBendFactor )
{
    #ifdef _BAKED_MESH_DATA
        float trunkMask = saturate( uv1.x * bendFactor );
    #else
        float trunkMask = pow2(saturate( vertex.y / treeHeight )) * bendFactor;
    #endif

    return saturate( trunkMask + saturate( vertex.y ) * baseBendFactor );
}

void Wind_Trunk( 
    float3 vertex,              // The vertex position in object space.
    float3 vertexWorldPosition, // The vertex position in world space.
    float3 vertexWithWind,      // The vertex position with wind displacement in world space.
    float2 uv1,                 // The second UV channel of the vertex. (UV1)
    float3 objectPivot,         // The object pivot in world space.
    float3 windDirection,       // The wind direction in world space. (normalized)
    out float3 vertexOut )
{
    // Additional properties. Either global or baked.
    float2 bendFactor = GetTrunkBendFactor();
    float trunkMask = GetTrunkMask( vertex, uv1, _PivotOffset, bendFactor.x,  bendFactor.y );
    float ambientStrength =  GetWindStrength();
    
    // Calculate Ambient Wind
    float4 trunkAmbient = 
        AmbientFrequency( 
            objectPivot, 
            vertexWorldPosition, 
            windDirection, 
            0, 
            0.75 ) + ambientStrength;
    trunkAmbient *= trunkMask;

    // Calculate Gust
    float3 trunkGust = 
        SampleGust( 
            objectPivot, vertexWorldPosition, windDirection, 0, 0, 7);
    trunkGust *= trunkMask;

    // Apply
    float gustFrequency = trunkAmbient.w * length(trunkGust);
    float baseFrequency1 = trunkAmbient.x;
    float baseFrequency2 = trunkAmbient.x + trunkAmbient.y;
    float baseFrequency = 
        lerp( baseFrequency1, baseFrequency2, (_SinTime.x + 1) * 0.5 * ambientStrength);
    
    // TODO: Use the "FixStretching" approach?
    vertexOut =
        RotateAroundAxis( 
            objectPivot, 
            vertexWithWind, 
            normalize( cross( float3(0,1,0) , windDirection ) ), 
            (baseFrequency * 0.75 + gustFrequency) * ambientStrength * 0.0375);
}
#endif