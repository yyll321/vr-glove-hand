Shader "Underwater/GerstnerNormal"
{
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" } Pass {
 ZWrite Off ZTest Always Cull Off
 HLSLPROGRAM
 #pragma vertex Vert
 #pragma fragment Frag
 #pragma target 3.5
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "GerstnerWaves.hlsl"
 struct Varyings { float4 positionCS:SV_POSITION; float2 texcoord:TEXCOORD0; };
 Varyings Vert(uint id:SV_VertexID)
 {
    Varyings o; o.positionCS=GetFullScreenTriangleVertexPosition(id);o.texcoord=GetFullScreenTriangleTexCoord(id);return o;
 }
 half4 Frag(Varyings i):SV_Target
 {
    float3 p,n; UWWave(i.texcoord*_UWTileSize,p,n);
    return half4(n*0.5+0.5,p.y);
 }
 ENDHLSL
 } }
}
