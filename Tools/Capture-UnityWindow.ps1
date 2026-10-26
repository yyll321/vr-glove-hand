Add-Type -AssemblyName System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public static class UnderwaterWindowCapture {
 [StructLayout(LayoutKind.Sequential)] public struct Rect { public int L,T,R,B; }
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hwnd,out Rect rect);
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hwnd,IntPtr hdc,uint flags);
}
'@
$captureUnity=Get-Process Unity | Where-Object { $_.MainWindowHandle -ne [IntPtr]::Zero } | Select-Object -First 1
$captureRect=New-Object UnderwaterWindowCapture+Rect
[UnderwaterWindowCapture]::GetWindowRect($captureUnity.MainWindowHandle,[ref]$captureRect) | Out-Null
$captureBitmap=New-Object Drawing.Bitmap ($captureRect.R-$captureRect.L),($captureRect.B-$captureRect.T)
$captureGraphics=[Drawing.Graphics]::FromImage($captureBitmap)
$captureDc=$captureGraphics.GetHdc()
[UnderwaterWindowCapture]::PrintWindow($captureUnity.MainWindowHandle,$captureDc,2) | Out-Null
$captureGraphics.ReleaseHdc($captureDc)
$captureBitmap.Save((Join-Path (Split-Path -Parent $PSScriptRoot) 'Logs/UnderwaterPresence/unity-window.png'))
$captureGraphics.Dispose();$captureBitmap.Dispose()
$captureUnity | Select-Object Id,MainWindowTitle,Responding
