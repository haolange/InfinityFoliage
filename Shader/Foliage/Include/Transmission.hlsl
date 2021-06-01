#ifndef _TransmissionInclude_
#define _TransmissionInclude_

float Square(float x)
{
    return x * x;
}

float HenyeyGreensteinPhase(float cosTheta, float g)
{
    return (1.0f - g * g) / (4.0f * PI * pow(1.0f + g * g - 2.0f * g * cosTheta, 1.5f));
}

float3 Transmission(float3 subsurfaceColor, float3 L, float3 V, float3 N)
{
    float Wrap = 0.5;
    float NoL = saturate((dot(-N, L) + Wrap) / Square(1 + Wrap));

    float VoL = saturate(dot(V, -L));
    float a = 0.6;
    float a2 = a * a;
    float d = ( VoL * a2 - VoL ) * VoL + 1;	
    float GGX = (a2 / PI) / (d * d);		
    return NoL * GGX * subsurfaceColor;
}

float3 Transmission(float3 subsurfaceColor, float3 L, float3 V, float3 N, float3 H, float occlusion, float thickness) 
{
    float InScatter = pow(saturate(dot(L, -V)), 12) * lerp(3, 0.1, thickness);
    float NormalContribution = saturate(dot(N, H) * thickness + 1 - thickness);
    float BackScatter = occlusion * NormalContribution / (PI * 2);
    return subsurfaceColor * lerp(BackScatter, 1, InScatter);
}
#endif