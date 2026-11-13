# MiControl

A lightweight replacement for Xiaomi PC Manager on REDMI Book Pro 14 2026 (TM2426).

## Features

- Battery charge limit (40/50/60/70/80/90/100 %) enforced through the Xiaomi MIFS ACPI-WMI interface
- Performance modes: Quiet / Smart / Performance / FullSpeed, optional auto FullSpeed on AC
- Vendor hotkeys: Fn+F4 microphone mute, Fn+F8 projection, Fn+F9 settings, F7 and custom keys
- Event driven: no polling timers; responds to AC changes and hotkeys immediately

## Architecture

- `MiControlService` (Windows service, LocalSystem): MIFS WMI calls, charge guard, hotkey listener,
  loopback TCP IPC on 127.0.0.1, power notifications through RegisterPowerSettingNotification.
- `MiControlTray`: tray UI, settings dialog, IPC client with push channel.

## Versions

- 1.2.1 - stable, WinForms tray
- 1.3.0-Beta - same service with a pure Win32 tray (smaller, UI not finalised)

## Requirements

- Windows 11 x64
- .NET 8 Desktop Runtime
- Administrator rights for the service (installed by MSI)