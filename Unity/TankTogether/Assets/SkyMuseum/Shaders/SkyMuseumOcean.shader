Shader "SkyMuseum/Ocean"
{
    Properties
    {
        _NearColor ("近处清澈蓝青", Color) = (0.095, 0.64, 0.73, 1)
        _FarColor ("远处柔海蓝", Color) = (0.13, 0.48, 0.67, 1)
        _SkyZenith ("反射天顶色", Color) = (0.22, 0.59, 0.89, 1)
        _SkyHorizon ("反射地平线色", Color) = (0.66, 0.84, 0.96, 1)
        _HorizonWater ("薄雾中的远海", Color) = (0.35, 0.65, 0.81, 1)
        _RippleScale ("世界空间大浪频率", Range(0.25, 6)) = 3
        _RippleStrength ("大浪法线强度", Range(0, 0.3)) = 0.12
        _WaveSpeed ("缓慢波速", Range(0, 2)) = 0.26
        _WaveContrast ("大浪色带层次", Range(0, 0.5)) = 0.22
        _FoamColor ("稀疏柔白浪尖", Color) = (0.80, 0.96, 0.98, 1)
        _FoamStrength ("白浪覆盖强度", Range(0, 1)) = 0.34
        _GlintStrength ("柔和日光反射", Range(0, 2)) = 0.32
        _SceneUnitScale ("场景世界单位倍率", Float) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _NearColor;
            float4 _FarColor;
            float4 _SkyZenith;
            float4 _SkyHorizon;
            float4 _HorizonWater;
            float4 _FoamColor;
            float _RippleScale;
            float _RippleStrength;
            float _WaveSpeed;
            float _WaveContrast;
            float _FoamStrength;
            float _GlintStrength;
            float _SceneUnitScale;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "OceanForward"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Off
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                VertexPositionInputs p = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = p.positionCS;
                output.positionWS = p.positionWS;
                return output;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }
            float Noise(float2 p)
            {
                float2 cell = floor(p), f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                return lerp(lerp(Hash21(cell), Hash21(cell + float2(1, 0)), f.x),
                    lerp(Hash21(cell + float2(0, 1)), Hash21(cell + float2(1, 1)), f.x), f.y);
            }
            float WaveVisibility(float phase)
            {
                return 1.0 - smoothstep(0.55, 2.5, fwidth(phase));
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float t = _Time.y * _WaveSpeed;
                float2 p = input.positionWS.xz * _RippleScale;
                float2 along = float2(0.94, 0.342);
                float2 across = float2(-0.342, 0.94);
                float u = dot(p, along), w = dot(p, across);
                float warp = (Noise(p * 0.075 + float2(t * 0.035, -t * 0.024)) - 0.5) * 3.4;

                // 主波长约 8 / 14 / 4 米；方向相近并低频扭曲，避免交叉网格。
                float phaseA = u * 0.80 + sin(w * 0.24 + t * 0.21) * 1.15 + warp - t;
                float phaseB = u * 0.43 + w * 0.13 + warp * 0.64 - t * 0.68 + 2.1;
                float phaseC = u * 1.52 - w * 0.17 + warp * 1.22 - t * 1.38 + 4.7;
                float3 visibility = float3(WaveVisibility(phaseA), WaveVisibility(phaseB), WaveVisibility(phaseC));
                float3 waves = sin(float3(phaseA, phaseB, phaseC)) * visibility;
                float swell = dot(waves, float3(0.56, 0.29, 0.15));
                float2 slope = along * cos(phaseA) * 0.80 * visibility.x;
                slope += (along * 0.43 + across * 0.13) * cos(phaseB) * 0.48 * visibility.y;
                slope += (along * 1.52 - across * 0.17) * cos(phaseC) * 0.18 * visibility.z;
                slope += across * cos(w * 0.24 + t * 0.21) * cos(phaseA) * 0.276 * visibility.x;
                half3 n = normalize(float3(-slope.x * _RippleStrength, 1, -slope.y * _RippleStrength));
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                n *= dot(n, v) < 0 ? -1.0 : 1.0;
                float viewDistance = distance(input.positionWS.xz, _WorldSpaceCameraPos.xz) / max(_SceneUnitScale, 0.000001);
                half distanceBlend = smoothstep(6.0, 58.0, viewDistance);
                half broadVariation = Noise(p * 0.055 + float2(t * 0.012, t * 0.009));
                half3 water = lerp(_NearColor.rgb, _FarColor.rgb, saturate(distanceBlend + (broadVariation - 0.5) * 0.18));
                half bandVisibility = 1.0 - smoothstep(24.0, 100.0, viewDistance) * 0.80;
                water *= 1.0 + swell * _WaveContrast * bandVisibility;
                half waveFace = smoothstep(0.12, 0.85, waves.x * 0.74 + waves.y * 0.26);
                water = lerp(water, _FoamColor.rgb, waveFace * _WaveContrast * 0.55 * bandVisibility);

                half ndv = saturate(dot(n, v));
                half fresnel = 0.025 + 0.975 * pow(1.0 - ndv, 5.0);
                half3 reflected = reflect(-v, n);
                half3 sky = lerp(_SkyHorizon.rgb, _SkyZenith.rgb, pow(saturate(reflected.y), 0.70));
                half3 color = lerp(water, sky, 0.10 + fresnel * 0.48);

                // 白浪沿大浪顶呈断续弧带，远处按像素足迹和距离同时淡出。
                float crestWidth = max(fwidth(waves.x) * 1.25, 0.025);
                half crest = smoothstep(0.93 - crestWidth, 0.98 + crestWidth, waves.x);
                float patches = Noise(float2(u * 0.16 - t * 0.04, w * 0.37 + warp * 0.15));
                half brokenCrest = smoothstep(0.58, 0.79, patches);
                half foamFade = (1.0 - smoothstep(18.0, 64.0, viewDistance)) * visibility.x;
                half foam = crest * brokenCrest * foamFade * _FoamStrength;
                color = lerp(color, _FoamColor.rgb, foam);

                Light sun = GetMainLight();
                half3 h = SafeNormalize(sun.direction + v);
                float nh = saturate(dot(n, h));
                float exponent = lerp(110.0, 34.0, saturate(fwidth(nh) * 180.0));
                half glint = pow(nh, exponent) * (0.35 + 0.65 * waveFace) * 0.55;
                color += min(sun.color, half3(1.4, 1.4, 1.4)) * glint * _GlintStrength * saturate(sun.distanceAttenuation);

                // 片元雾兼容仅四顶点的大平面；保留淡蓝远海与更亮天空的层次。
                float fog = ComputeFogFactor(TransformWorldToHClip(input.positionWS).z);
                half horizonVeil = 1.0 - smoothstep(0.012, 0.10, abs(v.y));
                half3 oceanHaze = lerp(_HorizonWater.rgb, _SkyHorizon.rgb, horizonVeil * 0.52);
                half seaAir = 1.0 - exp(-max(viewDistance - 12.0, 0.0) * 0.008);
                color = lerp(color, oceanHaze, seaAir * 0.55);
                color = MixFogColor(color, oceanHaze, fog);
                return half4(max(color, 0), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "OceanDepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Off
            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS : SV_POSITION; UNITY_VERTEX_OUTPUT_STEREO };
            Varyings DepthVert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }
            half DepthFrag(Varyings input) : SV_Target { return input.positionCS.z; }
            ENDHLSL
        }
    }
    Fallback Off
}
