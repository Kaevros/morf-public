# Screenshots

README images are captured from the current WPF interface using demonstration data. They do not contain private notifications, user file paths or a claim of live third-party playback.

Generate fresh captures on Windows:

```powershell
dotnet run --project tests/Widgates.Checks -c Release -- --native-mixer-visual artifacts/screenshots
```

Copy the matching PNGs into this directory:

| Generated file | Repository file |
| --- | --- |
| `player-volume-Dark.png` | `player-dark.png` |
| `player-volume-Light.png` | `player-light.png` |
| `mixer-Dark.png` | `mixer-dark.png` |
| `settings-Light.png` | `settings-light.png` |

This mode opens temporary windows and does not start audio capture or write real audio levels. Inspect each image before replacing a published screenshot.
