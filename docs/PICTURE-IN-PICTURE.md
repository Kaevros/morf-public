# Picture-in-picture preview

Opt-in browser-owned PiP. Standard video PiP is the default, without Document PiP's gray origin header; controls are provided by the browser. The optional advanced player retains ten-second seeking including YouTube, optional 5/15-second seeking, play/pause, a seekable-range-clamped timeline, source-supported previous/next, return to tab and pause-on-close. Changing player mode applies on the next opening. Capsule artwork routes only to an explicitly linked matching browser/title/artist. Spotify and track swipes retain their existing behavior.

The application remains WPF/Win32/Windows media sessions, without WebView, capture or video decoding. The companion is a Manifest V3 Chromium extension. The browser owns both window types; the advanced Document PiP origin header cannot be removed with CSS or its supported options. The extension adds inline controls on YouTube and Netflix players with recognized control containers. Playback events in the active tab establish the link. Other HTTPS sites use the extension action or shortcut. Opening an unlinked inline player requires the host's enabled confirmation.

YouTube's watch-page metadata guard runs in the page's MAIN world before site metadata is published. It requires agreement between the current `/watch?v=` ID and the main player's video data. Recommendation metadata cannot replace an accepted main-video object. When repairing an initial preview it uses artwork for the main video ID, never the preview's artwork. Navigation, empty metadata and advertisements retain their normal paths. There is no timer, background request, or playback command in this guard; it also works while the bounded main player is in advanced PiP. It depends on YouTube's player data API and may need adjustment if the site changes.

The capsule can invoke the linked player's accessible inline button after an explicit artwork click. The lookup requires matching media metadata, the launching browser process and a per-session accessibility ID. If invocation fails, the extension can offer the source-tab button.

Advanced YouTube PiP moves its bounded `#movie_player` subtree to retain the controller/video relationship. Moving only its video paused live playback during verification. Other sites move the original video, with a placeholder and inline-style restoration. Standard PiP leaves the source DOM intact. A removed/replaced source ends the session safely. Site adapters are packaged code, never remote selectors. Standard PiP's leave event does not identify the browser's close-versus-return action; Morf pauses on either exit. Advanced PiP's explicit return action preserves playback.

## Boundaries

First launch and the PiP switch offer a browser picker for local installation. Store confirmation requires a published listing. On Windows, Morf listens for move/resize completion in the launching browser process during an open PiP session. It checks the window's session-specific UI Automation marker before snapping it inside the nearest monitor's working area. Physical multi-monitor placement remains unverified. All-desktop pinning is still a Task View action.

On YouTube the two video navigation buttons can also use playlist neighbors, a previous same-origin watch entry, or a suggested video. A manual ad-skip button invokes YouTube's own available skip control. There is no Premium entitlement integration.

`PictureInPictureRegistration` exports embedded extension files and current-user host registrations only when enabled in a real Morf instance. It does not install extensions, change browser policy, elevate or publish. Chromium launches the registered executable with the extension origin as its first argument and an optional `--parent-window` argument. This real browser invocation, as well as the diagnostic `--pip-native-host <origin>` form, enters the native host before WPF or single-instance activation. Rejected extension origins and unknown command-line arguments fail closed without opening the capsule. Traffic is framed UTF-8 JSON capped at 8192 bytes before allocation, with version/type/direction/token/metadata validation. IPC uses a current-user-restricted, per-user/per-logon-session pipe. Content-script messages are checked for extension identity, selected tab, top frame, origin and token.

Disabling disposes the pipe. Browser cleanup removes listeners/observers/temporary DOM and pauses/closes PiP. The host exits on disconnect. Handshake/write deadlines are bounded and canceled. There is no idle timer, automatic reconnection, heartbeat or recurring capture. Unchanged settings do not transmit configuration; `timeupdate` updates progress without querying site capabilities or rewriting titles.

On explicit host startup, one public Toolhelp snapshot identifies the launching browser by exact executable name. This distinguishes Chromium variants such as Brave even when their user agents say Chrome, without a recurring process watcher. Denied/unknown caller lookup retains the conservative extension hint; artwork remains subject to exact source/metadata matching.

This is not a zero-vulnerability claim or independent security audit. Same-user native malware remains outside this boundary. Store signing/identities and updates require owner-controlled release work.

## Verification — 2026-11-22

Windows 11 Home Single Language **10.0.26300 / build 26300**; Chrome for Testing **151.0.7922.34**, isolated profiles; generated VP8 video and public YouTube Big Buck Bunny (`aqz-KE-bpKQ`). Tests did not change the owner's profiles, saved preferences, startup registration or installed Morf.

| Check | Scope |
| --- | --- |
| .NET 10 product build | `dotnet build src/Widgates/Widgates.csproj -c Release -o artifacts/pip-net10`; zero warnings/errors. |
| Inbox product build | `scripts/Build-InboxPreview.ps1 -OutputDirectory artifacts/pip-inbox`; warnings treated as errors. |
| .NET 10 standard suite | **428 passed**. Settings/defaults, Spotify exclusions, exact metadata matching, bounds/type/token validation, malformed handshake recovery, real IPC, actual WPF artwork click and disabled cleanup. `artifacts/pip-net10-checks.log`. |
| Inbox standard suite | **429 passed**, same PiP coverage. `artifacts/pip-inbox-checks.log`. |
| .NET 10 native layout / settings | **26 / 58 passed**, separately run. Programmatic WPF geometry/menu scenarios; no saved preference or media-command changes. |
| Inbox native layout / settings | **26 / 58 passed**, separately run; process exit confirmed. |
| Offline extension checks | **11 passed**. `node --test tests/pip/extension.test.cjs`: permissions, no idle work, disabled rejection, tab/token/origin checks, seek bounds, SPA/navigation cleanup. |
| Actual host executables, each runtime | `tests/pip/native-host-check.cjs`: bidirectional framed traffic without WPF startup; oversize message rejection and process exit. No registry change. |
| Generated-video browser checks | **18 passed**, headed Chromium. Real Document PiP, playback, resize/layout, ten-second labels, stale-token rejection, live setting update, pause/restoration on close, return without pause and disconnect cleanup. Includes a local YouTube-shaped fixture invoking available original previous/next controller buttons and restoring the bounded player. Native Messaging simulated. |
| Loaded extension + live YouTube | Real extension action/activeTab/isolated-world injection, advancing live playback, ten-second forward seek, native-command focus of existing PiP, close/pause/restoration. Native Messaging simulated in the test copy, not shipped source. |

The native host and browser integration are tested separately. A store-installed extension connected to installed packaged Morf through registry registration is **unverified**. Physical keyboard shortcut/conflicts, corner dragging, DPI/multiple monitors, Task View listing/pinning and virtual-desktop transition continuity are **unverified**. No undocumented desktop COM interfaces are used.

Netflix/DRM, native-browser fallback, Firefox, Edge/Brave/Opera/Vivaldi device compatibility, captions and source-supported previous/next transitions are **unverified**. The selected live YouTube fixture did not expose a usable next-video action. Builds and functional checks are not CPU/GPU/power/battery benchmarks; disabled lifecycle is checked structurally and through IPC/process exit.

## Browser checks

### Real native transport regression — 2026-11-23 / package revision 17

The revision-16 program only recognized the diagnostic switch. A real browser's origin-first invocation therefore reached the ordinary single-instance activation path, opened/latch-pinned the capsule on playback, and exited without a native response. Earlier browser checks replaced Native Messaging and the separate host check supplied the diagnostic switch, so neither reproduced the actual invocation. `tests/pip/entrypoint-check.cjs` first failed against installed revision 16 (exit 0, no native output), then passed against both corrected builds and the exact revision-17 payload.

On Windows build **26300**, .NET SDK **10.0.403**, installed Brave **1.100.58 / Chromium 158.0.8127.42**:

- .NET 10 product build: passed with zero warnings/errors; **430 standard checks passed**.
- Inbox product build: passed; **431 standard checks passed**.
- Offline extension checks: **16 passed**.
- Exact origin-first native launches: **3 checks passed for each runtime and the packaged payload**, connected to the real running Morf listener. Unknown extension origin rejection and legacy diagnostic compatibility were checked separately.
- Installed Brave, original MV3 extension, real native executable, actual named pipe and running Morf: **9 checks passed** for a generated HTTPS fixture, and **9 passed on live YouTube** (`aqz-KE-bpKQ`). No Native Messaging stub/configuration replacement was used. Standard PiP opened from the trusted inline button, playback advanced, and close paused playback/released the port. The actual signed revision-17 payload repeated all **9 live-YouTube checks**.

The real-native browser wrapper uses a disposable Brave profile. It temporarily points the existing per-user native manifest to the tested repository payload and restores its exact original text in `finally`, unless changed independently. It does not modify the owner's Brave profile or browser policies. Tests use the running Morf bridge; installed-revision verification after restart remains a separate check. The signed MSIX was staged with deferred registration, and the staged EXE hash matched the tested package payload. Event 658 confirmed that revision 17 waits for the running revision 16 to exit normally. Settings/theme hashes were unchanged at staging.

Evidence: `artifacts/brave-entrypoint-before.log`, `brave-entrypoint-net10.log`, `brave-entrypoint-inbox.log`, `brave-entrypoint-package.log`, `brave-real-native.log`, `brave-real-native-youtube.log`, `brave-real-native-package-youtube.log`, `brave-fix-checks-net10.log`, `brave-fix-checks-inbox.log`, and `brave-update-0.3.3.17/status.json`.

The earlier physical-desktop/DPI/device gaps and revision-16 inbox settings-test shutdown/timing observations in [the wider QA report](QA-2026-11-23.md) remain open; this focused fix changes launch routing only.

Use an installed Playwright package and matching Chrome for Testing, outside product dependencies:

```powershell
$env:MORF_PLAYWRIGHT_MODULE = '<installed playwright module directory>'
$env:MORF_CHROMIUM_EXECUTABLE = '<Chrome for Testing chrome.exe>'
node tests/pip/browser-check.cjs
$env:MORF_HEADED = '1'
node tests/pip/loaded-extension-check.cjs
$env:MORF_PIP_LIVE_URL = 'https://www.youtube.com/watch?v=aqz-KE-bpKQ'
node tests/pip/loaded-extension-check.cjs
```

The loaded-extension check uses a test-only native stub and extension-debugging switch in an isolated fresh profile to invoke the actual extension action with CDP. These are not included in exported product assets. Artifacts stay under ignored `artifacts/`. Actual-host checks require `MORF_NATIVE_EXE`, `MORF_TEST_USER_SID` and `MORF_TEST_SESSION_ID`; they temporarily create the default PiP pipe and must not run while an installed Morf PiP listener is active.
