# Shelf, notification activation and friend setup — 0.3.2-preview

Date: 2026-11-20. Windows build **26300.9974**; standard user. Product source version and both runtime builds are `0.3.2-preview`.

## Behavior

- The complete player is laid out before the file shelf. Source/settings header, artwork/title, timeline, transport buttons and volume retain their positions relative to the media panel when files are added. File content grows below them. The existing empty-shelf undo action remains available.
- Very small work areas retain native vertical scrolling so media controls and shelf content remain reachable. At normal available height, the original media area stays at the top and the shelf extends beneath it. Bottom docking retains its existing edge anchoring.
- Notification activation previously rejected identities without `!` and used packaged-only activation. It now accepts validated opaque desktop identities and registered GUID suffixes as well as packaged AUMIDs, resolves them only in Windows AppsFolder and invokes the registered open action on an STA worker. Paths, URLs, command arguments and malformed GUIDs are rejected before activation.
- A notification click opens the source application. Opening the original specific chat, screenshot or private toast action is not implemented; that action payload is not exposed by this listener integration. Missing/unregistered sources remain recoverable.
- Island scrollbars receive their themed template before overflow is first displayed. Horizontal file scrollbars explicitly override the native default; notification text continues to scroll normally.
- Closing-window deactivation/visibility callbacks are ignored. A regression scenario exposed an attempt to show a window while it was closing; it passed after adding the closing guards.

## Builds and checks

| Check | .NET 10 | Inbox / .NET Framework 4.8 |
| --- | --- | --- |
| Release product build | Passed; 0 warnings/errors | Passed; warnings treated as errors |
| Standard checks | 384 passed | 384 passed |
| Expansion and new shelf/player/scrollbar scenarios | 34 passed | 34 passed |
| Windows notification presentation | 18 passed | 18 passed |
| Notification lifecycle | 18 passed | 18 passed |
| Floating layout | 13 passed | 13 passed |
| Display recovery | 58 passed | 58 passed |
| Clock | 16 passed | 16 passed |
| Panel lifecycle | 9 passed | 9 passed |
| Registered application resolution and live Spotify activation | 7 passed | 7 passed |

The new native shelf checks compare source, title, timeline, transport and volume positions during growth; verify file preservation and the existing undo action; and exercise overflowing notification text and actual scrolling in dark/light themes. The constrained-height lifecycle expectation now verifies that playback is centered and reachable by native scrolling in the new media-first ordering.

Individual native modes were run against the final product sources. The full optional native sequence, including the previously documented Inbox settings shutdown scenario, was not rerun or claimed as passing.

Actual WPF output was inspected for the player above a populated shelf, themed overflowing notification content and the setup window. Test media labels/artwork and file fixtures were synthetic; original files and clipboard contents were not changed by the checks.

## Application activation evidence

- Apple Music, Snipping Tool, WhatsApp, packaged Spotify and desktop Microsoft Access identities resolved through the actual Windows AppsFolder namespace on this device.
- Both runtimes sent an explicit real Spotify opening request; Windows accepted it. After the request, the foreground process was `Spotify`, checked through the public Win32 foreground-window APIs. No playback command or message was sent.
- Desktop identifier handling, STA dispatch, rejected paths and declined launches also passed injected regression checks.
- Real Windows toast clicks for Apple Music, WhatsApp, Snipping Tool and arbitrary desktop sources remain **unverified**. AppsFolder resolution alone is not proof of those applications' full notification workflows.

## Friend-test archive

`Morf-0.3.2-Kurulum-x64.zip` was built with `scripts/Build-FriendSetup.ps1` and copied to the owner's Windows Desktop. ZIP SHA256: `373A0EC0A89A922609A98741821D487F6C64654DCAC52D5DB5DBA1ADD45B25BC`.

Its exact five-file allowlist is `Kurulum.exe`, `Morf-0.3.2-x64.msix`, `Morf.cer`, `KURULUM.txt`, `SHA256.txt`. Every archived file hash was compared with its staged source, and the Desktop ZIP hash matched the build output. Setup EXE and MSIX signatures were valid on the build device. The public certificate was verified not to contain a private key. No source, saved preferences, notification history or signing private key enters the archive.

The setup runs as the user, validates embedded package/certificate hashes and opens the Windows certificate/MSIX installers only on explicit button clicks. A first-device certificate trust step may require administrator approval; Morf itself runs as a standard user. `--verify` succeeded for the intact payload and rejected a deliberately modified certificate. `--render` produced the inspected setup preview without installing anything.

No installation or certificate trust change was performed by this task. Clean-device installation, a friend's certificate/permission prompts, real-device media behavior, physical multi-DPI scenarios and antivirus acceptance remain **unverified**. This friend-test archive is not a publicly trusted or Store-signed release.

## API references

- [ShellExecuteEx and STA initialization](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/nf-shellapi-shellexecuteexw)
- [Shell namespace identity parsing](https://learn.microsoft.com/en-us/windows/win32/api/shlobj_core/nf-shlobj_core-shparsedisplayname)
- [Shell execution by item identifier list](https://learn.microsoft.com/en-us/windows/win32/api/shellapi/ns-shellapi-shellexecuteinfow)
