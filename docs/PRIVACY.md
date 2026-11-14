# Privacy and Windows integration

Morf runs as the signed-in Windows user. It is a desktop application, not an AppContainer sandbox.

## Local data

| Data | Storage and lifetime |
| --- | --- |
| Preferences and theme | `%LOCALAPPDATA%\Morf\widget.settings` and `theme.txt` |
| Shelf paths, groups and undo | Memory only; cleared when Morf exits |
| Notification content and pending messages | Memory only; bounded and discarded after use |
| Playback metadata and decoded artwork | In-memory working state/cache |
| Notification-routing restoration journal | Current-user registry, while managing banner preferences |

Morf does not provide an account, cloud sync or analytics backend. Opening YouTube or another source launches that external application; its own network and privacy behavior is separate.

## Files and clipboard

The shelf accepts local files and directories. It rejects network/device paths, alternate data streams and paths containing reparse points. Junctions, symbolic links and some cloud placeholders are therefore unsupported.

Dropping a path does not copy or move the original. Dragging a shelf item out requests a copy. Clipboard writes occur only through the user's copy command. Dropped files are not executed.

Image decoding is bounded by file size and source pixel count, with small decoded previews. These checks reduce resource use; Windows image codecs and path-validation races remain part of the desktop application's threat model.

## Audio

Volume controls use Windows Core Audio. Multi-output playback is opt-in and uses loopback capture of a selected **playback output**. Morf has no physical-microphone recording feature, does not write audio to disk and does not transmit audio to a server.

Loopback still uses a Windows capture interface. Security software may report access to that interface as recording activity. A prior Kaspersky microphone-access prompt named the check executable; the exact triggering call was not reproduced. It was not proven to be a false positive. Do not grant the test tool recording access merely to use basic media controls.

## Notification permission

Live notifications require the MSIX package and explicit Windows notification access. This is a broad Windows permission; Morf filters display behavior using application preferences. The portable EXE can show synthetic examples but cannot subscribe to live notifications.

Notification text is not logged, uploaded or persisted. Clicking an eligible notification opens a known registered application, without executing message text or attempting to send a reply. Morf does not mark messages as read or supply call controls.

## Windows banner preferences

The packaged integration can temporarily suppress eligible Windows banners while Morf presents notifications. It records previous values and restores them when disabled, suppressed or closed. Protected system/security senders and explicitly disabled notification-center delivery are excluded.

This uses a version-dependent current-user Windows settings schema, not a stable public banner-interception API. The journal and event-driven guard reduce recovery risk. They cannot guarantee recovery after simultaneous termination of the entire process tree, power loss, uninstall or an unsupported Windows schema change. New-source notification timing can also produce a duplicate banner.

The relevant registry scopes are:

```text
HKCU\Software\Microsoft\Windows\CurrentVersion\Notifications\Settings
HKCU\Software\Morf\NotificationRouting
```

## Startup and permissions

Windows startup is off by default. An explicit settings action manages Morf's own current-user startup value. Launching the application does not silently recreate or move the startup registration.

The build and package scripts do not request administrator access for normal application use, change antivirus settings or change certificate trust. A release must complete the signing and antivirus work recorded in [release readiness](RELEASING.md).
