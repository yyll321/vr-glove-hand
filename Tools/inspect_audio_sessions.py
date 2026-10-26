"""Inspect render endpoints, per-process sessions and PCM meters without modifying them."""
import ctypes as c
import importlib.util
import json
from pathlib import Path

project = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('routes', project / 'Tools/inspect_audio_route.py')
routes = importlib.util.module_from_spec(spec)
spec.loader.exec_module(routes)
ole = c.OleDLL('ole32')
ole.CoInitialize(None)
enum = c.c_void_p()
cls = routes.guid('BCDE0395-E52F-467C-8E3D-C4579291692E')
iid = routes.guid('A95664D2-9614-4F35-A746-DE8DB63617E6')
ole.CoCreateInstance(c.byref(cls), None, 1, c.byref(iid), c.byref(enum))

def release(p):
    if p:
        routes.method(p, 2, c.c_ulong)(p)

def query(p, guid):
    result = c.c_void_p()
    identity = routes.guid(guid)
    hr = routes.method(p, 0, c.HRESULT, c.c_void_p, c.POINTER(c.c_void_p))(p, c.byref(identity), c.byref(result))
    return result if hr >= 0 else None

out = []
for endpoint in routes.endpoints:
    device = c.c_void_p()
    routes.method(enum, 5, c.HRESULT, c.c_wchar_p, c.POINTER(c.c_void_p))(enum, '{0.0.0.00000000}.' + endpoint['id'], c.byref(device))
    if not device:
        continue
    manager = c.c_void_p()
    identity = routes.guid('77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F')
    hr = routes.method(device, 3, c.HRESULT, c.c_void_p, c.c_uint, c.c_void_p, c.POINTER(c.c_void_p))(device, c.byref(identity), 23, None, c.byref(manager))
    if hr < 0:
        release(device)
        continue
    sessions = c.c_void_p()
    routes.method(manager, 5, c.HRESULT, c.POINTER(c.c_void_p))(manager, c.byref(sessions))
    count = c.c_int()
    routes.method(sessions, 3, c.HRESULT, c.POINTER(c.c_int))(sessions, c.byref(count))
    for i in range(count.value):
        control = c.c_void_p()
        routes.method(sessions, 4, c.HRESULT, c.c_int, c.POINTER(c.c_void_p))(sessions, i, c.byref(control))
        control2 = query(control, 'BFB7FF88-7239-4FC9-8FA2-07C950BE9C6D')
        pid, state = c.c_uint(), c.c_int()
        routes.method(control, 3, c.HRESULT, c.POINTER(c.c_int))(control, c.byref(state))
        if control2:
            routes.method(control2, 14, c.HRESULT, c.POINTER(c.c_uint))(control2, c.byref(pid))
        volume = query(control, '87CE5498-68D6-44E5-9215-6DA47EF883D8')
        level, mute = c.c_float(), c.c_int()
        if volume:
            routes.method(volume, 4, c.HRESULT, c.POINTER(c.c_float))(volume, c.byref(level))
            routes.method(volume, 6, c.HRESULT, c.POINTER(c.c_int))(volume, c.byref(mute))
        meter = query(control, 'C02216F6-8C67-4B5B-9D00-D008E73E0064')
        peak = c.c_float()
        if meter:
            routes.method(meter, 3, c.HRESULT, c.POINTER(c.c_float))(meter, c.byref(peak))
        out.append(dict(endpoint=endpoint['name'], pid=pid.value, state=state.value, volume=level.value, muted=bool(mute.value), peak=peak.value))
        for pointer in [meter, volume, control2, control]:
            release(pointer)
    for pointer in [sessions, manager, device]:
        release(pointer)
release(enum)
ole.CoUninitialize()
path = project / 'Logs/UnderwaterPresence/windows-audio-sessions.json'
path.write_text(json.dumps(out, indent=2), encoding='utf-8')
print(json.dumps(out, indent=2))
