# HandDriver.exe to Unity setup

Unity side is already wired through the imported UdexReal HandDriver plugin.

## Unity scene

Open this scene:

```text
Assets/HandDriver/Scenes/Sample.unity
```

The scene contains:

- `Network` GameObject with `Network.cs`
- `LeftHand` and `RightHand` driven by `HandDriver.cs`
- `UsingNetwork` enabled on both hands
- UDP listen port `5555`

This scene is also registered in `ProjectSettings/EditorBuildSettings.asset`.

## HandDriver.exe settings

`handdriver.exe` is not present inside this Unity project, so run it from wherever UdexReal installed or extracted it.

In HandDriver.exe:

- Connect and calibrate the glove first.
- Enable/choose Unity or UDP/Motive Stream output.
- Set target IP to `127.0.0.1` when HandDriver.exe and Unity run on the same PC.
- Set target port to `5555`.
- Start streaming before or after pressing Play in Unity.

If HandDriver.exe is on another machine, use the Unity PC's LAN IP instead of `127.0.0.1`, and allow UDP port `5555` through Windows Firewall.

## Quick test flow

1. Open `Assets/HandDriver/Scenes/Sample.unity`.
2. Press Play in Unity.
3. Start streaming in HandDriver.exe to `127.0.0.1:5555`.
4. Move the glove. The left/right hand model should bend with the glove data.

## Notes

- `Network.cs` now tolerates incomplete or malformed packets instead of throwing errors while the driver is warming up.
- Enable `Log Packets` on the `Network` component only when debugging; logging every UDP packet can slow the Editor.
