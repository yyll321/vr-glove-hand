Shader "Underwater/Particles"
{
    Properties { _BaseColor("Color", Color) = (0.6,0.9,1,0.25) }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "WaterVolume.hlsl"
            struct Attributes { float4 positionOS:POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Varyings { float4 positionCS:SV_POSITION; float2 uv:TEXCOORD0; half4 color:COLOR; float3 world:TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END
            Varyings Vert(Attributes i)
            {
                Varyings o; UNITY_SETUP_INSTANCE_ID(i); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.world = TransformObjectToWorld(i.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.world); o.uv=i.uv; o.color=i.color*_BaseColor; return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float d=length(i.uv*2-1);
                half alpha=saturate(1-d*d); alpha*=alpha;
                float3 eye=GetCameraPositionWS();float range=distance(i.world,eye);
                float2 screenUv=GetNormalizedScreenSpaceUV(i.positionCS);
                float sceneDepth=LinearEyeDepth(SampleSceneDepth(screenUv),_ZBufferParams);
                float particleDepth=-TransformWorldToView(i.world).z;
                alpha*=saturate((sceneDepth-particleDepth)*8)*exp(-range*0.055);
                // Particles remain inside the water volume and softly disappear near the eye.
                alpha*=step(i.world.y,10.0)*step(abs(i.world.z),25.0)*max(step(abs(i.world.x),12.5),step(abs(i.world.x-62.0),12.5))*saturate(distance(i.world,GetCameraPositionWS())*1.5-0.25);
                half3 tint=UWIntegrateWater(i.color.rgb*(1+UWLocalLamp(i.world)*0.65),eye,i.world,float3(0,1,0));
                return half4(tint,i.color.a*alpha);
            }
            ENDHLSL
        }
    }
}
