Shader "Underwater/AreaCausticMap"
{
 SubShader { Tags { "RenderPipeline"="UniversalPipeline" } Pass {
 Blend One One
 ZWrite Off ZTest Always Cull Off
 HLSLPROGRAM
 #pragma vertex Vertex
 #pragma fragment Fragment
 #pragma target 3.5
 #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
 #include "GerstnerWaves.hlsl"
 float _UWGrid; int _UWGridGuard;
 TEXTURE2D(_UWWaterNormalMap); SAMPLER(sampler_UWWaterNormalMap);
 float2 ProjectNormalMap(float2 x)
 {
    float4 sample=SAMPLE_TEXTURE2D_LOD(_UWWaterNormalMap,sampler_UWWaterNormalMap,frac(x/_UWTileSize),0);
    float3 p,n; UWWave(x,p,n); n=normalize(sample.xyz*2-1); p.y=sample.w;
    float3 r=refract(normalize(_UWLightTravel),n,1.0/1.333);
    float3 f=refract(normalize(_UWLightTravel),float3(0,1,0),1.0/1.333);
    return p.xz+r.xz*((-_UWProjectionDepth-p.y)/min(r.y,-0.001))-f.xz*(-_UWProjectionDepth/min(f.y,-0.001));
 }
 struct V { float4 positionCS:SV_POSITION; float2 source:TEXCOORD0; float2 projected:TEXCOORD1; };
 float Area2(float2 a,float2 b) { return abs(a.x*b.y-a.y*b.x); }
 V Vertex(uint id:SV_VertexID)
 {
    uint extent=(uint)_UWGrid+2*(uint)_UWGridGuard;
    uint cell=id/6, corner=id%6;
    float2 offsets[6]={float2(0,0),float2(0,1),float2(1,0),float2(1,0),float2(0,1),float2(1,1)};
    float2 x=(float2(cell%extent,cell/extent)+offsets[corner]-_UWGridGuard)*(_UWTileSize/_UWGrid);
    V o; float2 p=ProjectNormalMap(x);
    o.source=x; o.projected=p;
    float2 clip=(p/_UWTileSize)*2-1;
    #if UNITY_UV_STARTS_AT_TOP
       clip.y=-clip.y;
    #endif
    o.positionCS=float4(clip,0.5,1); return o;
 }
 half4 Fragment(V i):SV_Target
 {
    // Conserved energy: source triangle area / refracted receiver area. Derivatives
    // supply the ratio directly, avoiding seven wave evaluations per ray vertex.
    float a=Area2(ddx(i.source),ddy(i.source));
    float b=Area2(ddx(i.projected),ddy(i.projected));
    float ratio=min(10,a/max(b,0.0000001));
    return half4(min(ratio,8),0,0,1);
 }
 ENDHLSL
 } }
}
