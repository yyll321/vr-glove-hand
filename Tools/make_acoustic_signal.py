from pathlib import Path
import numpy as np
import wave

rate=48000
seconds=6
t=np.arange(rate*seconds)/rate
rng=np.random.default_rng(875)
noise=rng.normal(0,1,t.size)
# Quiet harmonic thruster plus a smooth short chirp for comparing travel time and reflected paths.
motor=0.08*np.sin(2*np.pi*63*t)+0.065*np.sin(2*np.pi*125*t)+0.03*np.sin(2*np.pi*250*t)+0.012*noise
pulse=np.zeros_like(t)
for start in [0.4,3.4]:
    dt=t-start
    window=(dt>=0)&(dt<0.08)
    phase=2*np.pi*(900*dt+900*dt*dt/0.16)
    pulse+=window*0.20*np.sin(phase)*np.sin(np.pi*np.clip(dt/0.08,0,1))**2
samples=np.int16(np.clip(motor+pulse,-0.9,0.9)*32767)
out=Path('Assets/Underwater/Audio/ROVMotorAndPing.wav')
out.parent.mkdir(parents=True,exist_ok=True)
with wave.open(str(out),'wb') as f:
    f.setnchannels(1);f.setsampwidth(2);f.setframerate(rate);f.writeframes(samples.tobytes())
