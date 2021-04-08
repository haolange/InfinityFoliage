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
				if (crossFade >= 0.5f)
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

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				Out.PrimitiveId = In.InstanceId;
				FGrassBatch grassBatch = _GrassBatchBuffer[In.InstanceId];

				Out.uv0 = In.uv0;
				//Out.normal = normalize(mul(In.normal, (float3x3)unity_WorldToObject));
				Out.vertexWS = mul(grassBatch.matrix_World, In.vertexOS);
				//float Height = _TerrainHeightmap.SampleLevel(Global_point_clamp_sampler, grassBatch.position.xz * rcp(_TerrainSize), 0, 0);
    			//Out.vertexWS.y += UnpackHeightmap(Height) * (_TerrainScaleY * 2);

				Out.vertexCS = mul(unity_MatrixVP, Out.vertexWS);
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
				float3 WS_PixelPos = In.vertexWS.xyz;
				float4 color = _AlbedoTexture.Sample(sampler_AlbedoTexture, In.uv0);
				if (color.a <= 0.5f)
				{
					discard;
				}
				return color;
			}
            ENDHLSL
        }
    }
}
