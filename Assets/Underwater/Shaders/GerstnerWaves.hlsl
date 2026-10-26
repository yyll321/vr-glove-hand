#ifndef UNDERWATER_GERSTNER_INCLUDED
#define UNDERWATER_GERSTNER_INCLUDED
float4 _UWWaves[8]; // wave vector x/z, amplitude, steepness
float4 _UWSpeeds[2];
float4 _UWPhases[2];
float _UWTime, _UWTileSize, _UWProjectionDepth, _UWMicroStrength;
int _UWWaveCount;
float3 _UWLightTravel;
void UWWave(float2 x, out float3 p, out float3 n)
{
    p=float3(x.x,0,x.y); float3 dx=float3(1,0,0), dz=float3(0,0,1);
    [loop] for(int i=0;i<_UWWaveCount;i++)
    {
        float4 w=_UWWaves[i]; float k=max(length(w.xy),0.00001); float2 d=w.xy/k;
        float speed=_UWSpeeds[i/4][i%4];
        float phase=dot(w.xy,x)-sqrt(9.81*k)*speed*_UWTime+_UWPhases[i/4][i%4];
        float s=sin(phase),c=cos(phase);
        p+=float3(w.w*w.z*d.x*c,w.z*s,w.w*w.z*d.y*c);
        dx+=float3(-w.w*w.z*d.x*w.x*s,w.z*w.x*c,-w.w*w.z*d.y*w.x*s);
        dz+=float3(-w.w*w.z*d.x*w.y*s,w.z*w.y*c,-w.w*w.z*d.y*w.y*s);
    }
    n=normalize(cross(dz,dx));
    // Short, periodic wind ripples; integer cycles ensure seamless tiling without edge blending.
    float2 u=x*(6.2831853/_UWTileSize);
    n=normalize(n+float3(cos(dot(u,float2(17,11))-_UWTime*1.8),0,sin(dot(u,float2(-13,19))+_UWTime*1.3))*_UWMicroStrength);
}
float2 UWProject(float2 x)
{
    float3 p,n; UWWave(x,p,n);
    float3 ray=refract(normalize(_UWLightTravel),n,1.0/1.333);
    float3 flat=refract(normalize(_UWLightTravel),float3(0,1,0),1.0/1.333);
    float2 shift=flat.xz*(-_UWProjectionDepth/min(flat.y,-0.001));
    return p.xz+ray.xz*((-_UWProjectionDepth-p.y)/min(ray.y,-0.001))-shift;
}
#endif
