Shader "ToonBridge/Toon"
{
    Properties
    {
        [MainTexture] _BaseMap("底色贴图", 2D) = "white" {}
        [MainColor] _BaseColor("底色", Color) = (1,1,1,1)
        [Toggle(_NORMALMAP)] _NormalMapEnabled("使用法线贴图", Float) = 0
        [Normal] _NormalMap("切线空间法线", 2D) = "bump" {}
        _NormalStrength("法线强度", Range(0,4)) = 1
        _EmissionMap("自发光贴图", 2D) = "white" {}
        [HDR] _EmissionColor("自发光颜色", Color) = (0,0,0,1)
        [Toggle(_VERTEX_COLOR)] _UseVertexColor("使用顶点色", Float) = 0
        _Threshold("明暗分界", Range(0,1)) = 0.5
        _Softness("分界柔和度", Range(0.001,0.5)) = 0.03
        _ShadowStrength("阴影强度", Range(0,1)) = 0.65
        [HDR] _RimColor("边缘光颜色", Color) = (1,1,1,1)
        _RimStrength("边缘光强度", Range(0,2)) = 0.15
        _RimPower("边缘光指数", Range(0.1,16)) = 3
        [Toggle(_ALPHATEST_ON)] _AlphaClip("透明度裁剪", Float) = 0
        _Cutoff("裁剪阈值", Range(0,1)) = 0.5
        [HideInInspector] _Roughness("源粗糙度（非 PBR）", Float) = 0.5
        [HideInInspector] _Metallic("源金属度（非 PBR）", Float) = 0
        [HideInInspector] _Surface("表面类型", Float) = 0
        [HideInInspector] _SrcBlend("源混合", Float) = 1
        [HideInInspector] _DstBlend("目标混合", Float) = 0
        [HideInInspector] _ZWrite("深度写入", Float) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull("剔除模式", Float) = 2
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 200

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
        #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"

        // 所有通道使用完全相同的布局，不复用 URP/Lit 的不兼容通道。
        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            half4 _EmissionColor;
            half4 _RimColor;
            float _NormalStrength;
            float _NormalMapEnabled;
            float _UseVertexColor;
            float _Threshold;
            float _Softness;
            float _ShadowStrength;
            float _RimStrength;
            float _RimPower;
            float _AlphaClip;
            float _Cutoff;
            float _Roughness;
            float _Metallic;
            float _Surface;
            float _SrcBlend;
            float _DstBlend;
            float _ZWrite;
            float _Cull;
        CBUFFER_END

        TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
        TEXTURE2D(_NormalMap); SAMPLER(sampler_NormalMap);
        TEXTURE2D(_EmissionMap); SAMPLER(sampler_EmissionMap);

        struct Attributes
        {
            float4 positionOS : POSITION;
            float3 normalOS : NORMAL;
            float4 tangentOS : TANGENT;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float3 positionWS : TEXCOORD0;
            float3 normalWS : TEXCOORD1;
            float4 tangentWS : TEXCOORD2;
            float2 uv : TEXCOORD3;
            half4 color : TEXCOORD4;
            half4 fogAndVertexLight : TEXCOORD5;
            UNITY_VERTEX_INPUT_INSTANCE_ID
            UNITY_VERTEX_OUTPUT_STEREO
        };

        half4 ReadBase(float2 uv, half4 vertexColor)
        {
            half4 value = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor;
            #if defined(_VERTEX_COLOR)
                value *= vertexColor;
            #endif
            #if defined(_ALPHATEST_ON)
                clip(value.a - _Cutoff);
            #endif
            return value;
        }

        half3 ReadNormal(Varyings input)
        {
            half3 n = NormalizeNormalPerPixel(input.normalWS);
            #if defined(_NORMALMAP)
                half3 t = SafeNormalize(input.tangentWS.xyz);
                half3 b = cross(n, t) * input.tangentWS.w;
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, input.uv), _NormalStrength);
                return NormalizeNormalPerPixel(TransformTangentToWorld(normalTS, half3x3(t, b, n)));
            #else
                return n;
            #endif
        }

        half ToonBand(half ndotl)
        {
            half softness = max(_Softness, 0.001);
            return smoothstep(_Threshold - softness, _Threshold + softness, saturate(ndotl));
        }

        Varyings SurfaceVertex(Attributes input)
        {
            Varyings output = (Varyings)0;
            UNITY_SETUP_INSTANCE_ID(input);
            UNITY_TRANSFER_INSTANCE_ID(input, output);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            VertexPositionInputs pos = GetVertexPositionInputs(input.positionOS.xyz);
            VertexNormalInputs basis = GetVertexNormalInputs(input.normalOS, input.tangentOS);
            output.positionCS = pos.positionCS;
            output.positionWS = pos.positionWS;
            output.normalWS = basis.normalWS;
            output.tangentWS = float4(basis.tangentWS, input.tangentOS.w * GetOddNegativeScale());
            output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
            output.color = input.color;
            output.fogAndVertexLight.x = ComputeFogFactor(pos.positionCS.z);
            #if defined(_ADDITIONAL_LIGHTS_VERTEX)
                output.fogAndVertexLight.yzw = VertexLighting(pos.positionWS, basis.normalWS);
            #endif
            return output;
        }

        half3 AdditionalToonLight(Light light, half3 normalWS)
        {
            // 局部光源必须随距离衰减，不能把暗面底亮度叠加到整个场景。
            return light.color * ToonBand(dot(normalWS, light.direction))
                * light.distanceAttenuation * light.shadowAttenuation;
        }
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForwardOnly" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite [_ZWrite]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SurfaceVertex
            #pragma fragment ForwardFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma shader_feature_local _VERTEX_COLOR
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
            #pragma shader_feature_local_fragment _NORMALMAP
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fragment _ _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile _ _FORWARD_PLUS

            half4 ForwardFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half4 base = ReadBase(input.uv, input.color);
                half3 normalWS = ReadNormal(input);
                half3 viewWS = GetWorldSpaceNormalizeViewDir(input.positionWS);
                float4 shadowCoord;
                #if defined(_MAIN_LIGHT_SHADOWS_SCREEN) && !defined(_SURFACE_TYPE_TRANSPARENT)
                    shadowCoord = ComputeScreenPos(TransformWorldToHClip(input.positionWS));
                #else
                    shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                #endif
                Light mainLight = GetMainLight(shadowCoord, input.positionWS, half4(1,1,1,1));
                half mainBand = ToonBand(dot(normalWS, mainLight.direction));
                half mainShade = lerp(1.0h, mainBand * mainLight.shadowAttenuation, _ShadowStrength);
                half3 illumination = SampleSH(normalWS)
                    + mainLight.color * mainShade * mainLight.distanceAttenuation;

                #if defined(_ADDITIONAL_LIGHTS)
                    // URP14 Forward+ 的 LIGHT_LOOP_BEGIN 需要这两个 InputData 字段。
                    InputData inputData = (InputData)0;
                    inputData.positionWS = input.positionWS;
                    inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                    #if USE_FORWARD_PLUS
                        UNITY_LOOP for (uint lightIndex = 0; lightIndex < min(URP_FP_DIRECTIONAL_LIGHTS_COUNT, MAX_VISIBLE_LIGHTS); ++lightIndex)
                        {
                            FORWARD_PLUS_SUBTRACTIVE_LIGHT_CHECK
                            Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                            illumination += AdditionalToonLight(light, normalWS);
                        }
                    #endif
                    uint lightCount = GetAdditionalLightsCount();
                    LIGHT_LOOP_BEGIN(lightCount)
                        Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1,1,1,1));
                        illumination += AdditionalToonLight(light, normalWS);
                    LIGHT_LOOP_END
                #elif defined(_ADDITIONAL_LIGHTS_VERTEX)
                    illumination += input.fogAndVertexLight.yzw;
                #endif

                half rim = pow(saturate(1.0h - dot(normalWS, viewWS)), max(_RimPower, 0.1));
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb;
                half3 color = base.rgb * illumination + emission + _RimColor.rgb * (rim * _RimStrength);
                color = MixFog(color, input.fogAndVertexLight.x);
                // OPAQUE/CLIP 不将源 alpha 泄漏到相机目标；BLEND 使用直通 alpha。
                return half4(color, _Surface > 0.5 ? base.a : 1.0h);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowVertex
            #pragma fragment ShadowFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #pragma shader_feature_local _VERTEX_COLOR
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            float3 _LightDirection;
            float3 _LightPosition;

            Varyings ShadowVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if defined(_CASTING_PUNCTUAL_LIGHT_SHADOW)
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif
                output.positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    output.positionCS.z = min(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #else
                    output.positionCS.z = max(output.positionCS.z, output.positionCS.w * UNITY_NEAR_CLIP_VALUE);
                #endif
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap);
                output.color = input.color;
                return output;
            }

            half4 ShadowFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ReadBase(input.uv, input.color);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SurfaceVertex
            #pragma fragment DepthFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local _VERTEX_COLOR
            #pragma shader_feature_local_fragment _ALPHATEST_ON

            half4 DepthFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                ReadBase(input.uv, input.color);
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormalsOnly"
            Tags { "LightMode"="DepthNormalsOnly" }
            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SurfaceVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local _VERTEX_COLOR
            #pragma shader_feature_local_fragment _ALPHATEST_ON
            #pragma shader_feature_local_fragment _NORMALMAP
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            half4 DepthNormalsFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                ReadBase(input.uv, input.color);
                half3 normalWS = ReadNormal(input);
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormal = PackNormalOctQuadEncode(normalWS);
                    half3 packedNormal = PackFloat2To888(saturate(octNormal * 0.5 + 0.5));
                    return half4(packedNormal, 0);
                #else
                    return half4(normalWS, 0);
                #endif
            }
            ENDHLSL
        }
    }
    Fallback Off
}
