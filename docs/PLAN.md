# Roadmap

Morf is preparing its first public preview release. The current code version is `0.3.3-preview`.

## Implemented

- Native Windows media sessions, source selection, transport controls and optional source handoff.
- Compact/expanded panel states, pinning, monitor selection and application/game hiding rules.
- Local copy-only shelf with ordering, temporary groups and undo.
- Light/dark/system themes, surface transparency and reduced-motion behavior.
- Per-application audio mixer and experimental multi-output playback.
- Opt-in Windows notification previews in the identity-bearing MSIX build.
- Notifications inside the expanded hub, content-first headings and matched media artwork/album details.
- Per-user preferences, optional startup, tray actions and configurable shortcuts.
- In-panel countdown and stopwatch, compact indicators and local paused stopwatch restoration.

## Release priorities

1. Resolve antivirus findings and choose the signing/distribution channel.
2. Complete the live Windows/media/device matrix in [release readiness](RELEASING.md).
3. Validate notification recovery across update, crash and uninstall scenarios.
4. Verify multi-output audio on physical speakers and Bluetooth hardware.
5. Finalize installation instructions and publish the owner-approved preview artifacts.

## Later

- Broader Windows/device compatibility, based on reported results.
- Interface localization.
- Picture-in-picture feasibility remains deferred; it is not part of the first release.

Build instructions are in [BUILDING.md](BUILDING.md), implementation boundaries in [ARCHITECTURE.md](ARCHITECTURE.md), and data/permission behavior in [PRIVACY.md](PRIVACY.md).
