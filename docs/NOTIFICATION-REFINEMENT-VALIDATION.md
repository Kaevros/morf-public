# Notification refinement validation — 0.3.1-preview

Date: 2026-11-16. Environment: Windows build **26300.9974**, standard user.

## Resulting behavior

- Track changes update the expanded player without replacing it with a compact notification. Manually opening the player consumes matched music previews and stops their pending dismissal clocks.
- Other applications' notifications use a bounded row below the expanded player. The player remains visible, retains its selected state and continues its local progress clock while playback is available. Closed hubs retain the compact notification capsule.
- Visible headings omit Morf's added application-name prefix. Empty bodies remain empty. Application identity is retained for accessibility, preferences and activation.
- A source/title/artist match permits the current media cover, artist and album. A notification bound to an earlier track cannot acquire a later track's artwork. Missing artwork falls back to the application icon.
- Rapid matched media updates replace the current media preview without building a queue of obsolete songs. Consuming a music preview preserves pending notifications from other applications.
- Private content mode hides metadata and artwork and uses neutral text. WhatsApp sender photos are unavailable through the current documented listener API; its application icon remains the fallback.

## Builds and automated checks

Final product sources were built with `scripts/Build-Test-Package.ps1` without its optional full native sequence. Native modes below were run separately. Build/package logs are in the ignored `artifacts/notification-refinement/` and `artifacts/lifecycle-checks/` directories.

| Check | .NET 10 | Inbox / .NET Framework 4.8 |
| --- | --- | --- |
| Release product build | Passed; 0 warnings, 0 errors | Passed; warnings treated as errors |
| Standard checks | 381 passed | 381 passed |
| Windows notification presentation | 18 passed | 18 passed |
| Notification lifecycle and inline layout | 18 passed | 18 passed |
| Expansion | 24 passed | 24 passed |
| Floating island | 13 passed | 13 passed on exact retry; see below |
| Display recovery | 58 passed | 58 passed |
| Idle clock | 16 passed | 16 passed |
| Panel lifecycle | 9 passed | 9 passed |

Notification checks cover matching artwork and album enrichment, privacy, stale same-title artwork, manual opening during a media preview, an unrelated screenshot notification inside the open hub, duplicate media previews, and passing the old preview deadline with the hub still open. Long text, theme changes, upper/lower docking, dismissal, real dispatcher expiry and suppression are also exercised.

The first broad `-NativeChecks` attempt reached the previously documented Inbox settings-window shutdown hang after its assertions. Only that invocation's verified test process was stopped. That full sequence is **not a passing result**. The relevant native modes above then ran separately. One Inbox floating-mode attempt failed the bottom-edge source-popup boundary assertion at 240 DIP; the exact unchanged retry passed all 13 checks. The initial failure's cause was not established.

## Visual inspection

Actual WPF renderings were inspected for the dark music capsule, an expanded hub with an inline screenshot notification, a short dark notification and a long light notification. Text alignment, artwork area, missing application-name prefix, wrapping and the retained player were visible. The music artwork used in the dedicated presentation test was a synthetic image; the toast examples were synthetic.

The native geometry checks also observed an Apple Music Windows session without issuing media commands. This does not establish real Apple Music toast delivery or actual track-change behavior in the installed MSIX.

## Packages

- Portable: `artifacts/share/Morf-0.3.1-preview-Windows.zip`. Its four-file allowlist and archived EXE hash were verified by the packaging script. EXE SHA256: `4E04E768A78399B911665D95D7E01486EF131A050A8EF4ED27336514B8DA8865`.
- MSIX: `artifacts/notification-refinement/msix/Morf-Notifications-0.3.1-preview-x64.msix`. MakeAppx validation passed. Manifest and payload file versions are `0.3.1.0`. MSIX SHA256: `50D1A97702CBA14DBBD3096CF84FDE4A4DE0654C26269174F5A024EED63F8C8B`.
- The MSIX is **unsigned**. No certificate trust, installation, saved preferences or running owner application was changed. The portable build cannot receive live Windows notifications.

## Manual scenarios still unverified

| Application / environment | Scenario | Status |
| --- | --- | --- |
| Apple Music | Real Windows toast; next/previous from the hub and from the music app; actual cover matching | Unverified on the new MSIX |
| Spotify | Real Windows toast; rapid track changes; desktop/package identity behavior | Unverified on the new MSIX |
| WhatsApp | Real person/group notification, emoji and application-icon fallback | Unverified on the new MSIX |
| Snipping Tool | Actual screenshot toast with the hub open and closed | Unverified on the new MSIX |
| Devices / accessibility | Physical multi-monitor and multiple-DPI transitions, real high contrast and reduced-motion settings | Unverified; synthetic checks do not replace these scenarios |
| Packaging | Signed installation/update, permission denial/revocation and installed-package notification routing | Unverified; no installation was performed |

No public binary release, antivirus clearance, performance improvement or universal Windows compatibility is claimed by these results.
