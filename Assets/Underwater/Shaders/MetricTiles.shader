Shader "Underwater/MetricTiles"
{
 Properties { _BaseColor("Ceramic",Color)=(0.76,0.84,0.83,1) }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" }
  Pass
  {
   Tags { "LightMode"="UniversalForward" }
   HLSLPROGRAM
   #pragma vertex Vert
   #pragma fragment Frag
   #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
   #pragma multi_compile_fragment _ _SHADOWS_SOFT
   #pragma multi_compile_instancing
   #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor;
   CBUFFER_END
   struct A { float4 positionOS:POSITION; float3 normalOS:NORMAL; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float3 normal:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
   V Vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o); o.world=TransformObjectToWorld(i.positionOS.xyz); o.positionCS=TransformWorldToHClip(o.world); o.normal=TransformObjectToWorldNormal(i.normalOS); return o; }
   half4 Frag(V i):SV_Target
   {
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
    float3 n=normalize(i.normal);
    float2 uv=abs(n.y)>0.5?i.world.xz:(abs(n.x)>0.5?i.world.zy:i.world.xy);
    float2 cell=uv*4; // 25 cm ceramic tiles, 3 mm grout.
    float2 f=abs(frac(cell)-0.5);
    float edge=max(f.x,f.y); float aa=max(fwidth(cell.x),fwidth(cell.y));
    float grout=smoothstep(0.488-aa,0.488+aa,edge);
    float variation=frac(sin(dot(floor(cell),float2(127.1,311.7)))*43758.5453);
    half3 albedo=lerp(_BaseColor.rgb*(0.94+variation*0.09),half3(0.32,0.41,0.41),grout);
    Light light=GetMainLight(TransformWorldToShadowCoord(i.world));
    half diffuse=saturate(dot(n,light.direction));
    half3 ambient=SampleSH(n);
    return half4(albedo*(ambient+light.color*diffuse*light.shadowAttenuation),1);
   }
   ENDHLSL
  }
  UsePass "Universal Render Pipeline/Lit/DepthOnly"
  UsePass "Universal Render Pipeline/Lit/ShadowCaster"
 }
}
