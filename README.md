# Tablet Desk

Tablet Desk is a portable Windows app for drawing tablets that expose a standard HID pen collection. It uses Windows pen input and requires no vendor installer.

## Run the app

Download [TabletDesk.exe](./TabletDesk.exe) and open it with your tablet connected. Select **Relative mouse** to move the cursor with pen motion. Hover to move, tap to click, and lift the pen to reposition it without moving the pointer. **Mouse speed** sets the sensitivity. Use **Minimize to tray** to keep the app running in the background. **Ctrl+Alt+F10** switches between relative mouse and Windows pen input when the shortcut is available.

**Absolute mouse** maps the selected tablet area to a display and emits mouse input. Both mouse modes support crop, rotation, and pen actions where the device reports the controls. **Windows pen** leaves normal pen input available to drawing apps. The pressure curve is for the built-in preview. Tablet express-key remapping and vendor-only protocols are outside this version's supported controls.

The UGEE S640 was detected on Windows. Compatibility with other devices depends on the HID reports the device exposes.

## Build from source

From PowerShell on Windows, run `./build.ps1`. The script uses the Windows .NET Framework C# compiler and writes `build/TabletDesk.exe`.

