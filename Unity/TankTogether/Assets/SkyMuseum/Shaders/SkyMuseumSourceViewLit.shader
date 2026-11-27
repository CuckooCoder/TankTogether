Shader "SkyMuseum/SourceViewLit"
{
 Properties {
  [MainTexture] _BaseMap("Base Color",2D)="white"{}
  [MainColor] _BaseColor("Base Color",Color)=(1,1,1,1)
  _Metallic("Metallic",Range(0,1))=0
  _MetallicGlossMap("Metallic R Smoothness A",2D)="white"{}
  _Smoothness("Smoothness",Range(0,1))=.5
  _SmoothnessTextureChannel("Smoothness Channel",Float)=0
  [Normal] _BumpMap("Normal",2D)="bump"{}
  _BumpScale("Normal Scale",Float)=1
  [HDR] _EmissionColor("Emission",Color)=(0,0,0,0)
  _EmissionMap("Emission",2D)="white"{}
  _ViewEmissionLUT("Source Color Ramp",2D)="black"{}
  _LayerBlend("Source Layer Weight Blend",Range(0,1))=.5
  _ViewEmissionStrength("Source Emission Strength",Float)=1
  _Cutoff("Cutoff",Range(0,1))=.5
  _Surface("Surface",Float)=0
  _Blend("Blend",Float)=0
  _Cull("Cull",Float)=2
  _AlphaClip("Alpha Clip",Float)=0
  _SrcBlend("Source Blend",Float)=1
  _DstBlend("Destination Blend",Float)=0
  _SrcBlendAlpha("Source Alpha Blend",Float)=1
  _DstBlendAlpha("Destination Alpha Blend",Float)=0
  _ZWrite("ZWrite",Float)=1
  _AlphaToMask("AlphaToMask",Float)=0
  _ReceiveShadows("Receive Shadows",Float)=1
  _WorkflowMode("Workflow",Float)=1
  _SpecColor("Specular",Color)=(.2,.2,.2,1)
  _SpecGlossMap("Specular Map",2D)="white"{}
  _SpecularHighlights("Highlights",Float)=1
  _EnvironmentReflections("Reflections",Float)=1
  _OcclusionMap("Occlusion",2D)="white"{}
  _OcclusionStrength("Occlusion Strength",Float)=1
  _Parallax("Parallax",Float)=.005
  _ParallaxMap("Parallax Map",2D)="black"{}
  _DetailMask("Detail Mask",2D)="white"{}
  _DetailAlbedoMap("Detail",2D)="linearGrey"{}
  _DetailAlbedoMapScale("Detail Scale",Float)=1
  _DetailNormalMap("Detail Normal",2D)="bump"{}
  _DetailNormalMapScale("Detail Normal Scale",Float)=1
  _ClearCoatMask("Coat",Float)=0
  _ClearCoatSmoothness("Coat Smoothness",Float)=0
 }
 SubShader {
  Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "UniversalMaterialType"="Lit"}
  Pass {
   Name "ForwardLit"
   Tags {"LightMode"="UniversalForwardOnly"}
   Blend [_SrcBlend] [_DstBlend], [_SrcBlendAlpha] [_DstBlendAlpha]
   ZWrite [_ZWrite]
   Cull [_Cull]
   HLSLPROGRAM
   #pragma target 3.5
   #pragma vertex LitPassVertex
   #pragma fragment SourceFragment
   #pragma shader_feature_local _NORMALMAP
   #pragma shader_feature_local_fragment _SURFACE_TYPE_TRANSPARENT
   #pragma shader_feature_local_fragment _ALPHATEST_ON
   #pragma shader_feature_local_fragment _EMISSION
   #pragma shader_feature_local_fragment _METALLICSPECGLOSSMAP
   #pragma shader_feature_local_fragment _CLEARCOAT
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
   #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
   #pragma multi_compile _ _FORWARD_PLUS
   #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
   #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
   #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
   #pragma multi_compile_fog
   #pragma multi_compile_instancing
   #include "Packages/com.unity.render-pipelines.universal/Shaders/LitInput.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/Shaders/LitForwardPass.hlsl"
   TEXTURE2D(_ViewEmissionLUT); SAMPLER(sampler_ViewEmissionLUT);
   float _LayerBlend, _ViewEmissionStrength;
   half4 SourceFragment(Varyings input) : SV_Target {
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
    SurfaceData surface;
    InitializeStandardLitSurfaceData(input.uv, surface);
    InputData data;
    InitializeInputData(input, surface.normalTS, data);
    InitializeBakedGIData(input, data);
    float ndv = abs(dot(normalize(data.normalWS), normalize(data.viewDirectionWS)));
    float blend = clamp(_LayerBlend,0,0.99999);
    float exponent = blend < .5 ? 2*blend : .5/(1-blend);
    float facing = 1 - pow(max(ndv,0.000001),exponent);
    surface.emission = SAMPLE_TEXTURE2D(_ViewEmissionLUT,sampler_ViewEmissionLUT,float2(saturate(facing),.5)).rgb * _ViewEmissionStrength;
    half4 color = UniversalFragmentPBR(data,surface);
    color.rgb = MixFog(color.rgb,data.fogCoord);
    color.a = OutputAlpha(color.a,IsSurfaceTypeTransparent(_Surface));
    return color;
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
  UsePass "Universal Render Pipeline/Lit/DepthNormals"
 }
 Fallback Off
}
