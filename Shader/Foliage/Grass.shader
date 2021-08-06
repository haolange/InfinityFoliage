Shader "Landscape/Grass"
{
	Properties 
	{
		[Header(Transparency)]
        _AlphaThreshold("Alpha Threshold", Range(0.0, 1.0)) = 0.5
		_ScaleDensity ("Fade Density", Range(0.0, 0.99)) = 0.125
        _AlphaFadeness ("Fade Out Alpha", Vector) = (50, 20, 0, 0)
        [HideInInspector]_NatureRendererDistanceControl ("", Float) = 1

        [Header(Surface)]
        [NoScaleOffset]_AlbedoTexture ("Texture", 2D) = "white" {}
		[Toggle (_ColorMap)] ColorMap ("UseColorMap", Range(0, 1)) = 0
        _TopTint ("TopTint", Color) = (1, 1, 1, 1)
		_BottomTint ("BottomTint", Color) = (1, 1, 1, 1)
        _TintVariation ("Tint Variation", Color) = (1, 1, 1, 1)
        _ColorVariation ("Color Variation", Float) = 0.0005

		[Header(DetailDark)]
        _DarkTint ("Dark Tint", Color) = (1, 1, 1, 1)
        _DarkVariation ("Dark Variation", Float) = 0.0005
		_DarkFadeness("Fade Out Dark", Vector) = (50, 20, 0, 0)

        [Header(Normal)]
        [NoScaleOffset]_NomralTexture ("Texture", 2D) = "bump" {}
		_VertexNormalStrength("VertexStrength", Range(0, 1)) = 1

		[Header(Mesh)]
        _PivotOffset("Mesh Height Offset", Float) = 0.5

		[Header(Wind)]
		[Toggle (_PivotFromUV1)] PivotFromUV1 ("PivotFromUV1", Range(0, 1)) = 0
		_WindStrength("Wind Strength", Range(0, 2)) = 1
        _WindVariation("Wind Variation", Range(0, 1)) = 0.3
        _TurbulenceStrength("Wind Turbulence", Range(0, 2)) = 1
        _RecalculateWindNormals("Recalculate Normals", Range(0,1)) = 0.5
		_WindFadeness("Fade Out Wind", Vector) = (50, 20, 0, 0)
		
		[Header(Unuse)]
		_MainTex ("Base (RGB)", 2D) = "white" {}
		//[Header(State)]
		//_ZTest("ZTest", Int) = 4
		//_ZWrite("ZWrite", Int) = 1
	}

	HLSLINCLUDE
		cbuffer UnityPerMaterial 
		{
			float _ScaleDensity;
			float _AlphaThreshold;
			float _PivotOffset;
			float _WindStrength;
			float _DarkVariation, _WindVariation, _ColorVariation;
			float _TurbulenceStrength;
			float _VertexNormalStrength;
			float _RecalculateWindNormals;
			float4 _TopTint, _BottomTint;
			float4 _DarkTint, _TintVariation;
			float4 _TrunkBendFactor;
			float4 _DarkFadeness, _WindFadeness, _AlphaFadeness;
		};

		int _TerrainSize;
		float2 _WindFade;
		float4 _TerrainPivotScaleY;
		Texture2D _AlbedoTexture, _NomralTexture, _TerrainHeightmap, _TerrainNormalmap;
    	SamplerState sampler_AlbedoTexture, sampler_NomralTexture;

		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"
		
		#include "Include/Wind.hlsl"
		#include "Include/ViewFade.hlsl"
		#include "Include/Transmission.hlsl"
		#include "Include/ColorVariation.hlsl"
		#include "Include/ProceduralInstance.hlsl"
		#include "Packages/com.infinity.render-foliage/Shader/Foliage/Include/Foliage.hlsl"
	ENDHLSL

    SubShader
    {
        Tags{"Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "RenderType" = "Opaque" "NatureRendererInstancing" = "True"}
		AlphaToMask On

        Pass
        {
            Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }
			Cull Off ZTest LEqual ZWrite On 

            HLSLPROGRAM
			#pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
			#pragma multi_compile_instancing
			//#pragma enable_d3d11_debug_symbols
			#pragma instancing_options procedural:SetupNatureRenderer

			#pragma shader_feature _ColorMap
			#pragma shader_feature _PivotFromUV1

			#pragma multi_compile _ _SHADOWS_SOFT
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
			
			#define _TYPE_GRASS
			
			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD2;
				float4 color : COLOR;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float2 noise : TEXCOORD2;
				float4 color : COLOR;
				float3 normalWS : NORMAL;
				float4 vertexWS : TEXCOORD3;
				float4 vertexCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);

				float3 worldPos = mul(UNITY_MATRIX_M, input.vertexOS).xyz;
				float3 objectPos = float3(UNITY_MATRIX_M[0].w, UNITY_MATRIX_M[1].w, UNITY_MATRIX_M[2].w);
				#if _PivotFromUV1
                    objectPos.xz += input.uv1;
                #endif

				float windFade;
                float scaleFade;
                PerVertexFade(objectPos, windFade, scaleFade);
				output.noise.y = PerlinNoise(objectPos.xz, _DarkVariation);
				output.noise.x = PerlinNoise(objectPos.xz, _ColorVariation);
				output.noise.y *= 1.0 - saturate((distance(objectPos, _WorldSpaceCameraPos) - _DarkFadeness.x) / _DarkFadeness.y);

				FWindInput windInput;
                windInput.fade = windFade;
                windInput.flutter = 1;
                windInput.phaseOffset = 0;
                windInput.speed = GetWindSpeed();
                windInput.objectPivot = objectPos;
                windInput.normalWS = output.normalWS;
                windInput.direction = GetWindDirection();
                windInput.mask = input.uv0.y * saturate(input.vertexOS.y / _PivotOffset) * GetWindVariation(objectPos);
				Wind(windInput, worldPos, output.normalWS);
				worldPos = ApplyScaleFade(worldPos, objectPos, scaleFade);

				output.uv0 = input.uv0;
				output.uv1 = input.uv1;
				output.color = input.color;
				output.vertexWS = float4(worldPos, 1);
				output.vertexCS = mul(UNITY_MATRIX_VP, output.vertexWS);
				output.normalWS = TransformObjectToWorldNormal(input.normalOS);
				output.normalWS = lerp(float3(0, 1, 0), output.normalWS, _VertexNormalStrength);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);

				//Geometry Context
				float3 normalWS = input.normalWS;
				float3 worldPos = input.vertexWS.xyz;
				float3 objectPos = float3(UNITY_MATRIX_M[0].w, UNITY_MATRIX_M[1].w, UNITY_MATRIX_M[2].w);
				#if _PivotFromUV1
                    objectPos.xz += input.uv1;
                #endif

				//Surface
				float4 baseColor = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0);
				clip(baseColor.a);
				//clip(baseColor.a - _AlphaThreshold);

				float3 variantColor = lerp(lerp(_BottomTint.rgb, _TopTint.rgb, input.uv0.y), _TintVariation.rgb, input.noise.x);
				variantColor = lerp(variantColor, _DarkTint.rgb, input.noise.y);

				//BXDF Context
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                float3 halfDir = normalize(viewDir + lightDir);

				//Light&Shadow
				float4 shadowCoord = 0;
				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					shadowCoord = TransformWorldToShadowCoord(worldPos);
				#endif
				Light mainLight = GetMainLight(shadowCoord, worldPos, 1);
				//float lightShadow = MainLightRealtimeShadow(shadowCoord);
				float3 attenuatedLightColor = mainLight.color * (/*mainLight.distanceAttenuation */ mainLight.shadowAttenuation);

				//Lighting
				float3 directDiffuse = saturate(dot(lightDir, normalWS.xyz));
				#if _ColorMap
                    directDiffuse *= baseColor.rgb;
                #endif

				float3 indirectDiffuse = SampleSH(normalWS);
				#if _ColorMap
                    indirectDiffuse *= baseColor.rgb;
                #endif

				float3 subsurfaceColor = Transmission(baseColor.rgb, lightDir, viewDir, normalWS, halfDir, 1, 0.32) * 2;

				//Surface
				float3 outColor = variantColor * (indirectDiffuse + (directDiffuse + subsurfaceColor) * attenuatedLightColor);
				return float4(outColor, baseColor.a);
			}
            ENDHLSL
        }

		Pass
        {
            Name "ForwardLit-Instance"
			Tags { "LightMode" = "UniversalForward-Instance" }
			ZTest LEqual ZWrite On Cull Off
			
            HLSLPROGRAM
			#pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
			//#pragma enable_d3d11_debug_symbols

			#pragma shader_feature _ColorMap
			#pragma shader_feature _PivotFromUV1

			#pragma multi_compile _ _SHADOWS_SOFT
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float4 uv1 : TEXCOORD1;
				float4 color : COLOR;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
			};

			struct Varyings
			{
				uint PrimitiveId  : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float4 uv1 : TEXCOORD1;
				float2 noise : TEXCOORD2;
				float4 color : COLOR;
				float3 normalWS : NORMAL;
				float4 vertexWS : TEXCOORD3;
				float4 vertexCS : SV_POSITION;
			};

			float4 Blerp(float4 c00, float4 c10, float4 c01, float4 c11, float tx, float ty)
			{
				return lerp(lerp(c00, c10, tx), lerp(c01, c11, tx), ty);
			}

			float4 SampleHeight(float2 p, float4 leftBottomH, float4 leftTopH, float4 rightBottomH, float4 rightTopH)
			{
				return Blerp(leftBottomH, rightBottomH, leftTopH, rightTopH, p.x, p.y);
			}

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				output.PrimitiveId = input.InstanceId;
				FGrassElement grassElement = _GrassElementBuffer[input.InstanceId];

				float3 worldPos = mul(grassElement.matrix_World, input.vertexOS).xyz;
				float3 objectPos = float3(grassElement.matrix_World[0].w, grassElement.matrix_World[1].w, grassElement.matrix_World[2].w);

				/*float invSize = rcp(_TerrainSize);
				float2 sampleUV = (objectPos.xz - _TerrainPivotScaleY.xz) * invSize;
				float4 leftTopH = _TerrainHeightmap.SampleLevel(Global_point_clamp_sampler,  sampleUV, 0, int2(1, 0));
				float4 rightTopH = _TerrainHeightmap.SampleLevel(Global_point_clamp_sampler, sampleUV, 0, int2(1, 1));
				float4 leftBottomH = _TerrainHeightmap.SampleLevel(Global_bilinear_clamp_sampler, sampleUV, 0, 0);
				float4 rightBottomH = _TerrainHeightmap.SampleLevel(Global_point_clamp_sampler, sampleUV, 0, int2(0, 1));
				float4 sampledHeight = SampleHeight(frac(objectPos.xz * invSize), leftBottomH, leftTopH, rightBottomH, rightTopH);
				float height = UnpackHeightmap(sampledHeight) * (_TerrainPivotScaleY.w * 2);*/

				float2 sampleUV = (objectPos.xz - _TerrainPivotScaleY.xz) * rcp(_TerrainSize);
				float4 sampledHeight = _TerrainHeightmap.SampleLevel(Global_bilinear_clamp_sampler, sampleUV, 0, 0);
				float height = UnpackHeightmap(sampledHeight) * (_TerrainPivotScaleY.w * 2);
				float3 normal = _TerrainNormalmap.SampleLevel(Global_bilinear_clamp_sampler, sampleUV, 0, 0) * 2 - 1;

				worldPos.y += height;
				objectPos.y += height;

				float windFade;
                float scaleFade;
                PerVertexFade(objectPos, windFade, scaleFade);
				output.noise.y = PerlinNoise(objectPos.xz, _DarkVariation);
				output.noise.x = PerlinNoise(objectPos.xz, _ColorVariation);
				output.noise.y *= 1.0 - saturate((distance(objectPos, _WorldSpaceCameraPos) - _DarkFadeness.x) / _DarkFadeness.y);

				FWindInput windInput;
                windInput.fade = windFade;
                windInput.flutter = 1;
                windInput.phaseOffset = 0;
                windInput.speed = GetWindSpeed();
                windInput.objectPivot = objectPos;
                windInput.normalWS = output.normalWS;
                windInput.direction = GetWindDirection();
                windInput.mask = input.uv0.y * saturate(input.vertexOS.y / _PivotOffset) * GetWindVariation(objectPos);
				Wind(windInput, worldPos, output.normalWS);
				worldPos = ApplyScaleFade(worldPos, objectPos, scaleFade);

				output.uv0 = input.uv0;
				output.uv1 = input.uv1;
				output.color = input.color;
				output.vertexWS = float4(worldPos, 1);
				output.vertexCS = mul(unity_MatrixVP, output.vertexWS);
				output.normalWS = normalize(mul(input.normalOS, (float3x3)unity_WorldToObject));
				output.normalWS = lerp(normal, output.normalWS, _VertexNormalStrength);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				//Geometry Context
				FGrassElement grassElement = _GrassElementBuffer[input.PrimitiveId];
				float3 normalWS = input.normalWS;
				float3 worldPos = input.vertexWS.xyz;
				float3 objectPos = float3(grassElement.matrix_World[0].w, grassElement.matrix_World[1].w, grassElement.matrix_World[2].w);

				//Surface
				float4 baseColor = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0);
				clip(baseColor.a);
				//clip(baseColor.a - _AlphaThreshold);

				float3 variantColor = lerp(lerp(_BottomTint.rgb, _TopTint.rgb, input.uv0.y), _TintVariation.rgb, input.noise.x);
				variantColor = lerp(variantColor, _DarkTint.rgb, input.noise.y);

				//BXDF Context
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                float3 halfDir = normalize(viewDir + lightDir);

				//Light&Shadow
				float4 shadowCoord = 0;
				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					shadowCoord = TransformWorldToShadowCoord(worldPos);
				#endif
				Light mainLight = GetMainLight(shadowCoord, worldPos, 1);
				//float lightShadow = MainLightRealtimeShadow(shadowCoord);
				float3 attenuatedLightColor = mainLight.color * (/*mainLight.distanceAttenuation */ mainLight.shadowAttenuation);

				//Lighting
				float3 directDiffuse = saturate(dot(lightDir, normalWS.xyz));
				#if _ColorMap
                    directDiffuse *= baseColor.rgb;
                #endif

				float3 indirectDiffuse = SampleSH(normalWS);
				#if _ColorMap
                    indirectDiffuse *= baseColor.rgb;
                #endif

				float3 subsurfaceColor = Transmission(baseColor.rgb, lightDir, viewDir, normalWS, halfDir, 1, 0.32) * 2;

				//Surface
				float3 outColor = variantColor * (indirectDiffuse + (directDiffuse + subsurfaceColor) * attenuatedLightColor);
				return float4(outColor, baseColor.a);
			}
            ENDHLSL
        }

        Pass
        {
            Name "Shadowmap"
			Tags { "LightMode" = "ShadowCaster" }
			Cull Off ZTest LEqual ZWrite On 

            HLSLPROGRAM
			#pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
			#pragma multi_compile_instancing
			#pragma shader_feature _PivotFromUV1
			//#pragma enable_d3d11_debug_symbols
			#pragma instancing_options procedural:SetupNatureRenderer

			#define _TYPE_GRASS
			
			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float4 uv1 : TEXCOORD2;
				float4 color : COLOR;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float4 uv1 : TEXCOORD1;
				float4 color : COLOR;
				float3 normalWS : NORMAL;
				float4 vertexWS : TEXCOORD2;
				float4 vertexCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			float3 ApplyShadowBias(float depthBias, float normalBias, float3 positionWS, float3 normalWS, float3 lightDirection)
			{
				float invNdotL = 1.0 - saturate(dot(lightDirection, normalWS));
				float scale = invNdotL * normalBias;

				// normal bias is negative since we want to apply an inset normal offset
				positionWS = lightDirection * depthBias + positionWS;
				positionWS = normalWS * scale.xxx + positionWS;
				return positionWS;
			}

            float4 UnityWorldToClipPos(float3 positionWS, float3 normalWS)
            {
				float3 vertexWS_Bias = ApplyShadowBias(-0.05, -0, positionWS, normalWS, normalize(_MainLightPosition.xyz));
				float4 positionCS = mul(UNITY_MATRIX_VP, float4(vertexWS_Bias, 1));

				#if UNITY_REVERSED_Z
					positionCS.z = min(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
				#else
					positionCS.z = max(positionCS.z, positionCS.w * UNITY_NEAR_CLIP_VALUE);
				#endif

				return positionCS;
            }

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);

				output.uv0 = input.uv0;
				output.uv1 = input.uv1;
				output.color = input.color;
				output.normalWS = normalize(mul((float3x3)UNITY_MATRIX_M, input.normalOS));
				output.vertexWS = mul(UNITY_MATRIX_M, input.vertexOS);
				float3 objectPos = float3(UNITY_MATRIX_M[0].w, UNITY_MATRIX_M[1].w, UNITY_MATRIX_M[2].w);
				#if _PivotFromUV1
                    objectPos.xz += input.uv1;
                #endif

				float windFade;
                float scaleFade;
                PerVertexFade(objectPos, windFade, scaleFade);

				FWindInput windInput;
                windInput.fade = windFade;
                windInput.flutter = 1;
                windInput.phaseOffset = 0;
                windInput.speed = GetWindSpeed();
                windInput.objectPivot = objectPos;
                windInput.normalWS = output.normalWS;
                windInput.direction = GetWindDirection();
                windInput.mask = input.uv0.y * saturate(input.vertexOS.y / _PivotOffset) * GetWindVariation(objectPos);
				Wind(windInput, output.vertexWS.xyz, output.normalWS);
				output.vertexWS.xyz = ApplyScaleFade(output.vertexWS.xyz, objectPos, scaleFade);
				output.vertexCS = UnityWorldToClipPos(output.vertexWS.xyz, output.normalWS);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);

				float alpha = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0).a;
				clip(alpha - 0.33);
				//clip(alpha - _AlphaThreshold);
				return 0;
			}
            ENDHLSL
        }
    }
}
