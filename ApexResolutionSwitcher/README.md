# Apex Resolution Switcher

A Windows desktop utility for temporarily changing a selected monitor to a higher resolution while Apex Legends runs, then restoring the resolution that was active when the app started.

## Features

- Select an active display and a resolution/refresh-rate mode exposed by Windows.
- Remembers your selected monitor, target resolution, refresh rate, resolution filters, and Apex rendering API between launches.
- Settings are stored locally in `%LOCALAPPDATA%\ApexResolutionSwitcher\settings.json`.
- Search and filter modes by resolution group (above native, 1080p+, 1440p+, 4K+, ultrawide), aspect ratio, and refresh rate.
- Select whether Apex is running in DirectX 12 (`r5apex_dx12`) or DirectX 11 (`r5apex`) mode.
- Apply the selected mode and launch Apex manually through Steam or the EA app.
- Waits for the selected game process to remain present for five seconds before considering Apex launched, and waits for it to remain absent for five seconds before restoring.
- Manual **Switch back to native** button.
- Saves the selected display's starting mode for the current app session.
- No external display utility required.

## Important notes

- The app remembers the selected Windows display mode; it does not create a custom GPU mode itself. Higher resolutions only appear if Windows/the GPU driver exposes them for the selected display. Enable AMD Virtual Super Resolution (VSR) or NVIDIA DSR/DLDSR first if needed.
- The DX11 option watches `r5apex`. The DX12 option watches `r5apex_dx12`. Select the API that matches your Apex launch configuration.
- Rendering at a higher resolution can improve image clarity but uses more GPU resources. It does not change the physical resolution of your monitor.
- Keep this app open while Apex is running. If the app is force-closed or Windows/driver fails during a mode switch, restore the desktop in Windows Display settings.
- This is an independent community utility, not affiliated with or endorsed by EA or Respawn Entertainment.

## Build a standalone Windows EXE

Requires the .NET 8 SDK on Windows.

1. Extract the project folder.
2. Open PowerShell in the folder containing `ApexResolutionSwitcher.csproj`.
3. Run `.\Build-Windows.ps1` (or publish directly using the command below).

```powershell
.\Build-Windows.ps1
```

Direct publish command:

```powershell
dotnet publish .\ApexResolutionSwitcher.csproj -c Release -r win-x64 --self-contained true /p:PublishSingleFile=true
```

The published app will be in:

`bin\Release\net8.0-windows\win-x64\publish\ApexResolutionSwitcher.exe`

You can share that EXE with other Windows x64 PC users. Since the EXE is self-contained, they should not need to install the .NET runtime. Windows SmartScreen may warn users because the app is unsigned; only share builds made from source they can inspect.

## Safety behavior

The app changes the selected monitor's active mode only after the user presses the apply button. It restores the original mode when Apex exits or when the user clicks **Switch back to native**. Avoid closing the app while a game is being monitored.
