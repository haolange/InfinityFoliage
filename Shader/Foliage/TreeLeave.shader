Shader "Landscape/TreeLeave"
{
	Properties 
	{
        [Header(Surface)]
        [NoScaleOffset]_AlbedoTexture ("AlbedoTexture", 2D) = "white" {}

        [Header(Normal)]
        [NoScaleOffset]_NomralTexture ("NomralTexture", 2D) = "bump" {}

		//[Header(State)]
		//_ZTest("ZTest", Int) = 4
		//_ZWrite("ZWrite", Int) = 1
	}

	HLSLINCLUDE
		#include "Include/Foliage.hlsl"
		#include "Include/ShaderVariable.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
		/*#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Input.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
		#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"*/

		Texture2D _AlbedoTexture, _NomralTexture;
    	SamplerState sampler_AlbedoTexture, sampler_NomralTexture;
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
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols

			/*#pragma multi_compile _ _SHADOWS_SOFT
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS*/

			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float3 normal : NORMAL;
				float4 vertexOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float3 normal : NORMAL;
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
				output.normal = normalize(mul((float3x3)UNITY_MATRIX_M, input.normal));
				output.vertexWS = mul(UNITY_MATRIX_M, input.vertexOS);
				output.vertexCS = mul(UNITY_MATRIX_VP, output.vertexWS);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);

				float3 worldPos = input.vertexWS.xyz;

				//Shadow
				/*float4 shadowCoord = 0;
				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					shadowCoord = TransformWorldToShadowCoord(worldPos);
				#endif
				float shadowTream = MainLightRealtimeShadow(shadowCoord);*/

				//Lighting
				//float4 directDiffuse = saturate(dot(normalize(_MainLightPosition.xyz), input.normal.xyz)) * float4(_MainLightColor.rgb, 1) * shadowTream;

				//Surface
				float4 outColor = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0);
				//outColor.rgb *= directDiffuse.rgb;

				//CrossFade
				float crossFade = LODCrossDither(input.vertexCS.xy, unity_LODFade.x);
				if (crossFade >= 0.5f)
				{
					discard;
				}

				return outColor;
				//return float4(input.normal, 1);
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
			#pragma enable_d3d11_debug_symbols

			/*#pragma multi_compile _ _SHADOWS_SOFT
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _ADDITIONAL_LIGHT_SHADOWS
			#pragma multi_compile _ _MIXED_LIGHTING_SUBTRACTIVE
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
			#pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS*/

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float3 normal : NORMAL;
				float4 vertexOS : POSITION;
			};

			struct Varyings
			{
				uint PrimitiveId  : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				float3 normal : NORMAL;
				float4 vertexCS : SV_POSITION;
				float4 vertexWS : TEXCOORD1;
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				output.PrimitiveId  = _TreeIndexBuffer[input.InstanceId + _TreeIndexOffset];
				FTreeBatch treeBatch = _TreeBatchBuffer[output.PrimitiveId];

				output.uv0 = input.uv0;
				output.normal = normalize(mul((float3x3)treeBatch.matrix_World, input.normal));
				output.vertexWS = mul(treeBatch.matrix_World, input.vertexOS);
				output.vertexCS = mul(UNITY_MATRIX_VP, output.vertexWS);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				float3 worldPos = input.vertexWS.xyz;

				//Shadow
				/*float4 shadowCoord = 0;
				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					shadowCoord = TransformWorldToShadowCoord(worldPos);
				#endif
				float shadowTream = MainLightRealtimeShadow(shadowCoord);*/

				//Lighting
				//float4 directDiffuse = saturate(dot(normalize(_MainLightPosition.xyz), input.normal.xyz)) * float4(_MainLightColor.rgb, 1) * shadowTream;

				//Surface
				float4 outColor = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0);
				//outColor.rgb *= directDiffuse.rgb;
				if (outColor.a <= 0.3f)
				{
					discard;
				}

				return outColor;
				//return float4(input.normal, 1);
			}
            ENDHLSL
        }
    }
}
