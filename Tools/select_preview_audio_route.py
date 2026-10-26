"""Switch the console/media route for the PICO test, retaining a restore record."""
import argparse
import ctypes
import importlib.util
import json
from pathlib import Path

root = Path(__file__).resolve().parents[1]
spec = importlib.util.spec_from_file_location('audio_inspect', root / 'Tools/inspect_audio_route.py')
inspect = importlib.util.module_from_spec(spec)
spec.loader.exec_module(inspect)
parser = argparse.ArgumentParser()
parser.add_argument('action', choices=['Pico', 'Restore'])
args = parser.parse_args()
record = root / 'Logs/UnderwaterPresence/audio-route-before-switch.json'
if args.action == 'Pico':
    targets = [e for e in inspect.endpoints if e['name'] == 'Pico Streaming Speaker']
    if len(targets) != 1:
        raise RuntimeError('Exactly one active PICO playback endpoint is required.')
    if not record.exists():
        record.write_text(json.dumps(inspect.report, ensure_ascii=False, indent=2), encoding='utf-8')
    endpoint = '{0.0.0.00000000}.' + targets[0]['id']
    destinations = {0: endpoint, 1: endpoint}
else:
    previous = json.loads(record.read_text(encoding='utf-8'))
    destinations = {0: previous['defaults']['console'], 1: previous['defaults']['multimedia']}

ole = ctypes.OleDLL('ole32')
ole.CoInitialize(None)
policy = ctypes.c_void_p()
cls = inspect.guid('870AF99C-171D-4F9E-AF0D-E63DF40C2BC9')
iid = inspect.guid('F8679F50-850A-41CF-9C72-430F290290C8')
ole.CoCreateInstance(ctypes.byref(cls), None, 1, ctypes.byref(iid), ctypes.byref(policy))
setter = inspect.method(policy, 13, ctypes.HRESULT, ctypes.c_wchar_p, ctypes.c_int)
try:
    for role, endpoint in destinations.items():
        result = setter(policy, endpoint, role)
        if result < 0:
            raise OSError(f'Windows rejected output change: {result}')
finally:
    inspect.method(policy, 2, ctypes.c_ulong)(policy)
    ole.CoUninitialize()
print('Playback route changed. Communications route was preserved. Restore: Tools/Set-UnderwaterAudioRoute.ps1 -Action Restore')
