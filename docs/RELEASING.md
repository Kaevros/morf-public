# Release readiness

The current product version is **0.3.0-preview**. Local MSIX package revisions are installation counters, not separate public product releases. A locally trusted preview certificate is not a public distribution signing strategy.

## Before the first public binary

- Resolve the outstanding antivirus findings through vendor review and validate the exact release artifact. Earlier Kaspersky behavioral detections were not established as false positives; clean static scans did not settle them.
- Choose the distribution/signing channel. Keep signing keys outside Git and publish SHA256 checksums alongside artifacts.
- Run the automated checks and record each runtime separately. Complete the physical scenarios below on the intended release artifact.
- Check portable/MSIX feature differences, notification permission and banner-preference recovery during update and uninstall.
- Review screenshots, documentation, version numbers, package contents and license. Publish a tag or release only through the owner's release decision.

## Validation matrix

| Area | Required manual scenarios |
| --- | --- |
| Media | Apple Music, Spotify and YouTube in Edge/Chrome; source switching; unsupported commands; pause/resume ownership |
| Shelf | Explorer drop/copy/paste, multiple selections, original preservation, missing files, groups and undo |
| Placement | Supported Windows builds, taskbar positions, 100/125/150% DPI, multiple monitors, resolution changes and sleep/resume |
| Games | Windowed/fullscreen transitions and return to the previous desktop layout |
| Notifications | Permission denied/revoked, long text, swipe dismissal, real applications, duplicate banners, normal/crash recovery, update/uninstall |
| Audio | Device removal, volume callbacks, real speaker + Bluetooth playback, latency and long-duration drift |
| Accessibility | Keyboard focus, shortcut conflicts, high contrast and reduced motion |
| Resources | CPU, GPU and memory while idle, playing media and showing a full shelf; measure the release EXE |

Passing synthetic WPF checks does not close physical-device scenarios. Do not publish performance or compatibility claims without the corresponding measurements.

## Current evidence

Repository preparation was checked on Windows build **26300.9974**:

| Check | Result |
| --- | --- |
| .NET 10 Release build | Passed; 0 warnings, 0 errors |
| .NET 10 standard checks | 375 passed |
| Inbox build and standard checks | Passed; 375 checks |
| .NET WPF player/mixer visual checks | 13 passed; light/dark and narrow mixer layouts |
| Portable archive | Four-file allowlist and embedded EXE SHA256 verified |

README images were inspected from the actual WPF rendering with demonstration content. Local logs remain under `artifacts/`. Full live media, broad Windows/DPI coverage and physical multi-output audio remain open.

The full native sequence has reproduced an Inbox settings-window shutdown problem. Run individual modes to distinguish this issue from an application regression. Some native audio modes access a loopback interface; the documentation-only visual mode does not.

## Packaging

Use the commands in [BUILDING.md](BUILDING.md). `Build-Test-Package.ps1` verifies a source-free portable ZIP. `Build-NotificationPackage.ps1` validates the MSIX payload and can sign with an owner-selected certificate.

Exit the running app through its own exit action before replacing it. The in-memory shelf list is discarded on exit; original files remain untouched. Installers must not kill the app to complete an update.

After an installation, verify the package version/status, compare the installed EXE SHA256 with the staged payload, reopen Morf and check the intended media/device scenarios. Preserve preferences during updates.

Generated binaries, certificates, local reports, SDKs and development archives do not belong in the source tree. No public release is implied by a successful local build.
