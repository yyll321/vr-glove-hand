param([Parameter(Mandatory = $true)][int]$UnityProcessId)
Add-Type @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public static class UemUnityRefresh {
 [DllImport("user32.dll", SetLastError=true)] public static extern bool PostMessage(IntPtr hwnd,uint message,IntPtr wParam,IntPtr lParam);
 [DllImport("user32.dll")] public static extern IntPtr GetMenu(IntPtr hwnd);
 [DllImport("user32.dll")] public static extern IntPtr GetSubMenu(IntPtr menu,int position);
 [DllImport("user32.dll")] public static extern int GetMenuItemCount(IntPtr menu);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] public static extern int GetMenuString(IntPtr menu,uint item,StringBuilder text,int length,uint flags);
 [DllImport("user32.dll")] public static extern uint GetMenuItemID(IntPtr menu,int position);
}
'@
$panelRefreshWindow = (Get-Process -Id $UnityProcessId).MainWindowHandle
if ($panelRefreshWindow -eq [IntPtr]::Zero) { throw 'Unity window unavailable.' }
function Find-RefreshCommand([IntPtr]$menu) {
    for ($panelMenuIndex = 0; $panelMenuIndex -lt [UemUnityRefresh]::GetMenuItemCount($menu); $panelMenuIndex++) {
        $panelMenuText = New-Object Text.StringBuilder 512
        [UemUnityRefresh]::GetMenuString($menu, $panelMenuIndex, $panelMenuText, 512, 0x400) | Out-Null
        if ($panelMenuText.ToString() -match '^&?Refresh\b|^刷新') {
            return [UemUnityRefresh]::GetMenuItemID($menu, $panelMenuIndex)
        }
        $panelSubmenu = [UemUnityRefresh]::GetSubMenu($menu, $panelMenuIndex)
        if ($panelSubmenu -ne [IntPtr]::Zero) {
            $panelRefreshCommand = Find-RefreshCommand $panelSubmenu
            if ($null -ne $panelRefreshCommand) { return $panelRefreshCommand }
        }
    }
}
$panelMenu = [UemUnityRefresh]::GetMenu($panelRefreshWindow)
$panelCommand = Find-RefreshCommand $panelMenu
if ($null -ne $panelCommand) {
    [UemUnityRefresh]::PostMessage($panelRefreshWindow, 0x0111, [IntPtr]$panelCommand, [IntPtr]::Zero) | Out-Null
    Write-Output 'Invoked Unity Assets Refresh menu command.'
    exit
}
[UemUnityRefresh]::PostMessage($panelRefreshWindow, 0x001C, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
[UemUnityRefresh]::PostMessage($panelRefreshWindow, 0x0006, [IntPtr]::Zero, [IntPtr]::Zero) | Out-Null
[UemUnityRefresh]::PostMessage($panelRefreshWindow, 0x001C, [IntPtr]1, [IntPtr]::Zero) | Out-Null
[UemUnityRefresh]::PostMessage($panelRefreshWindow, 0x0006, [IntPtr]1, [IntPtr]::Zero) | Out-Null
Write-Output 'Sent refresh notification to Unity.'
