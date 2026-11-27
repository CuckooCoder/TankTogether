Shader "SkyMuseum/Sky"
{
    Properties
    {
        _ZenithColor ("清新天顶蓝", Color) = (0.055, 0.43, 0.88, 1)
        _HorizonColor ("浅蓝地平线", Color) = (0.46, 0.79, 0.98, 1)
        _LowerColor ("下半球蓝色", Color) = (0.38, 0.68, 0.84, 1)
        _GradientPower ("天空渐变", Range(0.2, 2)) = 0.5
        _HorizonWidth ("地平线柔化宽度", Range(0.01, 0.5)) = 0.12
        _SunDirection ("朝向太阳", Vector) = (0.4, 0.55, 0.65, 0)
        [HDR] _SunColor ("暖色阳光", Color) = (1, 0.975, 0.9, 1)
        _SunSize ("太阳角半径", Range(0.1, 5)) = 0.6
        _SunSoftness ("太阳边缘柔度", Range(0.05, 3)) = 0.42
        _SunGlow ("柔和光晕", Range(0, 1)) = 0.035
        _Exposure ("曝光", Range(0.1, 3)) = 1.02
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Pass
        {
            Name "SkyBackground"
            Cull Off
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _ZenithColor;
                float4 _HorizonColor;
                float4 _LowerColor;
                float4 _SunDirection;
                float4 _SunColor;
                float _GradientPower;
                float _HorizonWidth;
                float _SunSize;
                float _SunSoftness;
                float _SunGlow;
                float _Exposure;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 directionWS : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.directionWS = TransformObjectToWorldDir(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float3 direction = normalize(input.directionWS);
                float height = direction.y;
                half upperBlend = pow(saturate(height), max(_GradientPower, 0.01));
                half3 upperSky = lerp(_HorizonColor.rgb, _ZenithColor.rgb, upperBlend);
                half lowerBlend = smoothstep(0.0, 0.65, -height);
                half3 lowerSky = lerp(_HorizonColor.rgb, _LowerColor.rgb, lowerBlend);
                half3 color = height >= 0.0 ? upperSky : lowerSky;
                half haze = exp(-abs(height) / max(_HorizonWidth, 0.001));
                color = lerp(color, _HorizonColor.rgb, haze * 0.18);

                float3 sunDirection = _SunDirection.xyz;
                sunDirection = dot(sunDirection, sunDirection) > 0.0001 ? normalize(sunDirection) : float3(0.0, 1.0, 0.0);
                float alignment = dot(direction, sunDirection);
                float innerEdge = cos(radians(_SunSize));
                float outerEdge = cos(radians(_SunSize + max(_SunSoftness, 0.05)));
                half disc = smoothstep(outerEdge, innerEdge, alignment);
                half halo = pow(saturate(alignment), 48.0) * _SunGlow;
                color = lerp(color, _SunColor.rgb, disc * 0.85);
                color += _SunColor.rgb * halo;

                // v4 天空只保留干净渐变；全部云轮廓交给独立连续网格，不再采样噪声云。
                return half4(max(color * _Exposure, 0.0), 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
