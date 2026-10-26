Shader "Underwater/Surface"
{
 Properties
 {
  _BaseColor("Water",Color)=(0.035,0.24,0.28,0.3)
  _Refraction("Refraction",Range(0,0.05))=0.014
  _WaveCenter("Region center X",Float)=0
  _SurfaceWaveScale("Local surface wave strength",Range(0,2))=1
  _EnvironmentCube("World environment reflection",Cube)="black"{}
  _ReflectionCenter("Environment capture centre",Vector)=(0,9.6,0,0)
  _ReflectionMin("Environment bounds minimum",Vector)=(-12.5,0,-25,0)
  _ReflectionMax("Environment bounds maximum",Vector)=(12.5,10,25,0)
  _EnvironmentReady("Environment capture available",Float)=0
 }
 SubShader
 {
  Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
  Pass
  {
   Blend SrcAlpha OneMinusSrcAlpha
   ZWrite Off Cull Off
   HLSLPROGRAM
   #pragma vertex Vertex
   #pragma fragment Fragment
   #pragma target 3.5
   #pragma multi_compile_instancing
   #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
   #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
   #include "GerstnerWaves.hlsl"
   #include "WaterVolume.hlsl"
   #include "WaterFlow.hlsl"
   TEXTURE2D(_UWWaterNormalMap); SAMPLER(sampler_UWWaterNormalMap);
   TEXTURE2D_X(_UWFoggedColor); SAMPLER(sampler_UWFoggedColor);
   TEXTURECUBE(_EnvironmentCube); SAMPLER(sampler_EnvironmentCube);
   float _UWCausticsEnabled;
   CBUFFER_START(UnityPerMaterial)
   half4 _BaseColor; float _Refraction, _WaveCenter, _SurfaceWaveScale;
   float4 _ReflectionCenter, _ReflectionMin, _ReflectionMax;
   float _EnvironmentReady;
   CBUFFER_END
   struct A { float4 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
   struct V { float4 positionCS:SV_POSITION; float3 world:TEXCOORD0; float4 screen:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
   V Vertex(A i)
   {
      V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
      o.world=TransformObjectToWorld(i.positionOS.xyz);
      if(_UWCausticsEnabled>0.5)
      {
         float first,second,weight;UWFlowPhases(_UWTime,first,second,weight);
         float2 flow=UWLocalFlow(o.world.xz);
         float2 xa=o.world.xz-flow*first,xb=o.world.xz-flow*second;
         float3 pa,pb,n;UWWave(xa,pa,n);UWWave(xb,pb,n);
         float3 displacement=lerp(pa-float3(xa.x,0,xa.y),pb-float3(xb.x,0,xb.y),weight);
         float edge=saturate((12.5-abs(o.world.x-_WaveCenter))*2)*saturate((25-abs(o.world.z))*2);
         o.world+=displacement*edge*_SurfaceWaveScale;
      }
      o.positionCS=TransformWorldToHClip(o.world); o.screen=ComputeScreenPos(o.positionCS); return o;
   }
   half4 Fragment(V i):SV_Target
   {
      UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
      float3 n=float3(0,1,0);
      if(_UWCausticsEnabled>0.5)
      {
         float first,second,weight;UWFlowPhases(_UWTime,first,second,weight);
         float2 flow=UWLocalFlow(i.world.xz);
         float3 a=SAMPLE_TEXTURE2D(_UWWaterNormalMap,sampler_UWWaterNormalMap,(i.world.xz-flow*first)/_UWTileSize).xyz*2-1;
         float3 b=SAMPLE_TEXTURE2D(_UWWaterNormalMap,sampler_UWWaterNormalMap,(i.world.xz-flow*second)/_UWTileSize).xyz*2-1;
         n=normalize(lerp(a,b,weight));
         // A second octave makes surface detail independent of the coarse mesh.
         float3 da=SAMPLE_TEXTURE2D(_UWWaterNormalMap,sampler_UWWaterNormalMap,(i.world.xz-flow*first)/(_UWTileSize*0.37)+float2(0.23,0.61)).xyz*2-1;
         float3 db=SAMPLE_TEXTURE2D(_UWWaterNormalMap,sampler_UWWaterNormalMap,(i.world.xz-flow*second)/(_UWTileSize*0.37)+float2(0.23,0.61)).xyz*2-1;
         float3 detail=lerp(da,db,weight);
         n=normalize(float3((n.xz+detail.xz*0.18)*_SurfaceWaveScale,1).xzy);
      }
      float3 view=normalize(GetCameraPositionWS()-i.world); bool underwater=view.y<0;
      float3 faceNormal=underwater?-n:n;
      float cosI=saturate(dot(faceNormal,view));
      float eta=underwater?1.333:1/1.333;
      float sin2T=eta*eta*(1-cosI*cosI);
      bool tir=sin2T>=1;
      float cosT=sqrt(max(0,1-sin2T));
      float rs=(eta*cosI-cosT)/max(1e-4,eta*cosI+cosT);
      float rp=(cosI-eta*cosT)/max(1e-4,cosI+eta*cosT);
      float fresnel=tir?1:saturate(0.5*(rs*rs+rp*rp));
      float2 screenUv=i.screen.xy/i.screen.w;
      float2 offset=TransformWorldToViewDir(faceNormal,false).xy*_Refraction*(underwater?0.5:1);
      float2 uv=saturate(screenUv+offset);
      float depth=SampleSceneDepth(uv);
      float sceneDepth=LinearEyeDepth(depth,_ZBufferParams);
      float waterDepth=-TransformWorldToView(i.world).z;
      if(sceneDepth<waterDepth) uv=screenUv;
      half3 through=SAMPLE_TEXTURE2D_X(_UWFoggedColor,sampler_UWFoggedColor,UnityStereoTransformScreenSpaceTex(uv)).rgb;
      float thickness=max(0,sceneDepth-waterDepth);
      through=lerp(through,_BaseColor.rgb,1-exp(-min(thickness,20)*0.012));
      float3 reflected=reflect(-view,faceNormal);
      half3 reflection=GlossyEnvironmentReflection(reflected,i.world,0.06,1,screenUv);
      if(_EnvironmentReady>0.5)
      {
         float3 ray=normalize(reflected);
         float3 safeRay=float3(abs(ray.x)<1e-4?1e-4:ray.x,abs(ray.y)<1e-4?1e-4:ray.y,abs(ray.z)<1e-4?1e-4:ray.z);
         float3 boundary=lerp(_ReflectionMin.xyz,_ReflectionMax.xyz,step(0,safeRay));
         float3 times=(boundary-i.world)/safeRay;
         float travel=max(0,min(times.x,min(times.y,times.z)));
         float3 hit=i.world+ray*travel;
         float3 lookup=hit-_ReflectionCenter.xyz;
         half3 captured=SAMPLE_TEXTURECUBE_LOD(_EnvironmentCube,sampler_EnvironmentCube,lookup,1).rgb;
         if(underwater)
         {
            float3 light=refract(normalize(_UWLightTravel),float3(0,1,0),1.0/1.333);
            // Integrate the reflected path and the path back to the eye once each.
            reflection=UWIntegrateWater(captured,i.world,hit,-light);
            reflection=UWIntegrateWater(reflection,GetCameraPositionWS(),i.world,-light);
         }
         else reflection=captured;
      }
      Light sun=GetMainLight();
      float highlight=pow(saturate(dot(reflect(-sun.direction,faceNormal),view)),380);
      half3 color=lerp(through,reflection,fresnel)+sun.color*highlight*0.7;
      // Opaque texture already contains the depth-integrated fog. Output the composited refraction once.
      return half4(color,1);
   }
   ENDHLSL
  }
 }
}
