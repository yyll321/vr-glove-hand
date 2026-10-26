"""Read the Windows output endpoints and default routes without changing them."""
import ctypes
from ctypes import wintypes
import json
from pathlib import Path
import uuid
import winreg

def guid(value):
    return (ctypes.c_byte * 16).from_buffer_copy(uuid.UUID(value).bytes_le)

def method(pointer, slot, result, *args):
    table = ctypes.cast(pointer, ctypes.POINTER(ctypes.POINTER(ctypes.c_void_p))).contents
    return ctypes.WINFUNCTYPE(result, ctypes.c_void_p, *args)(table[slot])

ole = ctypes.OleDLL('ole32')
ole.CoInitialize(None)
enum = ctypes.c_void_p()
cls = guid('BCDE0395-E52F-467C-8E3D-C4579291692E')
iid = guid('A95664D2-9614-4F35-A746-DE8DB63617E6')
ole.CoCreateInstance(ctypes.byref(cls), None, 1, ctypes.byref(iid), ctypes.byref(enum))

defaults = {}
for role, name in enumerate(('console', 'multimedia', 'communications')):
    device = ctypes.c_void_p()
    method(enum, 4, ctypes.HRESULT, ctypes.c_int, ctypes.c_int, ctypes.POINTER(ctypes.c_void_p))(enum, 0, role, ctypes.byref(device))
    if not device:
        continue
    identity = ctypes.c_void_p()
    method(device, 5, ctypes.HRESULT, ctypes.POINTER(ctypes.c_void_p))(device, ctypes.byref(identity))
    defaults[name] = ctypes.wstring_at(identity)
    ole.CoTaskMemFree(identity)
    method(device, 2, ctypes.c_ulong)(device)
method(enum, 2, ctypes.c_ulong)(enum)
ole.CoUninitialize()

root = winreg.OpenKey(winreg.HKEY_LOCAL_MACHINE, r'SOFTWARE\Microsoft\Windows\CurrentVersion\MMDevices\Audio\Render')
endpoints = []
for index in range(winreg.QueryInfoKey(root)[0]):
    endpoint = winreg.EnumKey(root, index)
    key = winreg.OpenKey(root, endpoint)
    state = winreg.QueryValueEx(key, 'DeviceState')[0]
    if not state & 1:
        continue
    props = winreg.OpenKey(key, 'Properties')
    label = winreg.QueryValueEx(props, '{b3f8fa53-0004-438e-9003-51a46e139bfc},6')[0]
    endpoints.append(dict(id=endpoint, name=label, defaultRoles=[role for role, identity in defaults.items() if endpoint.lower() in identity.lower()]))
report = dict(defaults=defaults, activeOutputs=endpoints, changedSystemSettings=False)
path = Path(__file__).resolve().parents[1] / 'Logs/UnderwaterPresence/windows-audio-route.json'
path.parent.mkdir(exist_ok=True, parents=True)
path.write_text(json.dumps(report, ensure_ascii=False, indent=2), encoding='utf-8')
print(json.dumps(report, ensure_ascii=True, indent=2))
