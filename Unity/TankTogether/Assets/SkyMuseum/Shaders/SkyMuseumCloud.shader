Shader "SkyMuseum/Cloud"
{
    Properties
    {
        [HDR] _BaseColor ("暖白云顶", Color) = (1, 0.995, 0.975, 1)
        _ShadeColor ("淡蓝云底", Color) = (0.59, 0.77, 0.94, 1)
        _Wrap ("漫反射包裹", Range(0, 1)) = 0.72
        _AmbientLift ("暗部提亮", Range(0, 0.5)) = 0.12
        _Brightness ("ACES 云白度补偿", Range(0.8, 2.2)) = 1.55
        _FogInfluence ("远景雾影响", Range(0, 1)) = 0.16
        _HeightRange ("局部高度最小与最大值", Vector) = (-0.72, 1.75, 0, 0)
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10" }
        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseColor;
            float4 _ShadeColor;
            float4 _HeightRange;
            float _Wrap;
            float _AmbientLift;
            float _Brightness;
            float _FogInfluence;
        CBUFFER_END
        ENDHLSL
        Pass
        {
            Name "CloudSoftVolume"
            Tags { "LightMode"="UniversalForwardOnly" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fog
            #pragma multi_compile _ _FORWARD_PLUS
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half localHeight : TEXCOORD2;
                half fogFactor : TEXCOORD3;
                half3 localNormal : TEXCOORD4;
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
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.localNormal = input.normalOS;
                output.localHeight = saturate((input.positionOS.y - _HeightRange.x) / max(_HeightRange.y - _HeightRange.x, .001));
                output.fogFactor = ComputeFogFactor(p.positionCS.z);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS);
                half3 v = GetWorldSpaceNormalizeViewDir(input.positionWS);
                Light sun = GetMainLight();
                half wrapped = saturate((dot(n, sun.direction) + _Wrap) / (1.0 + _Wrap));
                half skyFacing = smoothstep(-.65, .65, n.y);
                half heightLight = smoothstep(.12, .90, input.localHeight);
                half puffLight = saturate(dot(normalize(input.localNormal), normalize(half3(-.50,.75,-.50))) * .5 + .5);
                half lift = saturate(.015 + puffLight * .65 + heightLight * .22 + skyFacing * .06 + _AmbientLift);
                half3 color = lerp(_ShadeColor.rgb, _BaseColor.rgb, lift);
                // 用软包裹光塑形，不采样 AO、不接收硬阴影，凹处不出现黑缝。
                half warmKey = saturate(dot(n, sun.direction)) * .045;
                half3 sunlight = min(sun.color, half3(1.25, 1.25, 1.25));
                color += _BaseColor.rgb * sunlight * warmKey;
                half rim = pow(1.0 - saturate(dot(n, v)), 3.0);
                half silverLining = rim * pow(saturate(dot(-v, sun.direction)), 5.0) * .045;
                color = (color + _BaseColor.rgb * silverLining) * _Brightness;
                // 独立控制云受雾程度，避免沿用全局雾后整朵变成蓝灰色。
                color = lerp(color, MixFog(color, input.fogFactor), _FogInfluence);
                return half4(max(color, 0), 1);
            }
            ENDHLSL
        }
        Pass
        {
            Name "CloudDepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back
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
