Shader "Underwater/Fine Wet Sand"
{
    Properties
    {
        _BaseMap("Grain Color",2D)="white"{}
        _BumpMap("Grain Normal",2D)="bump"{}
        _BaseColor("Tint",Color)=(1,1,1,1)
        _BumpScale("Grain Relief",Range(0,2))=.65
        _Smoothness("Wet Roughness",Range(0,1))=.13
        _GrainTileMetres("World Tile Size",Float)=.12
    }
    SubShader
    {
        Tags {"RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry"}
        Pass
        {
            Name "ForwardLit"
            Tags {"LightMode"="UniversalForward"}
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
            TEXTURE2D(_BaseMap);SAMPLER(sampler_BaseMap);
            TEXTURE2D(_BumpMap);SAMPLER(sampler_BumpMap);
            CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST,_BaseColor;
            float _BumpScale,_Smoothness,_GrainTileMetres;
            CBUFFER_END
            struct Attributes{float4 positionOS:POSITION;float3 normalOS:NORMAL;UNITY_VERTEX_INPUT_INSTANCE_ID};
            struct Varyings
            {
                float4 positionCS:SV_POSITION;float3 positionWS:TEXCOORD0;half3 normalWS:TEXCOORD1;
                half fog:TEXCOORD2;UNITY_VERTEX_INPUT_INSTANCE_ID UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings o=(Varyings)0;UNITY_SETUP_INSTANCE_ID(input);UNITY_TRANSFER_INSTANCE_ID(input,o);UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                VertexPositionInputs p=GetVertexPositionInputs(input.positionOS.xyz);o.positionCS=p.positionCS;o.positionWS=p.positionWS;
                o.normalWS=TransformObjectToWorldNormal(input.normalOS);o.fog=ComputeFogFactor(o.positionCS.z);return o;
            }
            half4 Frag(Varyings i):SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
                half3 n=normalize(i.normalWS),w=pow(abs(n),4);w/=max(dot(w,half3(1,1,1)),.0001);
                float3 p=i.positionWS/max(.01,_GrainTileMetres);
                half3 albedo=SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p.zy).rgb*w.x+
                    SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p.xz).rgb*w.y+SAMPLE_TEXTURE2D(_BaseMap,sampler_BaseMap,p.xy).rgb*w.z;
                half3 nx=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,p.zy),_BumpScale);
                half3 ny=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,p.xz),_BumpScale);
                half3 nz=UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap,sampler_BumpMap,p.xy),_BumpScale);
                // World projection on all three planes keeps grains round on
                // cavity walls, instead of stretching an XZ texture vertically.
                n=normalize(n+w.x*half3(0,nx.y,nx.x)+w.y*half3(ny.x,0,ny.y)+w.z*half3(nz.x,nz.y,0));
                InputData input=(InputData)0;input.positionWS=i.positionWS;input.normalWS=n;
                input.viewDirectionWS=GetWorldSpaceNormalizeViewDir(i.positionWS);input.shadowCoord=TransformWorldToShadowCoord(i.positionWS);
                input.fogCoord=i.fog;input.vertexLighting=VertexLighting(i.positionWS,n);input.bakedGI=SampleSH(n);
                input.normalizedScreenSpaceUV=GetNormalizedScreenSpaceUV(i.positionCS);input.shadowMask=half4(1,1,1,1);
                SurfaceData surface=(SurfaceData)0;surface.albedo=albedo*_BaseColor.rgb;surface.alpha=1;
                surface.smoothness=_Smoothness;surface.normalTS=half3(0,0,1);surface.occlusion=1;
                half4 color=UniversalFragmentPBR(input,surface);color.rgb=MixFog(color.rgb,i.fog);return color;
            }
            ENDHLSL
        }
        UsePass "Universal Render Pipeline/Lit/ShadowCaster"
        UsePass "Universal Render Pipeline/Lit/DepthOnly"
    }
    FallBack "Universal Render Pipeline/Lit"
}
