# Windows startup maintenance — 2026-11-28

## Cause and change

On Windows **26300.9974 / 26H2**, the installed MSIX package read a virtualized `WindowsWidgates.Morf` Run value pointing at package revision 17. The real current-user Windows Run key had no corresponding value. Updating the package changed its absolute executable path, so the next launch also reconciled the saved startup preference to off. This did not constitute working Windows logon startup.

Packaged builds now declare the native `MorfStartup` Windows startup task with `Enabled="false"`. An explicit startup setting enables/disables it through `Windows.ApplicationModel.StartupTask`; launch and settings refresh only read its state. Requests are asynchronous, bounded to five seconds, and respect user/policy-disabled states. Reset disables the same native task. Portable builds retain their existing user-controlled Run registration. No broad registry-virtualization exception, recurring timer or startup repair on launch was added.

See Microsoft's [StartupTask reference](https://learn.microsoft.com/en-us/uwp/api/windows.applicationmodel.startuptask) for packaged desktop startup and Windows control of enabled state.

## Actual verification

.NET SDK **10.0.403**, standard user, Windows build above; **no media application or playback commands** in startup tests.

| Scenario | Result |
| --- | --- |
| Package regression before fix | `tests/Check-StartupPackage.ps1` failed against revision 19: no native startup task declared. |
| .NET 10 Release build | Passed, zero warnings/errors. `artifacts/startup-net10-build.log`; final packaging rebuild also passed. |
| Inbox Release build | Passed separately, warnings treated as errors. |
| Final standard suites | **442 passed on each runtime** via `scripts/Build-Test-Package.ps1`; includes package-state policy/denial and pending settings-toggle scenarios. Logs under `artifacts/lifecycle-checks`. |
| Manifest after fix | Startup manifest check passed for revision 20: one `MorfStartup`, opt-in, current package executable, no version-specific path. |
| Real packaged Windows API | A temporary WPF helper reused the product's `StartupRegistration.cs` and `OperationDeadline.cs` under the installed package identity. Identity, initially disabled state, enable, repeated enable, disable, re-enable and enabled readback all passed. The user requested startup enabled; it remained enabled. `artifacts/pip-update-0.3.3.20/startup-native.json`. |
| Signed distribution | Setup and MSIX signatures, setup payload hashes and ZIP entry hashes passed for **0.3.3.20**. Desktop ZIP and MSIX matched the final artifacts. |
| Installed application | Registered **0.3.3.20**, status `Ok`; installed EXE matched the signed package payload; running processes used that package path. Preferences matched the intended PiP/startup settings after launch; theme hash unchanged. Exported extension assets matched current source. `artifacts/pip-update-0.3.3.20/installed-verification.json`. |

Actual sign-out/reboot/logon, physical Windows Startup settings interaction, other Windows builds and friend-machine installation remain **unverified**. The native API state and package configuration are verified; this report does not claim an observed reboot. The new PiP behavior retains the separate live-site/device limits in [its report](PICTURE-IN-PICTURE.md).
