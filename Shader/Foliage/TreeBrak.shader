Shader "Landscape/TreeBrak"
{
	Properties 
	{
        [Header(Texture)]
		[Toggle (_Mask)] _Mask ("Mask", Range(0, 1)) = 1
		[NoScaleOffset]_BrakMask ("BrakMask", 2D) = "white" {}
        [NoScaleOffset]_BrakColor ("BrakColor", 2D) = "white" {}
        [NoScaleOffset]_BrakNormal ("BrakNormal", 2D) = "bump" {}
		[NoScaleOffset]_TrunkColor ("TrunkColor", 2D) = "white" {}
        [NoScaleOffset]_TrunkNormal ("TrunkNormal", 2D) = "bump" {}

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

		Texture2D _BrakMask, _BrakColor, _BrakNormal, _TrunkColor, _TrunkNormal;
    	SamplerState sampler_BrakMask, sampler_BrakColor, sampler_BrakNormal, sampler_TrunkColor, sampler_TrunkNormal;

#if HAS_HALF
		half3 SampleSHCustom(half3 normalWS)
		{
			// LPPV is not supported in Ligthweight Pipeline
			half4 SHCoefficients[7];
			SHCoefficients[0] = half4(-0.0123161, -0.0050645, -0.0049810, 0.1772474);
			SHCoefficients[1] = half4(-0.0193599, 0.0411433, -0.0078300, 0.2203828);
			SHCoefficients[2] = half4(0.0331310, 0.1232350, -0.0134000, 0.2950366);
			SHCoefficients[3] = half4(0.0095811, -0.0038749, 0.0195689, 0.0144025);
			SHCoefficients[4] = half4(-0.0152499, -0.0061670, 0.0262519, 0.0208497);
			SHCoefficients[5] = half4(-0.0270614, -0.0109440, 0.0281989, 0.0296785);
			SHCoefficients[6] = half4(0.0342860, 0.0476035, 0.0587234, 1);

			return max(half3(0, 0, 0), SampleSH9(SHCoefficients, normalWS));
		}
#else
		float3 SampleSHCustom(float3 normalWS)
		{
			// LPPV is not supported in Ligthweight Pipeline
			float4 SHCoefficients[7];
			SHCoefficients[0] = float4(-0.0123161, -0.0050645, -0.0049810, 0.1772474);
			SHCoefficients[1] = float4(-0.0193599, 0.0411433, -0.0078300, 0.2203828);
			SHCoefficients[2] = float4(0.0331310, 0.1232350, -0.0134000, 0.2950366);
			SHCoefficients[3] = float4(0.0095811, -0.0038749, 0.0195689, 0.0144025);
			SHCoefficients[4] = float4(-0.0152499, -0.0061670, 0.0262519, 0.0208497);
			SHCoefficients[5] = float4(-0.0270614, -0.0109440, 0.0281989, 0.0296785);
			SHCoefficients[6] = float4(0.0342860, 0.0476035, 0.0587234, 1);

			return max(float3(0, 0, 0), SampleSH9(SHCoefficients, normalWS));
		}
#endif

		float LODCrossDither(uint2 fadeMaskSeed, float ditherFactor)
		{
			float p = GenerateHashedRandomFloat(fadeMaskSeed);
			return (ditherFactor - CopySign(p, ditherFactor));
			//clip(f);
		}
	ENDHLSL

    SubShader
    {
        Tags{"Queue" = "Geometry" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "RenderType" = "Opaque"}
		//AlphaToMask On

        Pass
        {
            Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }
			ZTest LEqual 
			ZWrite On 
			Cull Back

            HLSLPROGRAM
			#pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
			//#pragma enable_d3d11_debug_symbols

			#pragma shader_feature _Mask

			#pragma multi_compile_instancing
			#pragma multi_compile _ _SHADOWS_SOFT
			#pragma multi_compile __ LOD_FADE_CROSSFADE
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normalWS : NORMAL;
				float4 vertexCS : SV_POSITION;
				float4 vertexWS : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);

				output.uv0 = input.uv0;
				output.uv1 = input.uv1;
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

				float3 worldPos = input.vertexWS.xyz;

				//Surface
				float4 trunkColor = _TrunkColor.Sample(sampler_TrunkColor, input.uv0);
				float4 baseColor = trunkColor;
				#if _Mask
					float brakMask = _BrakMask.Sample(sampler_BrakMask, input.uv0).a;
					float4 brakColor = _BrakColor.Sample(sampler_BrakColor, input.uv1);
                    baseColor = lerp(trunkColor, brakColor, brakMask);
                #endif

				//Light&Shadow
				float4 shadowCoord = 0;
				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					shadowCoord = TransformWorldToShadowCoord(worldPos);
				#endif
				Light mainLight = GetMainLight(shadowCoord, worldPos, 1);
				//float lightShadow = MainLightRealtimeShadow(shadowCoord);
				float3 attenuatedLightColor = mainLight.color * (/*mainLight.distanceAttenuation */ mainLight.shadowAttenuation);

				//Lighting
				float3 directDiffuse = saturate(dot(normalize(_MainLightPosition.xyz), input.normalWS)) * attenuatedLightColor * baseColor.rgb;
				float3 indirectDiffuse = SampleSH(input.normalWS) * baseColor.rgb;
				return float4(directDiffuse + indirectDiffuse, 1);
			}
            ENDHLSL
        }

		Pass
        {
            Name "ForwardLit-Instance"
			Tags { "LightMode" = "UniversalForward-Instance" }
			ZTest LEqual 
			ZWrite On 
			Cull Back

            HLSLPROGRAM
			#pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
			//#pragma enable_d3d11_debug_symbols

			#pragma shader_feature _Mask
			
			#pragma multi_compile _ _SHADOWS_SOFT
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE

			#include "Packages/com.infinity.render-foliage/Shader/Foliage/Include/Foliage.hlsl"

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normalOS : NORMAL;
				float4 vertexOS : POSITION;
			};

			struct Varyings
			{
				uint PrimitiveId  : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float2 uv1 : TEXCOORD1;
				float3 normalWS : NORMAL;
				float4 vertexCS : SV_POSITION;
				float4 vertexWS : TEXCOORD2;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				output.PrimitiveId  = _TreeIndexBuffer[input.InstanceId];
				FTreeElement treeElement = _TreeElementBuffer[output.PrimitiveId];

				output.uv0 = input.uv0;
				output.uv1 = input.uv1;
				output.normalWS = normalize(mul((float3x3)treeElement.matrix_World, input.normalOS));
				output.vertexWS = mul(treeElement.matrix_World, input.vertexOS);
				output.vertexCS = mul(UNITY_MATRIX_VP, output.vertexWS);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				//Geometry Context
				float3 worldPos = input.vertexWS.xyz;

				//Surface
				float4 trunkColor = _TrunkColor.Sample(sampler_TrunkColor, input.uv0);
				float4 baseColor = trunkColor;
				#if _Mask
					float brakMask = _BrakMask.Sample(sampler_BrakMask, input.uv0).a;
					float4 brakColor = _BrakColor.Sample(sampler_BrakColor, input.uv1);
                    baseColor = lerp(trunkColor, brakColor, brakMask);
                #endif

				//Light&Shadow
				float4 shadowCoord = 0;
				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					shadowCoord = TransformWorldToShadowCoord(worldPos);
				#endif
				Light mainLight = GetMainLight(shadowCoord, worldPos, 1);
				//float lightShadow = MainLightRealtimeShadow(shadowCoord);
				float3 attenuatedLightColor = mainLight.color * (/*mainLight.distanceAttenuation */ mainLight.shadowAttenuation);

				//Lighting
				float3 directDiffuse = saturate(dot(normalize(_MainLightPosition.xyz), input.normalWS)) * attenuatedLightColor * baseColor.rgb;
				float3 indirectDiffuse = SampleSHCustom(input.normalWS) * baseColor.rgb;
				return float4(directDiffuse + indirectDiffuse, 1);
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
				#ifdef LOD_FADE_CROSSFADE 
					LODDitheringTransition(input.vertexCS.xy, unity_LODFade.x);
				#endif
				return 0;
			}
            ENDHLSL
        }		
    }
}
