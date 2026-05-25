# r400-remap

Remaps the **B button** on a USB presenter remote (VID `0x3151`, PID `0x3020`)
to **F13** so it can be used as a dedicated push-to-talk / toggle-to-talk hotkey
without colliding with the keyboard's real `B`.

The remote sends a burst keydown + keyup on release (no true hold signal), so
F13 is used in a "tap to start, tap to stop" toggle fashion with Whisper Keyboard.

## Files

| File | Platform | What it does |
| --- | --- | --- |
| `r400-remap.ps1` | Windows | PowerShell + C# Raw Input listener. Filters `WM_INPUT` events by device VID/PID and synthesizes an F13 keystroke via `SendInput`. Runs as a foreground process. |
| `r400-remap-mac.sh` | macOS | Uses Apple's built-in `hidutil` to remap at the HID layer (no third-party app). Can install a LaunchAgent for persistence. |

## Windows

```powershell
powershell -ExecutionPolicy Bypass -File r400-remap.ps1
```

Leave the window open while you want the remap active. The remote's B will
also still be delivered to the focused app (Raw Input is observational —
it can listen but can't suppress).

To run hidden at login, create a `.vbs` launcher pointing at the `.ps1` and
drop it in your Startup folder.

## macOS

```bash
chmod +x r400-remap-mac.sh
./r400-remap-mac.sh install   # apply + install LaunchAgent for login persistence
./r400-remap-mac.sh apply     # one-shot, no LaunchAgent
./r400-remap-mac.sh status    # show current mapping
./r400-remap-mac.sh uninstall # remove LaunchAgent and clear remap
```

`hidutil` ships with macOS, so no install needed. The remap is scoped to the
matching VID/PID, so your built-in keyboard is unaffected.

## Identifying a different device

If your remote has different IDs, find them with:

- **Windows**: Device Manager → right-click device → Properties → Details → "Hardware Ids" (look for `VID_xxxx&PID_xxxx`).
- **macOS**: `ioreg -p IOUSB -l | grep -E "idVendor|idProduct|USB Product Name"`

Then update the constants near the top of the relevant script.
