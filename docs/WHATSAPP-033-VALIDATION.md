# WhatsApp opening assessment and validation — 0.3.3-preview

Date: 2026-11-20. Environment: Windows **26300.9974**, standard-user privilege; installed stable WhatsApp **2.2645.101.0** (`WhatsApp.Root.exe`).

## Direct-chat feasibility

The supported WhatsApp click-to-chat mechanism requires a known full international phone number. Morf's Windows notification listener receives display text and application identity, not a verified sender number, group/conversation ID or the original toast's activation arguments. `UserNotificationListener` exposes reading/removal APIs, not an API to invoke another application's original toast action. The installed WhatsApp manifest registers protocols, but their presence does not supply the missing conversation identity or document a notification replay contract.

Therefore reliable direct-chat opening is **not implemented** with the available supported interfaces and notification data. Display names, numbers in the message body and supplied URLs are not treated as trustworthy recipient identities. No contact/history database access, account linking, process-memory inspection, UI-driven chat search or undocumented notification-database access was added. A notification opens WhatsApp and the user selects the conversation there.

Sources checked:

- [WhatsApp: How to use click to chat](https://faq.whatsapp.com/5913398998672934)
- [Microsoft: UserNotificationListener methods](https://learn.microsoft.com/en-us/uwp/api/windows.ui.notifications.management.usernotificationlistener)
- [Microsoft: Notification exposes visual content and expiration](https://learn.microsoft.com/en-us/uwp/api/windows.ui.notifications.notification)

## Implemented opening behavior

- A registered WhatsApp opening request is used first. If it is unavailable, only explicit, compiled-in WhatsApp package identities can be passed to Windows package activation.
- Stable/legacy stable identities stay on stable WhatsApp. The current and former Beta identities can fall back within Beta; they do not open stable WhatsApp instead.
- No other application is treated as WhatsApp because of its display name. Prefix lookalikes, paths, protocol URLs and malformed GUID identity prefixes are excluded.
- Accepted opening requests are not retried. Explicit Windows access-denied or cancelled requests stop before another API is attempted. The existing busy guard and bounded launch deadline remain in place.
- No notification title, body, phone-like string or supplied URI becomes a launch argument. The settings text and accessibility help explain that WhatsApp opens at application level.

## Verification

| Check | .NET 10 | Inbox / .NET Framework 4.8 |
| --- | --- | --- |
| Release product build | Passed; 0 warnings/errors | Passed; warnings treated as errors |
| Standard checks | 395 passed | 395 passed |
| Real WhatsApp click-handler and package-fallback scenarios | 5 passed | 5 passed |
| Windows notification presentation | 18 passed | 18 passed |
| Notification lifecycle | 18 passed | 18 passed |
| Applications drawer | 9 passed | 9 passed |
| Panel lifecycle | 9 passed | 9 passed |
| Expansion and shelf/player regressions | 34 passed | 34 passed |

The WhatsApp scenario presents a synthetic Morf notification, invokes the same `OpenNotificationAsync` handler used by its click action, and checks the actual foreground process through Win32. Both runtimes brought **WhatsApp.Root** to the foreground. A second scenario forces the registered-app path unavailable and verifies the real exact-package fallback also brings WhatsApp forward. No message was sent, phone link followed, chat selected or conversation contents inspected.

The 11 added standard checks cover legacy identities, the current Beta identity, bounded channel-specific fallback, no retry after acceptance, denied/cancelled opening, untrusted labels/URIs, unrelated applications and malformed identifiers. Real Beta operation and physical clicking of an incoming live Windows WhatsApp toast remain unverified. The full optional native sequence was not claimed as passing.

## Desktop delivery

The signed friend archive was built with `scripts/Build-FriendSetup.ps1`. Its exact contents are `Kurulum.exe`, `Morf-0.3.3-x64.msix`, public-only `Morf.cer`, `KURULUM.txt`, and `SHA256.txt`. Both signatures, setup payload verification and all archived file hashes passed. The instructions explicitly describe application-level WhatsApp opening.

- Desktop `Morf-Kurulum-x64.zip`: SHA256 `B1A548B8936C7FDA01C86249ECB689222390605E53B24F5DC8FE1CD01DEC76D5`.
- Desktop `Morf-0.3.3-x64.msix`: SHA256 `12998D263213EE94C5C842AEC10413D87D6B36767C170C66F2104908C954CF8B`.

The older Desktop archive was backed up before replacement; the known 0.3.2 versioned archive was moved into the ignored local backup directory. No original user files or shelf contents were changed. The build/delivery scripts do not install the application or modify certificate trust. Clean-device installation, friend's Windows permission prompts, broader device behavior and antivirus acceptance remain unverified. This locally signed friend-test package is not a Store or publicly trusted release.
