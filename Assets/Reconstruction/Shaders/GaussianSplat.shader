Shader "Reconstruction/GaussianSplat"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off ZTest LEqual Cull Off
            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Color.hlsl"
            StructuredBuffer<float4> _Splats, _Harmonics;
            StructuredBuffer<uint2> _Order;
            float4x4 _SplatLocalToWorld, _SplatWorldToLocal;
            float _FootprintScale, _AlphaCutoff;
            int _SHDegree;
            struct Input { uint vertexID : SV_VertexID; uint instanceID : SV_InstanceID; };
            struct Output { float4 positionCS : SV_POSITION; float2 gaussian : TEXCOORD0; float4 color : TEXCOORD1; UNITY_VERTEX_OUTPUT_STEREO };
            float3 SH(uint id, float3 d)
            {
                if (_SHDegree == 0) return 0;
                // Inverse of source Z-up -> Unity Y-up mapping (x,z,y).
                d = d.xzy;
                float x=d.x,y=d.y,z=d.z,xx=x*x,yy=y*y,zz=z*z;
                uint n=id*15;
                float3 c = 0.488602512f*(-y*_Harmonics[n].xyz+z*_Harmonics[n+1].xyz-x*_Harmonics[n+2].xyz);
                if (_SHDegree > 1)
                    c += 1.092548431f*(x*y*_Harmonics[n+3].xyz-y*z*_Harmonics[n+4].xyz-x*z*_Harmonics[n+6].xyz)
                        +0.315391565f*(2*zz-xx-yy)*_Harmonics[n+5].xyz+0.546274215f*(xx-yy)*_Harmonics[n+7].xyz;
                if (_SHDegree > 2)
                    c += -0.590043590f*y*(3*xx-yy)*_Harmonics[n+8].xyz+2.890611443f*x*y*z*_Harmonics[n+9].xyz
                        -0.457045799f*y*(4*zz-xx-yy)*_Harmonics[n+10].xyz+0.373176333f*z*(2*zz-3*xx-3*yy)*_Harmonics[n+11].xyz
                        -0.457045799f*x*(4*zz-xx-yy)*_Harmonics[n+12].xyz+1.445305721f*z*(xx-yy)*_Harmonics[n+13].xyz
                        -0.590043590f*x*(xx-3*yy)*_Harmonics[n+14].xyz;
                return c;
            }
            Output Vert(Input v)
            {
                Output o=(Output)0; UNITY_SETUP_INSTANCE_ID(v); UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                #if UNITY_ANY_INSTANCING_ENABLED
                uint instance=unity_InstanceID;
                #else
                uint instance=v.instanceID;
                #endif
                uint id = _Order[instance].y;
                float4 p=_Splats[id*4], a=_Splats[id*4+1], b=_Splats[id*4+2];
                float3 center=mul(_SplatLocalToWorld,float4(p.xyz,1)).xyz;
                float3 view=TransformWorldToView(center);
                if (view.z >= -_ProjectionParams.y) { o.positionCS=float4(2,2,2,1); return o; }
                float3x3 cov=float3x3(a.x,a.y,a.z,a.y,a.w,b.x,a.z,b.x,b.y);
                float3x3 r=mul((float3x3)UNITY_MATRIX_V,(float3x3)_SplatLocalToWorld);
                cov=mul(mul(r,cov),transpose(r));
                float iz=1/max(-view.z,0.0001);
                float3 jx=float3(UNITY_MATRIX_P._m00*iz,0,UNITY_MATRIX_P._m00*view.x*iz*iz);
                float3 jy=float3(0,UNITY_MATRIX_P._m11*iz,UNITY_MATRIX_P._m11*view.y*iz*iz);
                float c00=dot(jx,mul(cov,jx)),c01=dot(jx,mul(cov,jy)),c11=dot(jy,mul(cov,jy));
                c00+=1.2/(_ScreenParams.x*_ScreenParams.x);c11+=1.2/(_ScreenParams.y*_ScreenParams.y);
                float mid=(c00+c11)*0.5, diff=sqrt(max(0,(c00-c11)*(c00-c11)*0.25+c01*c01));
                float2 axis=abs(c01)>1e-10?normalize(float2(c01,mid+diff-c00)):(c00>=c11?float2(1,0):float2(0,1));
                float2 u=axis*sqrt(max(mid+diff,1e-12)), w=float2(-axis.y,axis.x)*sqrt(max(mid-diff,1e-12));
                const float2 corners[6]={float2(-1,-1),float2(-1,1),float2(1,1),float2(-1,-1),float2(1,1),float2(1,-1)};
                float2 q=corners[v.vertexID]*3;
                o.positionCS=TransformWorldToHClip(center);
                o.positionCS.xy+=(u*q.x+w*q.y)*o.positionCS.w*_FootprintScale;
                o.gaussian=q;
                float3 cameraLocal=mul(_SplatWorldToLocal,float4(GetCameraPositionWS(),1)).xyz;
                float3 rgb=max(0,_Splats[id*4+3].xyz+SH(id,normalize(p.xyz-cameraLocal)));
                #if !defined(UNITY_COLORSPACE_GAMMA)
                rgb=SRGBToLinear(rgb);
                #endif
                o.color=float4(rgb,p.w);
                return o;
            }
            half4 Frag(Output i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                float power=dot(i.gaussian,i.gaussian);
                clip(9-power);
                float alpha=min(0.99,i.color.a*exp(-0.5*power));clip(alpha-_AlphaCutoff);
                return half4(i.color.rgb*alpha,alpha);
            }
            ENDHLSL
        }
    }
}
