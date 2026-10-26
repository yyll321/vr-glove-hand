Shader "Reconstruction/BakedNeRF"
{
    Properties { _Volume("Baked radiance / density", 3D)=""{} _Steps("Ray steps",Float)=128 }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Cull Front ZWrite Off ZTest Always Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            TEXTURE3D(_Volume); SAMPLER(sampler_Volume);
            float _Steps, _TrainingUnitsPerWorldUnit;
            struct Input { float3 positionOS:POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct Output { float4 positionCS:SV_POSITION; float3 positionWS:TEXCOORD0; UNITY_VERTEX_OUTPUT_STEREO };
            Output Vert(Input v)
            {
                Output o; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionWS=TransformObjectToWorld(v.positionOS);o.positionCS=TransformWorldToHClip(o.positionWS);return o;
            }
            half4 Frag(Output i):SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float3 camera=GetCameraPositionWS(),dir=normalize(i.positionWS-camera);
                float3 ro=TransformWorldToObject(camera),rd=mul((float3x3)unity_WorldToObject,dir);
                float3 inv=sign(rd+1e-12)/max(abs(rd),1e-8);
                float3 t0=(-0.5-ro)*inv,t1=(0.5-ro)*inv;
                float3 lo=min(t0,t1),hi=max(t0,t1);
                float entry=max(0,max(lo.x,max(lo.y,lo.z))),exit=min(hi.x,min(hi.y,hi.z));
                float2 uv=i.positionCS.xy/_ScaledScreenParams.xy;
                float depth=SampleSceneDepth(uv);
                #if !UNITY_REVERSED_Z
                depth=lerp(UNITY_NEAR_CLIP_VALUE,1,depth);
                #endif
                float3 opaque=ComputeWorldSpacePosition(uv,depth,UNITY_MATRIX_I_VP);
                exit=min(exit,dot(opaque-camera,dir));
                clip(exit-entry);
                int steps=(int)clamp(_Steps,24,384);
                float dt=(exit-entry)/steps;
                // Density was baked in training units. Remove instance scale from integration length.
                float densityStep=dt*_TrainingUnitsPerWorldUnit;
                float4 result=0;
                [loop] for(int n=0;n<steps;n++)
                {
                    float3 p=ro+rd*(entry+(n+0.5)*dt)+0.5;
                    float4 sample=SAMPLE_TEXTURE3D_LOD(_Volume,sampler_Volume,p,0);
                    #if !defined(UNITY_COLORSPACE_GAMMA)
                    sample.rgb=SRGBToLinear(sample.rgb);
                    #endif
                    float alpha=1-exp(-max(0,sample.a)*densityStep);
                    result.rgb+=(1-result.a)*alpha*sample.rgb;
                    result.a+=(1-result.a)*alpha;
                    if(result.a>0.995)break;
                }
                return result;
            }
            ENDHLSL
        }
    }
}
