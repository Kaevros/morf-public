# Changelog

## 0.3.3-preview maintenance — 2026-11-23

- Add the opt-in Chromium companion extension and bounded native messaging for browser-owned picture-in-picture. Standard video PiP is the default; the optional advanced player provides seeking, a timeline and source-supported controls.
- Route Chromium's actual origin-first native-host invocation before WPF and single-instance activation, preventing playback from incorrectly opening and latching the capsule.
- Guard YouTube watch-page metadata against recommendation preview artwork and retain exact linked browser/session checks.
- Keep the capsule usable while settings are open, clear stale swipe/click state, and limit virtual-desktop synchronization to relevant events and bounded transition retries.
- Simplify settings navigation, notification disclosures and local shortcut capture while retaining independent surface transparency preferences.
- Record real Brave/YouTube/native-transport verification and remaining device/runtime gaps in [the PiP report](docs/PICTURE-IN-PICTURE.md) and [the wider QA report](docs/QA-2026-11-23.md).

## 0.3.3-preview — 2026-11-20

- Make WhatsApp notification opening explicit and bounded: preserve the stable/Beta source, recognize the current Beta identity, and use exact package activation when registered-app opening is unavailable.
- Respect Windows access denial or cancellation without trying another launch API.
- Explain that a WhatsApp notification opens the application; selecting a specific chat remains inside WhatsApp. Notification text, names and phone-like strings are never converted into chat links or launch arguments.
- Add unit coverage for target selection, failed opening and untrusted identifiers, plus a native check of Morf's notification-click handler and the real WhatsApp window.

See [the WhatsApp assessment and validation](docs/WHATSAPP-033-VALIDATION.md). Direct conversation opening is not implemented because this integration does not expose a trusted conversation identifier or the original toast action.

## 0.3.2-preview — 2026-11-20

- Keep the complete media player, including transport and volume controls, above the file shelf. Adding files extends the panel below the existing player.
- Open registered desktop notification sources as well as packaged applications through Windows AppsFolder activation on an STA worker. Application paths and toast text remain excluded from launch targets.
- Apply themed scrollbars before notification content first overflows, including the Inbox build, while retaining native scrolling.
- Ignore late visibility/deactivation callbacks during shutdown instead of attempting to reopen a closing window.
- Add a reproducible friend-test ZIP with a signed `Kurulum.exe`, signed notification-capable MSIX, public certificate and installation instructions. Certificate trust and installation stay explicit Windows actions.

Validation and live-scenario limits are recorded in [the 0.3.2 report](docs/SHELF-NOTIFICATION-032-VALIDATION.md).

## 0.3.1-preview — 2026-11-16

- Keep the expanded hub open when the track changes. Opening the hub consumes the music preview and stops its old dismissal clock.
- Show other notifications inside the expanded hub while leaving the player visible. Continue displaying the compact capsule when the hub is closed.
- Remove repeated application names from visible notification titles and empty bodies, including Apple Music, Spotify, WhatsApp and Snipping Tool.
- Use matching Windows media artwork with the track title, artist and available album. Fall back to the application icon when artwork is unavailable.
- Reject stale artwork after source, track identity or album changes, and merge rapid matched music updates without discarding other applications' pending notifications.
- Hide notification text and artwork together when content privacy is enabled.
- Refine notification spacing, image size, long-text wrapping and corners in both themes.

WhatsApp sender photos remain unavailable through the current documented Windows notification-listener API; the application icon is used. Live notifications require the MSIX build and Windows permission. The portable preview does not receive live Windows notifications.

See [notification validation](docs/NOTIFICATION-REFINEMENT-VALIDATION.md) for automated results and outstanding live scenarios.
