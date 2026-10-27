#ifndef UW_WATER_REGIONS_INCLUDED
#define UW_WATER_REGIONS_INCLUDED
float4 _UWSeaRegion; // centre XZ, half size XZ; zero means legacy scene
float4 UWRegion(int region)
{
    if(region==0)return float4(0,0,12.5,25);
    return _UWSeaRegion.z>0?_UWSeaRegion:float4(62,0,12.5,25);
}
float UWRegionContains(float2 world,int region)
{
    float4 b=UWRegion(region);return step(abs(world.x-b.x),b.z)*step(abs(world.y-b.y),b.w);
}
#endif
