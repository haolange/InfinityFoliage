Shader "Landscape/Grass"
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
		//_Cull("Cull", Int) = 0
	}

	HLSLINCLUDE
		#include "Include/Foliage.hlsl"
		#include "Include/ShaderVariable.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
		#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"

		int _TerrainSize;
		float _TerrainScaleY;
		Texture2D _AlbedoTexture, _NomralTexture, _TerrainHeightmap;
		SamplerState sampler_AlbedoTexture, sampler_NomralTexture, sampler_TerrainHeightmap;
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
				float3 normal : TEXCOORD1;
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
				output.normal = normalize(mul(input.normal, (float3x3)UNITY_MATRIX_M));
				output.vertexWS = mul(UNITY_MATRIX_M, input.vertexOS);
				output.vertexCS = mul(UNITY_MATRIX_VP, output.vertexWS);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);

				float3 worldPos = input.vertexWS.xyz;
				float4 color = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0);

				float crossFade = LODCrossDither(input.vertexCS.xy, unity_LODFade.x);
				if (crossFade >= 0.3f)
				{
					discard;
				}
				return color;
			}
            ENDHLSL
        }

		Pass
        {
            Name "ForwardLit-Instance"
			Tags { "LightMode" = "UniversalForward-Instance" }
			ZTest LEqual 
			ZWrite On 
			Cull Off

            HLSLPROGRAM
			#pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
			#pragma enable_d3d11_debug_symbols

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				//float3 normal : NORMAL;
				float4 vertexOS : POSITION;
			};

			struct Varyings
			{
				uint PrimitiveId  : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				//float3 normal : NORMAL;
				float4 vertexCS : SV_POSITION;
				float4 vertexWS : TEXCOORD1;
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
				FGrassBatch grassBatch = _GrassBatchBuffer[input.InstanceId];

				output.uv0 = input.uv0;
				//output.normal = normalize(mul(input.normal, (float3x3)unity_WorldToObject));
				output.vertexWS = mul(grassBatch.matrix_World, input.vertexOS);

				float4 leftTopH = _TerrainHeightmap.SampleLevel(Global_bilinear_clamp_sampler, (float2(1, 0) + grassBatch.position.xz) * rcp(_TerrainSize), 0, 0);
				float4 leftBottomH = _TerrainHeightmap.SampleLevel(Global_bilinear_clamp_sampler, grassBatch.position.xz * rcp(_TerrainSize), 0, 0);
				float4 rightTopH = _TerrainHeightmap.SampleLevel(Global_bilinear_clamp_sampler, (float2(1, 1) + grassBatch.position.xz) * rcp(_TerrainSize), 0, 0);
				float4 rightBottomH = _TerrainHeightmap.SampleLevel(Global_bilinear_clamp_sampler, (float2(0, 1) + grassBatch.position.xz) * rcp(_TerrainSize), 0, 0);
				float4 sampledHeight = SampleHeight(frac(rcp(_TerrainSize) * grassBatch.position.xz) + 0.5, leftBottomH, leftTopH, rightBottomH, rightTopH);
    			output.vertexWS.y += UnpackHeightmap(sampledHeight) * (_TerrainScaleY * 2);
				output.vertexCS = mul(unity_MatrixVP, output.vertexWS);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				float3 worldPos = input.vertexWS.xyz;
				//FGrassBatch grassBatch = _GrassBatchBuffer[input.PrimitiveId];

				float4 color = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0);
				if (color.a <= 0.3f)
				{
					discard;
				}
				return color;
			}
            ENDHLSL
        }
    }
}
