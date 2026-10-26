"""Deterministic, loopable stereo water bed and an unambiguous route-test cue."""
from pathlib import Path
import wave
import numpy as np

root = Path(__file__).resolve().parents[1] / 'Assets/Underwater/Audio'
rate = 48000
rng = np.random.default_rng(71)

def save(name, data):
    data = np.clip(data, -.95, .95)
    with wave.open(str(root / name), 'wb') as out:
        out.setnchannels(2)
        out.setsampwidth(2)
        out.setframerate(rate)
        out.writeframes((data * 32767).astype('<i2').tobytes())

duration = 24
t = np.arange(duration * rate) / rate
freq = np.fft.rfftfreq(len(t), 1 / rate)
stereo = []
for channel in range(2):
    noise = np.fft.rfft(rng.normal(size=len(t)))
    noise *= (freq / (freq + 65)) / (1 + (freq / 850) ** 3)
    bed = np.fft.irfft(noise, n=len(t))
    bed /= np.sqrt(np.mean(bed * bed))
    # Four complete 6 s breath cycles; no nonperiodic fade at the loop boundary.
    breath = .5 - .5 * np.cos(t * 2 * np.pi / 6)
    bed *= .065 + .042 * breath
    for start in [1.8, 7.8, 13.8, 19.8]:
        tau = np.maximum(t - start - channel * .025, 0)
        bubble = np.sin(2 * np.pi * (400 * tau + 650 * tau ** 2))
        bed += .055 * bubble * np.exp(-tau * 12) * (t >= start + channel * .025)
    stereo.append(bed)
save('WaterFlowAndBreathing.wav', np.stack(stereo, axis=-1))

t = np.arange(rate * 2) / rate
cue = np.zeros((len(t), 2))
for start, end, side, pitch in [(0, .48, 0, 660), (.65, 1.13, 1, 880), (1.3, 1.85, 2, 550)]:
    window = (t >= start) & (t < end)
    phase = (t[window] - start) / (end - start)
    tone = .4 * np.sin(np.pi * phase) ** 2 * np.sin(2 * np.pi * pitch * t[window])
    if side < 2:
        cue[window, side] = tone
    else:
        cue[window, :] = tone[:, None] * .707
save('AudioRouteTest_LeftRightBoth.wav', cue)
