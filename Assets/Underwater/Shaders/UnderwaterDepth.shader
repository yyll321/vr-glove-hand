Shader "Underwater/DepthFog"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Name "Underwater Fog and Absorption"
            ZWrite Off ZTest Always Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma target 3.5
            #pragma fragment Frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "WaterVolume.hlsl"
            #include "WaterFlow.hlsl"
            float _UWTime;
            float _UWSeaCausticStrength;
            TEXTURE2D(_UWCausticMap); SAMPLER(sampler_UWCausticMap);
            float _UWCausticsEnabled, _UWCausticStrength, _UWTileSize;
            float _UWCausticFootprint, _UWCausticVariation, _UWCausticDebug;
            float3 _UWLightTravel;
            float _UWPresence;
            half4 _UWCausticColor;
            float4x4 _UWInverseViewProjection[2];
            float4 _UWEyePositions[2];
            half SurfaceCaustic(float3 world, float3 refracted)
            {
                // Receiver coordinates depend only on world position and light direction.
                // Keep unwrapped UVs so implicit mip derivatives remain valid at tile edges.
                float2 projected=world.xz-world.y*refracted.xz/min(refracted.y,-0.001);
                float2 surface=world.xz+(_WaterHeight-world.y)*refracted.xz/min(refracted.y,-0.001);
                float firstPhase,secondPhase,blend;UWFlowPhases(_UWTime,firstPhase,secondPhase,blend);
                float2 flow=UWLocalFlow(surface);
                float footprint=max(0.25,_UWCausticFootprint);
                float2 warp=0.14*float2(sin(dot(projected,float2(0.37,0.21))),sin(dot(projected,float2(-0.19,0.43))));
                float2 uv=(projected+warp)/(max(1,_UWTileSize)*footprint);
                float2 drift=flow/(max(1,_UWTileSize)*footprint);
                half first=lerp(SAMPLE_TEXTURE2D(_UWCausticMap,sampler_UWCausticMap,uv-drift*firstPhase).r,SAMPLE_TEXTURE2D(_UWCausticMap,sampler_UWCausticMap,uv-drift*secondPhase).r,blend);
                float2 rotated=float2(0.8192*uv.x-0.5736*uv.y,0.5736*uv.x+0.8192*uv.y)*1.413+float2(0.173,0.619);
                float2 rotatedDrift=float2(0.8192*drift.x-0.5736*drift.y,0.5736*drift.x+0.8192*drift.y)*1.413;
                half second=lerp(SAMPLE_TEXTURE2D(_UWCausticMap,sampler_UWCausticMap,rotated-rotatedDrift*firstPhase).r,SAMPLE_TEXTURE2D(_UWCausticMap,sampler_UWCausticMap,rotated-rotatedDrift*secondPhase).r,blend);
                return lerp(first,second,_UWCausticVariation);
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                half4 original = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                // Blit UVs may include RTHandle scale/bias. Geometry reconstruction must
                // use the current eye's normalized raster coordinates, not the copy UVs.
                float2 screenUv = GetNormalizedScreenSpaceUV(input.positionCS.xy);
                float raw = SampleSceneDepth(screenUv);
                #if !UNITY_REVERSED_Z
                    raw = lerp(UNITY_NEAR_CLIP_VALUE, 1, raw);
                #endif
                float3 world = ComputeWorldSpacePosition(screenUv, raw, _UWInverseViewProjection[unity_StereoEyeIndex]);
                float3 eye = _UWEyePositions[unity_StereoEyeIndex].xyz;
                float3 delta = world - eye;
                float total = length(delta);
                float3 dir = delta / max(total,0.0001);
                float2 seaPath=UWVolumeInterval(eye,dir,total,1);
                float hazeColumn=UWSeaHazeColumn(eye,dir,seaPath);
                float blurAmount=(1-exp(-hazeColumn*0.8))*smoothstep(1.2,4,total);
                if(blurAmount>0.015 && _UWCausticDebug<0.5)
                {
                    // Same-eye, depth-aware softening; foreground edges and the HUD
                    // remain sharp. Height and path length, never head rotation, drive it.
                    half3 softened=original.rgb*2;float weights=2;
                    float centreDepth=LinearEyeDepth(raw,_ZBufferParams);
                    float2 offsets[4]={float2(1,0),float2(-1,0),float2(0,1),float2(0,-1)};
                    [unroll] for(int k=0;k<4;k++)
                    {
                        float2 offset=offsets[k]*(_UWSeaHaze.z*blurAmount)/_ScaledScreenParams.xy;
                        float neighborDepth=LinearEyeDepth(SampleSceneDepth(saturate(screenUv+offset)),_ZBufferParams);
                        float weight=exp(-abs(neighborDepth-centreDepth)/max(0.2,centreDepth*0.06));
                        softened+=SAMPLE_TEXTURE2D_X(_BlitTexture,sampler_LinearClamp,uv+offset*_BlitScaleBias.xy).rgb*weight;weights+=weight;
                    }
                    original.rgb=lerp(original.rgb,softened/weights,blurAmount);
                }
                float3 refracted=refract(normalize(_UWLightTravel),float3(0,1,0),1.0/1.333);
                if(_UWCausticDebug>0.5)
                    return half4((0.1+0.2*min(SurfaceCaustic(world,refracted),3)).xxx,1);
                if(_UWCausticsEnabled>0.5 && world.y>=-0.3 && world.y<_WaterHeight-0.05)
                {
                    float inPool=UWRegionContains(world.xz,0);
                    float inSea=UWRegionContains(world.xz,1);
                    half caustic=SurfaceCaustic(world,refracted);
                    float3 normal=normalize(cross(ddy(world),ddx(world)));
                    float facing=saturate(abs(dot(normal,-refracted)));
                    float shadow=GetMainLight(TransformWorldToShadowCoord(world)).shadowAttenuation;
                    float depthFade=exp(-0.028*max(0,_WaterHeight-world.y));
                    float silt=UWSiltColumn(world,-refracted,0,max(0,(_WaterHeight-world.y)/max(0.01,-refracted.y)));
                    original.rgb*=1+(clamp(caustic,0.4,2.2)-1)*_UWCausticStrength*_UWCausticColor.rgb*facing*shadow*depthFade*max(inPool,inSea*_UWSeaCausticStrength)*exp(-silt*1.4);
                }
                half3 result=UWIntegrateWater(original.rgb,eye,world,-refracted);
                return half4(result,original.a);
            }
            ENDHLSL
        }
    }
}
