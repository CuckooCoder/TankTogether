Shader "TankTogether/HeadSupportDebug"
{
 Properties { _Color("Color", Color) = (0,1,1,0.2) }
 SubShader {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Overlay" "RenderType"="Transparent" }
  Pass {
   Tags { "LightMode"="SRPDefaultUnlit" }
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off
   ZTest Always
   Cull Off
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _Color;
   CBUFFER_END
   struct Attributes {float4 positionOS:POSITION;};
   struct Varyings {float4 positionCS:SV_POSITION;};
   Varyings Vert(Attributes v) {Varyings o;o.positionCS=TransformObjectToHClip(v.positionOS.xyz);return o;}
   half4 Frag(Varyings v):SV_Target {return _Color;}
   ENDHLSL
  }
 }
}
