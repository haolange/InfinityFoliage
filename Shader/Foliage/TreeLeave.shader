Shader "Landscape/TreeLeave"
{
	Properties 
	{
        [Header(Surface)]
        [NoScaleOffset]_AlbedoTexture ("AlbedoTexture", 2D) = "white" {}

        [Header(Normal)]
        [NoScaleOffset]_NomralTexture ("NomralTexture", 2D) = "bump" {}

		[Header(Transparency)]
        _AlphaThreshold("Alpha Threshold", Range(0.0, 1.0)) = 0.5
		//[Header(State)]
		//_ZTest("ZTest", Int) = 4
		//_ZWrite("ZWrite", Int) = 1
	}

	HLSLINCLUDE
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

		cbuffer UnityPerMaterial 
		{
			float _AlphaThreshold;
		};

		Texture2D _AlbedoTexture, _NomralTexture;
    	SamplerState sampler_AlbedoTexture, sampler_NomralTexture;

		float LODCrossDither(uint2 fadeMaskSeed, float ditherFactor)
		{
			float p = GenerateHashedRandomFloat(fadeMaskSeed);
			return (ditherFactor - CopySign(p, ditherFactor));
			//clip(f);
		}
	ENDHLSL

    SubShader
    {
        Tags{"Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "RenderType" = "Opaque"}
		AlphaToMask On

        Pass
        {
            Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }
			ZTest LEqual 
			ZWrite On 
			Cull Off

            HLSLPROGRAM
			#pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
			//#pragma enable_d3d11_debug_symbols
			//#pragma fragmentoption ARB_precision_hint_nicest

			#pragma multi_compile_instancing
			#pragma multi_compile _ _SHADOWS_SOFT
			#pragma multi_compile __ LOD_FADE_CROSSFADE
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
			
			#include "Include/Transmission.hlsl"

			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float3 normalWS : NORMAL;
				float4 vertexCS : SV_POSITION;
				float4 vertexWS : TEXCOORD1;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);

				output.uv0 = input.uv0;
				output.normalWS = normalize(mul((float3x3)UNITY_MATRIX_M, input.normalOS));
				output.vertexWS = mul(UNITY_MATRIX_M, input.vertexOS);
				output.vertexCS = mul(UNITY_MATRIX_VP, output.vertexWS);
				//output.vertexCS.z = output.vertexCS.w;
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);
				#ifdef LOD_FADE_CROSSFADE 
					LODDitheringTransition(input.vertexCS.xy, unity_LODFade.x);
				#endif

				//Surface
				float4 baseColor = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0);
				//clip(baseColor.a);
				//clip(baseColor.a - _AlphaThreshold);

				//Geometry Context
				float3 worldPos = input.vertexWS.xyz;
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                float3 halfDir = normalize(viewDir + lightDir);

				//Shadow
				float4 shadowCoord = 0;
				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					shadowCoord = TransformWorldToShadowCoord(worldPos);
				#endif
				Light mainLight = GetMainLight(shadowCoord, worldPos, 1);
				//float lightShadow = MainLightRealtimeShadow(shadowCoord);
				float3 lightAttenuated = mainLight.color * (/*mainLight.distanceAttenuation */ mainLight.shadowAttenuation);

				//Lighting
				float3 directDiffuse = saturate(dot(normalize(_MainLightPosition.xyz), input.normalWS)) * baseColor.rgb;
				float3 indirectDiffuse = SampleSH(input.normalWS) * baseColor.rgb;
				float3 subsurfaceColor = Transmission(baseColor.rgb * float3(0.95, 1, 0), lightDir, viewDir, input.normalWS, halfDir, 1, 0.25) * 2;
				return float4(indirectDiffuse + (directDiffuse + subsurfaceColor) * lightAttenuated, baseColor.a - _AlphaThreshold);
			}
            ENDHLSL
        }

		Pass
        {
            Name "ForwardLit-Instance"
			Tags { "LightMode" = "UniversalForward-Instance" }
			ZTest LEqual 
			ZWrite On 
			Cull [_Cull]

            HLSLPROGRAM
			#pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
			//#pragma enable_d3d11_debug_symbols

			#pragma multi_compile _ _SHADOWS_SOFT
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

			#include "Include/Foliage.hlsl"
			#include "Include/Transmission.hlsl"

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
			};

			struct Varyings
			{
				uint PrimitiveId  : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float3 normalWS : NORMAL;
				float4 vertexCS : SV_POSITION;
				float4 vertexWS : TEXCOORD1;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				output.PrimitiveId  = _TreeIndexBuffer[input.InstanceId];
				FTreeElement treeElement = _TreeElementBuffer[output.PrimitiveId];

				output.uv0 = input.uv0;
				output.normalWS = normalize(mul((float3x3)treeElement.matrix_World, input.normalOS));
				output.vertexWS = mul(treeElement.matrix_World, input.vertexOS);
				output.vertexCS = mul(UNITY_MATRIX_VP, output.vertexWS);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				//Surface
				float4 baseColor = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0);
				//clip(baseColor.a);
				//clip(baseColor.a - _AlphaThreshold);

				//Geometry Context
				float3 worldPos = input.vertexWS.xyz;
                float3 lightDir = normalize(_MainLightPosition.xyz);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                float3 halfDir = normalize(viewDir + lightDir);

				//Shadow
				float4 shadowCoord = 0;
				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					shadowCoord = TransformWorldToShadowCoord(worldPos);
				#endif
				Light mainLight = GetMainLight(shadowCoord, worldPos, 1);
				//float lightShadow = MainLightRealtimeShadow(shadowCoord);
				float3 lightAttenuated = mainLight.color * (/*mainLight.distanceAttenuation */ mainLight.shadowAttenuation);

				//Lighting
				float3 directDiffuse = saturate(dot(normalize(_MainLightPosition.xyz), input.normalWS)) * baseColor.rgb;
				float3 indirectDiffuse = SampleSH(input.normalWS) * baseColor.rgb;
				float3 subsurfaceColor = Transmission(baseColor.rgb * float3(0.95, 1, 0), lightDir, viewDir, input.normalWS, halfDir, 1, 0.25) * 2;

				return float4(indirectDiffuse + (directDiffuse + subsurfaceColor) * lightAttenuated, baseColor.a - _AlphaThreshold);
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
			#pragma multi_compile __ LOD_FADE_CROSSFADE
			//#pragma enable_d3d11_debug_symbols
			
			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float4 color : COLOR;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				//float noise : TEXCOORD1;
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

				//float3 objectPos = float3(UNITY_MATRIX_M[0].w, UNITY_MATRIX_M[1].w, UNITY_MATRIX_M[2].w);

				output.uv0 = input.uv0;
				output.color = input.color;
				output.normalWS = normalize(mul((float3x3)UNITY_MATRIX_M, input.normalOS));
				output.vertexWS = mul(UNITY_MATRIX_M, input.vertexOS);
				output.vertexCS = UnityWorldToClipPos(output.vertexWS.xyz, output.normalWS);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);

				//Surface
				float4 baseColor = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0);
				clip(baseColor.a - 0.33);
				//clip(baseColor.a - _AlphaThreshold);
				
				#ifdef LOD_FADE_CROSSFADE 
					LODDitheringTransition(input.vertexCS.xy, unity_LODFade.x);
				#endif
				return 0;
			}
            ENDHLSL
        }	
    }
}
