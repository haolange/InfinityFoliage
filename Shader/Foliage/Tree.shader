Shader "Landscape/Tree"
{
	Properties 
	{
        [Header(Surface)]
        [NoScaleOffset]_AlbedoTexture ("AlbedoTexture", 2D) = "white" {}

        [Header(Normal)]
        [NoScaleOffset]_NomralTexture ("NomralTexture", 2D) = "bump" {}

		[Header(State)]
		//_ZTest("ZTest", Int) = 4
		//_ZWrite("ZWrite", Int) = 1
		_Cull("Cull", Int) = 0
	}

    SubShader
    {
        Tags{"RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "RenderType" = "Opaque"}
		AlphaToMask On

        Pass
        {
            Name "ForwardLit"
			Tags { "LightMode" = "UniversalForward" }
			ZTest LEqual 
			ZWrite On 
			Cull [_Cull]
			AlphaTest Greater 0

            HLSLPROGRAM
			#pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
			#pragma multi_compile_instancing
			#pragma enable_d3d11_debug_symbols

			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"

			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float3 normal : NORMAL;
				float4 vertex : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float3 normal : TEXCOORD1;
				float4 vertex : SV_POSITION;
				float4 worldPos : TEXCOORD2;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

            Texture2D _AlbedoTexture; SamplerState sampler_AlbedoTexture;
            Texture2D _NomralTexture; SamplerState sampler_NomralTexture;

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(In);
				UNITY_TRANSFER_INSTANCE_ID(In, Out);

				Out.uv0 = In.uv0;
				Out.normal = normalize(mul(In.normal, (float3x3)unity_WorldToObject));
				Out.worldPos = mul(unity_ObjectToWorld, In.vertex);
				Out.vertex = mul(unity_MatrixVP, Out.worldPos);
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(In);

				float3 WS_PixelPos = In.worldPos.xyz;
				return _AlbedoTexture.Sample(sampler_AlbedoTexture, In.uv0);
			}
            ENDHLSL
        }

		Pass
        {
            Name "ForwardLit-Instance"
			Tags { "LightMode" = "UniversalForward" }
			ZTest LEqual 
			ZWrite On 
			Cull [_Cull]
			AlphaTest Greater 0

            HLSLPROGRAM
			#pragma target 4.5
            #pragma vertex vert
            #pragma fragment frag
			#pragma enable_d3d11_debug_symbols

			#include "Foliage.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
			#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/UnityInstancing.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/UnityInput.hlsl"

			struct Attributes
			{
				uint InstanceId : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				//float3 normal : NORMAL;
				float4 vertex : POSITION;
			};

			struct Varyings
			{
				uint PrimitiveId  : SV_InstanceID;
				float2 uv0 : TEXCOORD0;
				//float3 normal : TEXCOORD1;
				float4 vertex : SV_POSITION;
				float4 worldPos : TEXCOORD2;
			};

            Texture2D _AlbedoTexture; SamplerState sampler_AlbedoTexture;
            Texture2D _NomralTexture; SamplerState sampler_NomralTexture;

			Varyings vert(Attributes In)
			{
				Varyings Out = (Varyings)0;
				Out.PrimitiveId  = _TreeIndexBuffer[In.InstanceId + _TreeIndexOffset];
				FTreeBatch treeBatch = _TreeBatchBuffer[Out.PrimitiveId];

				Out.uv0 = In.uv0;
				//Out.normal = normalize(mul(In.normal, (float3x3)unity_WorldToObject));
				Out.worldPos = mul(treeBatch.matrix_World, In.vertex);
				Out.vertex = mul(unity_MatrixVP, Out.worldPos);
				return Out;
			}

			float4 frag(Varyings In) : SV_Target
			{
				float3 WS_PixelPos = In.worldPos.xyz;//
				return _AlbedoTexture.Sample(sampler_AlbedoTexture, In.uv0);
			}
            ENDHLSL
        }
    }
}
