# ADR-0010: Measured speed — where the numbers come from, and the undocumented sources we accept

- **Date:** 2026-10-26
- **Status:** Accepted
- **Deciders:** Product Manager
- **Affected systems:** Startup & performance (Speed check, Startup and Performance tabs); the elevated helper's operation set; session journal

## Context

The goal for the optimization work is that a person *feels* their PC is faster after using Upkeep, and can see that it is. That rules out two easy paths. Placebo tweaks (RAM "boosters", turning off SysMain or Prefetch, network registry "tweaks") stay refused, as the README already says of registry cleaners. Promised savings that nothing measured would be the same kind of claim in a nicer shape.

What's left is changing the few things that really slow a Windows 11 PC down — what starts at sign-in, the power mode, apps allowed to run in the background — and measuring before and after with Windows' own numbers. Each of those numbers, and each of those levers, has to come from somewhere. Three of the sources are not documented by Microsoft, and this record exists so nobody has to rediscover what they are, or why we accepted them.

## Decision

### 1. Startup time and per-app delay: Windows' Diagnostics-Performance log, via the helper

Windows times every start in the `Microsoft-Windows-Diagnostics-Performance/Operational` event log:

- **Event 100** — one per start. Property 6 (`%6`, index 5) is the boot duration in milliseconds: from the start of Windows loading until the desktop has settled, which is the number Windows itself reports as the boot time. The record's `TimeCreated` dates it.
- **Event 101** — an app that made that start slower than usual. Properties: 3 file name (`OneDrive.exe`), 5 friendly name, 7 version, 8 total time in ms, 9 degradation time in ms.

Upkeep reads them with `EventLogReader` and takes the values **by position** from `EventRecord.Properties`, never from the rendered message, which is localized (on Polish Windows event 100 reads "System Windows został uruchomiony"). The positions were read from the provider's own metadata (`wevtutil gp Microsoft-Windows-Diagnostics-Performance /ge /gm`), which names them in its message strings.

The log is readable only by administrators. Reading it is therefore a new helper operation, `read-boot-performance`: no arguments, read-only, capped at the most recent starts. It widens what the elevated process *reads*, not what it changes — but it is still a change to the operation set, and CONTRIBUTING.md makes that blocking until a second reviewer has looked at it.

Windows logs event 101 only for apps that slowed a start down, so an app with no 101 is shown as **"Not timed"**, with a tooltip saying why. Upkeep does not estimate a time for it.

### 2. "Last started by you": UserAssist (undocumented)

To suggest which slow startup apps someone no longer uses, Upkeep reads the per-user **UserAssist** record Explorer keeps:

- Key: `HKCU\Software\Microsoft\Windows\CurrentVersion\Explorer\UserAssist\{GUID}\Count`, for `{CEBFF5CD-ACE2-4F4F-9178-9926F41749EA}` (executables) and `{F4E57C4B-2036-45F0-A9AB-443BCFE33D9F}` (shortcuts).
- Value names are ROT13-encoded paths. A path may begin with a known-folder GUID in braces (for example `{6D809377-6AFA-444B-8957-A3773F02200E}` for Program Files) instead of a drive letter, so Upkeep compares only the **file name** (`steam.exe`), never the full path.
- Value data is 72 bytes on Windows 7 and later: run count (Int32) at offset 4, focus count at 8, focus time in ms at 12, and the last run time as a `FILETIME` at offset 60. Anything shorter than 68 bytes, or a zero `FILETIME`, is ignored.

It records launches made through the shell: Start, the taskbar, Explorer, a shortcut. It does **not** record an app being started by Windows at sign-in, or reopened from its tray icon. An app someone uses every day through its tray icon can look unused. So this signal only **pre-selects** rows in a preview the person reviews and can untick. It never acts on its own, and the column is labelled "Last started by you", not "Last used".

### 3. Power mode: the documented power-mode API

Windows 11's power mode (Best power efficiency / Balanced / Best performance) is read and set with `PowerGetUserConfiguredACPowerMode`, `PowerGetUserConfiguredDCPowerMode` and their `Set` counterparts in `powrprof.dll` (`powersetting.h`). Plugged in and on battery are separate settings, as they are in Settings › System › Power. The modes are GUIDs: `961cc777-2547-4f9d-8174-7d86181b8a7a` (best power efficiency), `00000000-0000-0000-0000-000000000000` (balanced), `ded574b5-45a0-4f42-8737-46345c09c238` (best performance).

Windows applies power mode only while the **Balanced** power plan is active. When another plan is active, the section says so instead of offering a control that does nothing.

### 4. Background apps: the values Settings writes (undocumented)

Per-app background permission is read from and written to `HKCU\Software\Microsoft\Windows\CurrentVersion\BackgroundAccessApplications\<package family name>`, the same values Settings › Apps › Installed apps › Advanced options writes:

| Settings shows | Values |
|---|---|
| Never | `Disabled` = 1, `DisabledByUser` = 1 (DWORD) |
| Always | `Disabled` = 0, `DisabledByUser` = 0, `IgnoreBatterySaver` = 1 |
| Power optimized (Windows' default) | none of the above present |

Upkeep offers on/off: off writes "Never", and on goes back to whatever the app had before Upkeep turned it off. Each change journals the exact previous values of `Disabled`, `DisabledByUser`, `IgnoreBatterySaver` and `SleepDisabled` (present or absent) *before* writing, and a revert restores exactly those. The list is limited to installed, non-framework packages that `PackageManager` can name. The other entries under the key belong to Windows components with no name a person would recognise.

### 5. Not measured: memory at idle

Measuring memory "at idle" would mean Upkeep starting itself at sign-in, which is exactly what a startup tool should be talking people out of. And once Upkeep is open, the PC isn't idle. So there's no memory figure.

### 6. Savings are measured, not promised

Upkeep never adds up per-app times into an "it will save N seconds" figure, because apps start in parallel and the sum would overstate the gain. A saving is reported only once Windows has timed a start after the change. Speed check compares the average of the starts before the first change with the starts since, and says when that is a small sample.

## Options Considered

### Windows' own logs and settings, documented where possible, with undocumented ones written down (chosen)

- Pros: every number is one Windows produced; every lever is one Windows' own UI exposes and reads back; nothing is a guess.
- Cons: two undocumented registry formats (UserAssist, BackgroundAccessApplications) that a future Windows could change; one new helper operation.

### Task Manager's startup-impact data (`%SystemRoot%\System32\wdi\LogFiles\StartupInfo\*.xml`)

- Pros: has a figure for every startup app, not just the slow ones.
- Cons: undocumented, admin-only, and its "High/Medium/Low" mapping isn't published. Swapping one undocumented source for a less stable one buys coverage at the cost of more guessing. Rejected.

### Prefetch or SRUM for "last used"

- Pros: Prefetch counts every launch; SRUM records foreground time.
- Cons: Prefetch also counts the automatic launch at sign-in, so every startup app looks recently used — the opposite error. SRUM is an admin-only ESE database. Both are heavier than UserAssist and no more documented. Rejected.

### `PowerSetActiveOverlayScheme` for power mode

- Pros: works on older Windows 10 builds.
- Cons: undocumented, and it has no separate plugged-in and on-battery settings. The documented per-source API exists on every Windows 11 build Upkeep supports. Rejected.

### The `LetAppsRunInBackground` Group Policy for background apps

- Pros: documented.
- Cons: machine-wide and policy-enforced: it greys the setting out in Settings and needs the helper for an HKLM write. That's heavier-handed than the per-user switch Settings itself offers. Rejected.

## Consequences

- If a Windows update changes the UserAssist or BackgroundAccessApplications formats, the parsers should fail closed: unparseable data counts as "never started by you" (UserAssist), or the app isn't listed (background apps). They must never produce a wrong write. Tests pin the formats described above.
- Apps that started quickly enough not to be logged show "Not timed". That's honest but less complete than Task Manager.
- The helper's operation set grows by one read-only operation, which needs a second reviewer.

## Implementation Notes

- Parsing lives in Core and is tested: `BootPerformanceParser`, `UserAssistParser`, `BackgroundAccessValues`, `SpeedComparison`, `StartupRecommendation`. The OS reads (`EventLogReader`, `powrprof`, `PackageManager`) are thin adapters excluded from coverage.
- Startup items are matched to event 101 and UserAssist by the executable's file name, taken from the item's command. A Startup-folder shortcut has no command to read the file name from, so it shows "Not timed".

## References

- ADR-0005 (elevated helper — the operation set this widens)
- ADR-0006 (safety model — journaling before the change)
- `powersetting.h`: `PowerGetUserConfiguredACPowerMode` and related functions
- README — "What it deliberately doesn't do"
