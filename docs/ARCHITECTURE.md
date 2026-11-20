# Architecture

Morf is a native Windows desktop application written in C#. The SDK and inbox builds share the sources in `src/Widgates`; the inbox build selects a few compatibility paths through `INBOX_PREVIEW`.

## Application lifecycle

`Program` checks platform support, acquires the single-instance entry point and creates `IslandWindow`. A second launch signals the current user's existing instance. The application runs at standard user privilege.

`PanelState` owns compact, expanded and persistent state. `IslandWindow` is split by responsibility: controls, lifecycle, clock, media interaction, notifications, shelf and volume. Settings and the audio mixer are separate windows created on demand. Opening settings suspends hover opening and restores the prior state on close.

Desktop placement follows Windows work areas, taskbar position, monitor changes and DPI events. Game/application rules can suppress the panel. System events trigger deferred layout recovery instead of recurring input polling.

## Media

`MediaController` connects to Windows Global System Media Transport Controls. It publishes the current source, metadata, artwork, playback capabilities and timeline. Source applications retain playback ownership.

`LatestMediaRequest`, version checks and operation deadlines prevent stale asynchronous results from overwriting a newer selection. `PlaybackPriority` and `PlaybackReturn` manage optional source handoff. Return is restricted to a source Morf previously paused, with fresh identity and playback-state checks.

The local progress display runs only while needed by a visible player. It does not query Windows on every tick. Title motion and short transitions stop when hidden, paused or restricted by reduced-motion/power preferences.

## Audio

`SystemVolume` and `PlayerVolume` use public Core Audio interfaces. `AudioDevices` enumerates output endpoints and session channels. `AudioMixerWatch` forwards change callbacks so the mixer can preserve slider identity, focus and scroll position during updates.

Per-application output assignment is delegated to Windows settings. `AudioMirror` optionally captures the selected playback output using WASAPI loopback and forwards samples to selected outputs through bounded queues. Capture runs only after an explicit start. It does not open a physical microphone or save audio files.

## File shelf

`FileShelf` stores up to 24 local paths in memory, including ordering, temporary groups and undo state. Originals are not moved or renamed. Dragging out requests a copy; clipboard writes require the user's copy action.

`LocalFileAccess` validates paths on entry and before use. `PreviewImage`, `ShelfThumbnail` and `MediaArtworkCache` bound image decoding and cache reusable thumbnails. The shelf is not a persistent storage service.

## Notifications

`AppNotificationListener` uses Windows notification access when the app has package identity and permission. It coalesces events and bounds reads and pending content. `AppMessagePeek` owns presentation deadlines and the in-memory queue.

Notifications use a compact capsule while the hub is closed and a bounded inline row while it is expanded. Matched music notifications are consumed when opening the player instead of replacing it. `MediaNotification` matches source identities and track text before using the current media artwork, artist and album; the bound track identity rejects later stale matches. Application names remain available for accessibility, filtering and activation without being repeated in visible headings. Private notification mode hides content artwork as well as text.

`NotificationAppActivation` resolves validated identities inside Windows AppsFolder and invokes the registered open action on an explicit STA worker. This supports desktop AUMIDs as well as packaged apps without treating notification text as executable paths. A click opens the source application; the listener does not provide its original private toast-action payload for deep-linking to a conversation or document.

`WhatsAppNotificationActivation` limits fallback to explicit WhatsApp application identities and keeps the stable and Beta channels separate. Missing registered-app opening can use Windows package activation for those fixed identities. Windows denial/cancellation exceptions stop the attempt. Notification titles and bodies never become phone numbers, protocol URLs or activation arguments; no WhatsApp contact database or account session is accessed.

`NotificationBannerRouting` and `NotificationBannerRegistry` temporarily manage eligible current-user Windows banner preferences. A journal records restoration data. `NotificationBannerGuard` runs as a second process from the same EXE while needed; it waits for the parent and can restore preferences after an unexpected parent exit. This is not a universal pre-delivery interception API. Windows-version dependencies and recovery limits are documented in [privacy](PRIVACY.md).

## Settings and assets

`WidgetSettings` stores preferences under `%LOCALAPPDATA%\Morf`. `AtomicTextFile` bounds reads and handles atomic writes/recovery. `ThemePalette` supplies shared brushes; `PopupTheme` styles native WPF controls. Embedded branding and simple vector icons avoid runtime asset downloads.

There is no embedded browser, web UI or application telemetry service. External application launches use known targets or validated registered Windows identities; notification text is never treated as an executable path.
