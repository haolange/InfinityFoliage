#ifndef _ViewFadeInclude
#define _ViewFadeInclude

void PerVertexFade(float3 objectPivot, out float windFade, out float scaleFade )
{
     #if defined(_TYPE_TREE_LEAVES) || defined(_TYPE_TREE_BARK)
          windFade = 1.0;
          scaleFade = 1.0;
     #else
          float distanceToCamera = distance(objectPivot, _WorldSpaceCameraPos);

          windFade = 1.0 - saturate((distanceToCamera - _WindFadeness.x) / _WindFadeness.y);
          scaleFade = 1.0 - saturate((distanceToCamera - _AlphaFadeness.x) / _AlphaFadeness.y);
     
          float dither = saturate(frac(objectPivot.x * 3 + objectPivot.y * 6) + (_ScaleDensity * 2 - 1)); 
          float fade = 1 - saturate((distanceToCamera - _AlphaFadeness.z) / _AlphaFadeness.w );
          scaleFade *= dither <= _ScaleDensity ? fade : 1;
     #endif
}

float3 ApplyScaleFade(float3 vertexWS, float3 objectPivot, float fade)
{
     vertexWS = lerp(objectPivot, vertexWS, max(fade, 0));
     return vertexWS;
}

#endif