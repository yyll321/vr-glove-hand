#ifndef UW_WATER_VOLUME_INCLUDED
#define UW_WATER_VOLUME_INCLUDED
#include "WaterRegions.hlsl"
#include "WaterInteraction.hlsl"
// Coefficients are per metre. Share the same optics between opaque fog,
// transparent suspended matter and reflected underwater paths.
float _WaterHeight;
float3 _UWPoolAbsorption, _UWSeaAbsorption;
float3 _UWPoolScattering, _UWSeaScattering;
float4 _UWPoolScatterColor, _UWSeaScatterColor;
float _UWSediment, _UWAnisotropy;
float4 _UWSeaHaze; // bottom extinction per metre, fade height, blur radius in pixels
float UWSeaHazeHeight(float y)
{float q=1-saturate(y/max(0.1,_UWSeaHaze.y));return q*q;}
float UWSeaHazePrimitive(float y)
{
    float top=max(0.1,_UWSeaHaze.y),q=1-saturate(y/top);
    return min(y,0)+top*(1-q*q*q)/3;
}
float UWSeaHazeColumn(float3 eye,float3 dir,float2 interval)
{
    float a=eye.y+dir.y*interval.x,b=a+dir.y*interval.y;
    float average=abs(b-a)<0.001?UWSeaHazeHeight((a+b)*0.5):(UWSeaHazePrimitive(b)-UWSeaHazePrimitive(a))/(b-a);
    return max(0,average*interval.y*_UWSeaHaze.x);
}
void UWMedium(float3 samplePosition, int region, out float3 extinction, out half3 color)
{
    float seabed=region;
    float patch=0.5+0.25*sin(dot(samplePosition.xz,float2(0.31,0.17)))+0.25*sin(dot(samplePosition.xz,float2(-0.13,0.43))+1.7);
    float sediment=1+seabed*_UWSediment*exp(-max(samplePosition.y,0)*0.75)*(0.35+0.65*patch);
    extinction=lerp(_UWPoolAbsorption,_UWSeaAbsorption,seabed)+lerp(_UWPoolScattering,_UWSeaScattering,seabed)*sediment;
    color=lerp(_UWPoolScatterColor.rgb,_UWSeaScatterColor.rgb,seabed);
    // Sunlight attenuates with depth, leaving a soft ambient contribution.
    color*=0.42+0.58*exp(-max(0,_WaterHeight-samplePosition.y)*lerp(0.055,0.105,seabed));
}
float2 UWVolumeInterval(float3 eye,float3 dir,float total,int region)
{
    float3 safeDir=float3(abs(dir.x)<1e-5?1e-5:dir.x,abs(dir.y)<1e-5?1e-5:dir.y,abs(dir.z)<1e-5?1e-5:dir.z);
    float4 bounds=UWRegion(region);
    float3 lo=(float3(bounds.x-bounds.z,0,bounds.y-bounds.w)-eye)/safeDir;
    float3 hi=(float3(bounds.x+bounds.z,_WaterHeight,bounds.y+bounds.w)-eye)/safeDir;
    float3 nearT=min(lo,hi),farT=max(lo,hi);
    float entry=max(0,max(nearT.x,max(nearT.y,nearT.z)));
    float leave=min(total,min(farT.x,min(farT.y,farT.z)));
    return float2(entry,max(0,leave-entry));
}
half3 UWIntegrateWater(half3 scene,float3 eye,float3 world,float3 lightDirection)
{
    float3 delta=world-eye;float distanceToPoint=length(delta);
    float3 dir=delta/max(distanceToPoint,1e-5);
    float2 pool=UWVolumeInterval(eye,dir,distanceToPoint,0);
    float2 sea=UWVolumeInterval(eye,dir,distanceToPoint,1);
    // Composite the far volume first, so looking through two volumes is ordered.
    int farRegion=pool.x>sea.x?0:1;
    [unroll] for(int n=0;n<2;n++)
    {
        int region=n==0?farRegion:1-farRegion;
        float2 interval=region==0?pool:sea;
        if(interval.y<=0)continue;
        float3 midpoint=eye+dir*(interval.x+interval.y*0.5);
        float3 extinction;half3 scatter;UWMedium(midpoint,region,extinction,scatter);
        float hazeColumn=region==1?UWSeaHazeColumn(eye,dir,interval):0;
        float siltColumn=UWSiltColumn(eye,dir,interval.x,interval.y);
        float3 siltOptical=float3(1.05,1.16,1.38)*siltColumn;
        float3 hazeOptical=float3(0.94,1,1.07)*hazeColumn;
        float3 transmission=exp(-extinction*interval.y-siltOptical-hazeOptical);
        float g=_UWAnisotropy,cosine=dot(dir,lightDirection);
        float phase=(1-g*g)/pow(max(0.2,1+g*g-2*g*cosine),1.5);
        float daylight=lerp(1,clamp(phase,0.45,1.7),0.35);
        half3 mixture=lerp(scatter,half3(0.16,0.145,0.10),saturate(siltColumn/max(0.01,interval.y)*2));
        mixture=lerp(mixture,half3(0.075,0.17,0.165),saturate(hazeColumn/max(0.01,interval.y)*1.5));
        scene=scene*transmission+mixture*daylight*(1-transmission);
        // World-space cones: independent of eye rotation, bounded six samples.
        if(_UWInteractionReady>0.5 && _UWLampSlots>0)
        {
          // Integrate only the portion of each ray inside a work-light sphere.
          // Rays missing the local light skip the scattering samples entirely.
          [loop] for(int lamp=0;lamp<_UWLampSlots;lamp++)
          {
            float3 delta=eye-_UWWorkLampPositions[lamp].xyz;
            float closest=-dot(delta,dir),r=_UWWorkLampPositions[lamp].w;
            float disc=r*r-dot(delta,delta)+closest*closest;if(disc<=0 || r<=0)continue;
            float reach=sqrt(disc),lo=max(interval.x,closest-reach),hi=min(interval.x+interval.y,closest+reach);
            if(hi<=lo)continue;
            float stepLength=(hi-lo)/3;
            [unroll] for(int k=0;k<3;k++)
            {
              float t=lo+(k+0.5)*stepLength;float3 p=eye+dir*t;float density=UWSiltAt(p);
              float3 d=p-_UWWorkLampPositions[lamp].xyz;float distanceToLight=length(d);
              float cone=smoothstep(_UWWorkLampDirections[lamp].w,lerp(_UWWorkLampDirections[lamp].w,1,0.3),dot(d/max(distanceToLight,0.001),_UWWorkLampDirections[lamp].xyz));
              half3 irradiance=_UWWorkLampColors[lamp].rgb*cone*pow(saturate(1-distanceToLight/r),2)/(1+distanceToLight*distanceToLight*0.15);
              scene+=irradiance*(0.018+0.11*density+0.04*hazeColumn/max(0.01,interval.y))*stepLength*exp(-extinction*(t-interval.x)-(siltOptical+hazeOptical)*saturate((t-interval.x)/interval.y));
            }
          }
        }
    }
    return scene;
}
#endif
