#ifndef UW_INTERACTION_INCLUDED
#define UW_INTERACTION_INCLUDED
float _UWInteractionReady; int _UWPlumeSlots, _UWLampSlots;
float4 _UWPlumes[12], _UWPlumeDensities[12];
float4 _UWWorkLampPositions[2], _UWWorkLampDirections[2], _UWWorkLampColors[2];
float UWSiltAt(float3 p)
{
 float density=0;
 [loop] for(int i=0;i<_UWPlumeSlots;i++)
 { float3 d=p-_UWPlumes[i].xyz;float q=saturate(1-dot(d,d)/max(0.001,_UWPlumes[i].w*_UWPlumes[i].w));density+=_UWPlumeDensities[i].x*q*q; }
 return min(3,density)*_UWInteractionReady;
}
// Exact line integral of each compact spherical (1-r^2/R^2)^2 puff.
// Small clouds remain visible even when a coarse raymarch would skip them.
float UWSiltColumn(float3 eye,float3 dir,float entry,float length)
{
 float column=0;
 [loop] for(int i=0;i<_UWPlumeSlots;i++)
 {
  if(_UWPlumeDensities[i].x<=0)continue;
  float3 delta=eye-_UWPlumes[i].xyz;float centre=-dot(delta,dir);
  float r2=max(0.001,_UWPlumes[i].w*_UWPlumes[i].w);
  float a=1-(dot(delta,delta)-centre*centre)/r2;if(a<=0)continue;
  float reach=sqrt(a*r2),lo=max(-reach,entry-centre),hi=min(reach,entry+length-centre);if(hi<=lo)continue;
  float lo2=lo*lo,hi2=hi*hi;
  column+=_UWPlumeDensities[i].x*(a*a*(hi-lo)-2*a*(hi*hi2-lo*lo2)/(3*r2)+(hi*hi2*hi2-lo*lo2*lo2)/(5*r2*r2));
 }
 return max(0,column)*_UWInteractionReady;
}
half3 UWLocalLamp(float3 p)
{
 half3 light=0;
 [loop] for(int i=0;i<_UWLampSlots;i++)
 {
  float3 d=p-_UWWorkLampPositions[i].xyz;float r=length(d);
  float range=_UWWorkLampPositions[i].w;if(range<=0 || r>range)continue;
  float cone=smoothstep(_UWWorkLampDirections[i].w,lerp(_UWWorkLampDirections[i].w,1,0.3),dot(d/max(r,0.001),_UWWorkLampDirections[i].xyz));
  light+=_UWWorkLampColors[i].rgb*cone*pow(saturate(1-r/range),2)/(1+r*r*0.15);
 }
 return light*_UWInteractionReady;
}
#endif
