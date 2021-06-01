Shader "Landscape/Grass"
{
	Properties 
	{
		[Header(Transparency)]
        _AlphaThreshold("Alpha Threshold", Range(0.0, 1.0)) = 0.5
        _AlphaFadeness ("Alpha Fadeness", Vector) = (50, 20, 0, 0)
        [HideInInspector]_NatureRendererDistanceControl ("", Float) = 1

        [Header(Surface)]
        [NoScaleOffset]_AlbedoTexture ("Texture", 2D) = "white" {}
        _Tint ("Tint", Color) = (1, 1, 1, 1)
        _TintVariation ("Tint Variation", Color) = (1, 1, 1, 1)
        _ColorVariation ("Color Variation", Float) = 0.0005

        [Header(Normal)]
        [NoScaleOffset]_NomralTexture ("Texture", 2D) = "bump" {}
		_VertexNormalStrength("VertexStrength", Range(0, 1)) = 1

		[Header(Wind)]
        _PivotOffset("PivotOffsetY", Float) = 0.5
        _WindVariation("Wind Variation", Range(0, 1)) = 0.3
        _WindStrength("Wind Strength", Range(0, 2)) = 1
        _TurbulenceStrength("Turbulence Strength", Range(0, 2)) = 1
        _RecalculateWindNormals("Recalculate Normals", Range(0,1)) = 0.5
		_WindFadeness("Wind Fadeness", Vector) = (50, 20, 0, 0)

		//[Header(State)]
		//_ZTest("ZTest", Int) = 4
		//_ZWrite("ZWrite", Int) = 1
	}

	HLSLINCLUDE
		cbuffer UnityPerMaterial 
		{
			float _PivotOffset;
			float _WindStrength;
			float _WindVariation;
			float _ColorVariation;
			float _TurbulenceStrength;
			float _VertexNormalStrength;
			float _RecalculateWindNormals;

			float2 _WindFade;

			float4 _Tint;
			float4 _TintVariation;
			float4 _TrunkBendFactor;
		};

		int _TerrainSize;
		float _AlphaThreshold;
		float4 _AlphaFadeness, _WindFadeness, _TerrainPivotScaleY;
		Texture2D _AlbedoTexture, _NomralTexture, _TerrainHeightmap;
    	SamplerState sampler_AlbedoTexture, sampler_NomralTexture, sampler_TerrainHeightmap;

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
	ENDHLSL

    SubShader
    {
        Tags{"Queue" = "AlphaTest" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "RenderType" = "Opaque" "NatureRendererInstancing" = "True"}
		AlphaToMask Off

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

			#pragma multi_compile _ _SHADOWS_SOFT
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS
			#pragma multi_compile _ _MAIN_LIGHT_SHADOWS_CASCADE
			
			#define _TYPE_GRASS
			
			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float4 color : COLOR;
				float3 normalLS : NORMAL;
				float4 vertexOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float noise : TEXCOORD1;
				float4 color : COLOR;
				float3 normalWS : NORMAL;
				float4 vertexWS : TEXCOORD2;
				float4 vertexCS : SV_POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			Varyings vert(Attributes input)
			{
				Varyings output = (Varyings)0;
				UNITY_SETUP_INSTANCE_ID(input);
				UNITY_TRANSFER_INSTANCE_ID(input, output);

				output.uv0 = input.uv0;
				output.color = input.color;
				output.vertexWS = mul(UNITY_MATRIX_M, input.vertexOS);
				output.normalWS = normalize(mul((float3x3)UNITY_MATRIX_M, input.normalLS));
				float3 objectPos = float3(UNITY_MATRIX_M[0].w, UNITY_MATRIX_M[1].w, UNITY_MATRIX_M[2].w);

				float windFade;
                float scaleFade;
                PerVertexFade(objectPos, windFade, scaleFade);
				output.noise = PerlinNoise(objectPos.xz, _ColorVariation);

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

				output.vertexCS = mul(UNITY_MATRIX_VP, output.vertexWS);
				output.normalWS = lerp(float3(0, 1, 0), output.normalWS, _VertexNormalStrength);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);

				float3 normalWS = input.normalWS;
				float3 worldPos = input.vertexWS.xyz;
				float3 objectPos = float3(UNITY_MATRIX_M[0].w, UNITY_MATRIX_M[1].w, UNITY_MATRIX_M[2].w);

				float4 baseColor = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0);

                float3 lightDir = normalize(_MainLightPosition.xyz);
                float3 viewDir = normalize(_WorldSpaceCameraPos.xyz - worldPos);
                float3 halfDir = normalize(viewDir + lightDir);

				//Shadow
				float4 shadowCoord = 0;
				#if defined(MAIN_LIGHT_CALCULATE_SHADOWS)
					shadowCoord = TransformWorldToShadowCoord(worldPos);
				#endif
				float lightShadow = MainLightRealtimeShadow(shadowCoord);

				//Lighting
				//float phaseHG = HenyeyGreensteinPhase(saturate(dot(viewDir, lightDir)), 0.75) * 8;
				float3 lightColor = _MainLightColor.rgb;
				float3 directDiffuse = baseColor.rgb * saturate(dot(lightDir, normalWS.xyz));
				float3 subsurfaceColor = Transmission(baseColor.rgb * float3(1, 1, 0.25), lightDir, viewDir, normalWS, halfDir, 1, 0.25);

				//Surface
				float3 outColor = lerp(_TintVariation.rgb, _Tint.rgb, input.noise);
				//outColor *= input.uv0.y;
				outColor *= (directDiffuse + subsurfaceColor) * lightColor * lightShadow;
				
				clip(baseColor.a - _AlphaThreshold);
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

			#include "Packages/com.infinity.render-foliage/Shader/Foliage/Include/Foliage.hlsl"

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

				float4 worldPos = mul(grassBatch.matrix_World, input.vertexOS);

				float invSize = rcp(_TerrainSize);
				float3 position = grassBatch.position - _TerrainPivotScaleY.xyz;
				float4 leftTopH = _TerrainHeightmap.SampleLevel(Global_bilinear_clamp_sampler, (float2(1, 0) + position.xz) * invSize, 0, 0);
				float4 leftBottomH = _TerrainHeightmap.SampleLevel(Global_bilinear_clamp_sampler, position.xz * invSize, 0, 0);
				float4 rightTopH = _TerrainHeightmap.SampleLevel(Global_bilinear_clamp_sampler, (float2(1, 1) + position.xz) * invSize, 0, 0);
				float4 rightBottomH = _TerrainHeightmap.SampleLevel(Global_bilinear_clamp_sampler, (float2(0, 1) + position.xz) * invSize, 0, 0);
				float4 sampledHeight = SampleHeight(floor(grassBatch.position.xz * invSize) + 0.5, leftBottomH, leftTopH, rightBottomH, rightTopH);
				worldPos.y += UnpackHeightmap(sampledHeight) * (_TerrainPivotScaleY.w * 2);

				output.uv0 = input.uv0;
				output.normal = normalize(mul(input.normal, (float3x3)unity_WorldToObject));
				output.vertexWS = worldPos;
				output.vertexCS = mul(unity_MatrixVP, output.vertexWS);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				//float3 worldPos = input.vertexWS.xyz;
				//FGrassBatch grassBatch = _GrassBatchBuffer[input.PrimitiveId];

				float4 color = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0);
				if (color.a <= 0.5f)
				{
					discard;
				}
				return float4(color.rgb, 1);
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
			#pragma enable_d3d11_debug_symbols
			#pragma instancing_options procedural:SetupNatureRenderer

			#define _TYPE_GRASS
			
			struct Attributes
			{
				float2 uv0 : TEXCOORD0;
				float4 color : COLOR;
				float3 normal : NORMAL;
				float4 vertexOS : POSITION;
				UNITY_VERTEX_INPUT_INSTANCE_ID
			};

			struct Varyings
			{
				float2 uv0 : TEXCOORD0;
				float noise : TEXCOORD1;
				float4 color : COLOR;
				float3 normal : NORMAL;
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
				output.color = input.color;
				output.normal = normalize(mul((float3x3)UNITY_MATRIX_M, input.normal));
				output.vertexWS = mul(UNITY_MATRIX_M, input.vertexOS);

				float3 objectPos = float3(UNITY_MATRIX_M[0].w, UNITY_MATRIX_M[1].w, UNITY_MATRIX_M[2].w);

				FWindInput windInput;
                windInput.fade = 1;
                windInput.flutter = 1;
                windInput.phaseOffset = 0;
                windInput.speed = GetWindSpeed();
                windInput.objectPivot = objectPos;
                windInput.normalWS = output.normal;
                windInput.direction = GetWindDirection();
                windInput.mask = input.uv0.y * saturate(input.vertexOS.y / _PivotOffset) * GetWindVariation(objectPos);
				Wind(windInput, output.vertexWS.xyz, output.normal);

				output.noise = PerlinNoise(objectPos.xz, _ColorVariation);
				output.vertexCS = mul(UNITY_MATRIX_VP, output.vertexWS);
				output.normal = lerp(float3(0, 1, 0), output.normal, _VertexNormalStrength);
				return output;
			}

			float4 frag(Varyings input) : SV_Target
			{
				UNITY_SETUP_INSTANCE_ID(input);

				float alpha = _AlbedoTexture.Sample(sampler_AlbedoTexture, input.uv0).a;
				
				if (alpha <= 0.5f) { discard; }
				return 0;
			}
            ENDHLSL
        }
    }
}
