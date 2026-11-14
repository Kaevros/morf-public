# Contributing

Morf is a Windows desktop project. Use the repository root as the working directory and read [architecture](docs/ARCHITECTURE.md) before changing application behavior.

## Build and check

Install the .NET 10 SDK, then run:

```powershell
dotnet build src/Widgates/Widgates.csproj -c Release
dotnet run --project tests/Widgates.Checks -c Release -- artifacts/checks
.\scripts\Check-InboxPreview.ps1
```

Keep the inbox preview build working while it is distributed. Native/device tests are separate from standard checks; report what was actually run. See [BUILDING.md](docs/BUILDING.md) for requirements and opt-in native modes.

## Change guidelines

- Keep WPF, Win32 and public Windows media/audio APIs. React to events; avoid recurring idle polling and background rendering.
- Preserve original files and the shelf's local, copy-only behavior. Clipboard writes require a user copy action.
- Treat denied permissions, missing devices, unavailable media sources and occupied shortcuts as recoverable states.
- Keep user preferences and unrelated work intact. Normal application use must not require administrator privileges.
- Add checks for meaningful behavior changes. Prefer existing verification for small visual or documentation changes.
- Keep documentation current rather than adding a new dated report for each edit. Local diagnostics and investigation notes belong under `artifacts/`.

## Pull requests

Describe the problem, resulting behavior and validation. Include the Windows build and media application for manual results. Mark untested scenarios clearly. For visible changes, include screenshots with demonstration data rather than private notification content or file paths.

Source contributions do not authorize a public binary release, certificate-trust change or repository visibility change. See [release readiness](docs/RELEASING.md).
