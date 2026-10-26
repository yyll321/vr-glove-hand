#ifndef UW_WATER_FLOW_INCLUDED
#define UW_WATER_FLOW_INCLUDED
TEXTURE2D(_UWFlowMap); SAMPLER(sampler_UWFlowMap);
float _UWFlowReady;
float2 UWLocalFlow(float2 world)
{
    float region=step(31,world.x);
    float2 uv=saturate(float2((world.x-region*62+12.5)/25,(world.y+25)/50));
    // Each water body owns 32 columns. Clamp to texel centres to avoid leakage.
    uv=float2((region*32+0.5+uv.x*31)/64,(0.5+uv.y*31)/32);
    return SAMPLE_TEXTURE2D_LOD(_UWFlowMap,sampler_UWFlowMap,uv,0).rg*_UWFlowReady;
}
// Two offset phases fade through each reset. Texture detail is advected without
// stretching forever or snapping at the end of a flow cycle.
void UWFlowPhases(float time,out float first,out float second,out float weight)
{
    first=frac(time/8);second=frac(time/8+0.5);weight=abs(first*2-1);
    first*=8;second*=8;
}
#endif
