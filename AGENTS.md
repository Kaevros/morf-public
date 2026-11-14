# Windows Widgates

- Work from this repository root. Read `README.md` and `docs/PLAN.md` before changing product behavior.
- Keep the native WPF, Win32 and Windows media session approach. Use public Windows APIs and keep timers event-driven. Avoid recurring idle timers, global input polling and background rendering.
- Keep the shelf local and copy-only. It stores file paths in memory. Clipboard writes require the user's copy action. Preserve file names, folders and originals.
- Build with the .NET 10 SDK. Keep the inbox preview build usable while it remains part of the preview distribution. Report each build and each manual scenario separately.
- Use the standard user privilege level. Treat denied media access, unavailable sources, occupied shortcuts and missing files as recoverable states.
- Record actual results with the relevant OS build and media app. Describe open scenarios as unverified. Preserve local changes and keep release publication under the owner's control.
