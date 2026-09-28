# Changelog

All notable changes to GoGoGadget Hardware Monitor are documented here.

The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/),
and this project uses [Semantic Versioning](https://semver.org/). See
[CHANGES.md](CHANGES.md) for the one-time structural differences from
upstream LibreHardwareMonitor this fork started from.

## [Unreleased]

### Added
- Forked from LibreHardwareMonitor at commit `82bc3bd` (2026-09-13).
- Project rebranded to GoGoGadget Hardware Monitor (see [CHANGES.md](CHANGES.md)).
- `CONTRIBUTING.md`, `CODE_OF_CONDUCT.md`, issue/PR templates.
- Crash logging: unhandled exceptions (UI thread and fatal) now write a
  timestamped `GoGoGadgetHardwareMonitorCrash-*.log` next to the exe with
  the exception, stack trace, and basic OS/app info, instead of silently
  vanishing as they do upstream (`Utilities/CrashLogger.cs`).
- **Gadget sensor reordering (Phase 1).** Right-click a sensor row in the
  gadget itself (not the gadget-wide menu, the row under the cursor) to get
  "Move Up" / "Move Down", scoped to that sensor's own hardware block
  (e.g. CPU sensors only reorder among themselves). The arrangement is
  persisted per sensor (`gadget.order`) and survives a restart; a sensor
  newly added to an already-reordered block is appended after it instead
  of jumping to a SensorType-derived position.
- **Per-sensor bar color (Phase 2).** Right-click a sensor row → "Bar
  Color" → "Choose..." to set an explicit color for that sensor's bar
  (or its value text, for sensors that print a number instead of a bar),
  using the same picker as the gadget's font/background color. "Auto"
  clears the override. Persisted per sensor (`gadget.barColor`).
- **Gradient threshold coloring.** Right-click a sensor row → "Gradient
  Colors..." to set a threshold and a max for that sensor
  (`gadget.warnAt` / `gadget.critAt`). Below the threshold the bar/number
  is green; between the threshold and max it shifts smoothly green ->
  yellow -> red (quantized to 32 steps so the color cache stays bounded);
  at or above max it's solid red. A gradient always wins over a static
  "Bar Color" override when armed, so an alarm never gets silently
  masked by a decorative color choice. This is a continuous take on the
  originally-planned discrete good/warn/crit alarm coloring (see the plan
  artifact linked from CLAUDE.md) - same settings-key shape, different
  coloring model, chosen because it reads better at a glance for a
  constantly-moving sensor value.
- **Gradient coloring is on by default for Temperature and Load (%)
  sensors**, using a 50/90 threshold/max (`GradientDefaults.cs`) - no
  setup needed for the common case. Right-click → "Gradient Colors..." →
  "Clear" turns it off for one sensor even though its type has a
  default (`gadget.gradientOff`); "Save" with custom values always
  overrides the default regardless of sensor type.
- **Hardware-group reordering.** Right-click a hardware-name header row
  in the gadget (e.g. "CPU", "GPU", "Generic Memory") to get "Move Group
  Up" / "Move Group Down", reordering that whole block relative to the
  others - sensors within a block still only reorder among themselves
  (Phase 1), never across blocks. Persisted per hardware
  (`gadget.hardwareOrder`) and survives a restart, following the same
  anchor-at-the-end-of-existing-order rule as per-sensor reordering when
  a new hardware group first appears.
- **Bar track color no longer follows the gradient/bar-color by
  default.** The empty part of a sensor's bar used to be tinted the same
  resolved color as the filled part, which read as the whole bar glowing
  green/yellow/red instead of just the value indicator. It's now a
  neutral shade of the gadget's own Background Color by default
  (`GetBarTrackColor`) - right-click the gadget background → "Gradient
  Bar Background" to switch back to the old look
  (`sensorGadget.gradientBarBackground`).
- **Settings and logs moved out of the app folder into two subfolders**:
  `UserSettings\` for the settings file (every user-changed gadget/app
  setting - colors, order, thresholds, everything `PersistentSettings`
  stores) and `log\` for the CSV sensor log and crash logs
  (`AppPaths.cs`). A pre-existing settings file from an older build is
  moved into `UserSettings\` automatically the first time this version
  runs, so upgrading doesn't reset your setup.
- **"Display Name..."** on a sensor's right-click menu sets a display
  name used only by the gadget row (`gadget.displayName`) - separate
  from the main window's own "Rename", which is unchanged and still
  edits the sensor's real name everywhere (main window, CSV log, tray).
- **"Remove from Widget"** at the bottom of a sensor's right-click menu -
  same as unchecking "Show in Gadget" from the main window's tree,
  reachable without leaving the gadget.
- **Mini/Full gadget modes.** "Show in Gadget" split into "Add to Full
  Gadget" and "Add to Mini Gadget (also includes in full)" on a sensor's
  right-click menu in the main window - Mini membership always implies
  Full (`gadget.mini`, enforced by `SensorGadget.AddToMini`/`Remove`).
  Double-click the gadget itself (or use its "Mini Mode" checkbox) to
  switch which set it's showing; a hardware group with nothing to show
  in the current mode is skipped entirely rather than showing an empty
  header.
- **First-activation prompt.** The first time the gadget is ever shown,
  a dialog offers "Use Default Values" (a small starting selection - see
  below) or "Start From Scratch" (today's empty-gadget behavior). Only
  ever asked once (`sensorGadget.hasBeenConfigured`).
- **Editable default sensor selection**
  (`UserSettings\DefaultWidgetSelections.json`, `WidgetDefaultSelections.cs`).
  Each rule matches by hardware/sensor *type* and name rather than a
  literal (machine-specific) sensor Identifier, and carries a `Mode` of
  `"M"` (Mini, also included in Full) or `"F"` (Full only). Factory
  defaults: Mini = CPU total load, physical RAM load, and the busy % and
  used capacity % of whichever disk hosts the Windows drive
  (`WidgetDefaultsApplier` resolves the OS drive via
  `StorageDevice.Storage.Partitions` - LibreHardwareMonitorLib's own
  public API, no WMI needed); Full adds CPU temperature and, only if a
  GPU of that brand is present, that GPU's temperature, core usage %,
  and memory usage % (`"GPU Core"`/`"GPU Memory"` `SensorType.Load`,
  matched per-brand the same way GPU temperature already was - an
  integrated Intel GPU doesn't expose these as an aggregate `Load`
  sensor, so the Intel rules simply find nothing there and are
  skipped). A rule that matches nothing on this machine (no discrete
  GPU, etc.) is silently skipped. Network upload/download speed was
  briefly a Mini
  default (matching the connected adapter via
  `System.Net.NetworkInformation.NetworkInterface.OperationalStatus.Up`,
  excluding loopback/tunnel) but was dropped from the factory defaults
  before release - noisy for most users to have on by default; the
  `ConnectedNetworkOnly` rule matching still exists in
  `WidgetDefaultSelections.cs`/`WidgetDefaultsApplier` for anyone who
  wants to add it back via the editable JSON file.
- **Gadget scale multiplier.** Right-click the gadget background →
  "Scale" → 100/125/150/200% sizes the whole gadget - font, icons,
  margins, bar width - up or down together, on top of whichever Font
  Size preset is selected (`sensorGadget.ScaleMultiplier`).
- **Theme & Profile export/import.** Right-click the gadget background →
  "Theme / Profile" → "Export Theme..."/"Import Theme..."/
  "Export Profile..."/"Import Profile..." (`ThemeProfileManager.cs`).
  - A **Theme** (`*.lhmtheme.json`) is the visual-only subset of gadget
    settings - font size/color, background color, opacity, hardware
    names, gradient bar background, scale - shareable with anyone
    regardless of their hardware.
  - A **Profile** (`*.lhmprofile.json`) embeds a Theme plus every
    per-sensor and per-hardware gadget setting (order, bar color,
    gradient thresholds, display name, mini/full membership, hardware
    group order) - tied to this machine's specific sensors; importing
    onto different hardware just leaves the sensor-specific keys inert.
  - Import takes effect immediately for colors/opacity/scale/gradient-
    bar-background/hardware-names and for sensor order (both within a
    hardware block and between blocks) and per-sensor bar
    color/gradient/display name/mini membership on sensors already
    shown. A Profile that adds or removes which sensors are shown at all
    needs an app restart to pick that part up.
- **Value Display (Bar / % / Both).** Right-click a sensor row → "Value
  Display" to choose how Load, Control, Level, and Humidity sensors show
  their value - these used to always render as a bar with no number at
  all. "Bar" keeps that look; "%" shows just the number (in the same
  style/format other sensor types already use); "Both" layers the
  number on top of the bar, right-aligned at the same column "%" mode
  uses, with the bar drawn behind it. Persisted per sensor
  (`gadget.valueDisplay`), defaulting to "%" (number only) for every
  sensor of these types, so gadgets change appearance immediately on
  upgrading to this version rather than needing opt-in - originally
  defaulted to "Both", changed to "%" per user preference 2026-09-22,
  before this ever shipped in a tagged release. Drag-to-reorder and a skins
  picker (the rest of the originally planned Phase 4) were deliberately
  dropped from scope - see CLAUDE.md's Status section.
- **Per-sensor Text Color.** Right-click a sensor row → "Text Color" →
  "Choose..." to give that sensor's drawn number an explicit color,
  independent of "Bar Color"/gradient (which still controls the bar
  itself). "Auto" clears the override and falls back to whatever the
  bar resolves to, matching the previous behavior. Added specifically
  so a "Both"-mode number - now drawn on top of the bar rather than
  beside it - can stay legible against any bar color/gradient
  underneath it. Persisted per sensor (`gadget.textColor`).
- **Per-sensor Name Color.** Right-click a sensor row → "Name Color" →
  "Choose..." to give that sensor's drawn display name/label an
  explicit color, independent of "Bar Color"/"Text Color" (which
  control the bar and the drawn number). Unlike those two, "Choose..."
  and "Auto" each open a scope submenu - "This Sensor", "This Group"
  (every sensor under the same hardware block), "All Sensors" (every
  sensor in the gadget) - so a color picked once can be applied
  everywhere at once instead of one sensor at a time. Implemented as a
  one-time bulk write/clear of the existing per-sensor
  `gadget.nameColor` key across whichever sensors the chosen scope
  resolves to, not a new group-/gadget-level setting. Falls back to the
  gadget's normal font color when unset, matching the previous (only)
  behavior. User-requested 2026-09-22.
- **About box: relabeled the upstream link and added a fork link.**
  "Project Website" (which opens upstream LibreHardwareMonitor's GitHub
  page - unchanged) is now labeled "LibreHardwareMonitor (Upstream)" so
  it's unambiguous which project it points to, and a new
  "GoGoGadget Fork — What's Added" link opens this fork's own README
  section listing its gadget features - nothing in the running app
  previously pointed back at this fork's own repo. Prompted by an audit
  ahead of making the repo public, to make sure upstream stays clearly
  credited and the fork's own additions stay easy to find.
- **Startup Guide.** A short onboarding dialog (`StartupGuideDialog`)
  listing the gadget's fork-specific features - how to add a sensor to
  it, right-click for reordering/colors/alarms, double-click for
  Mini/Full, drag to resize, and the background menu's Theme/Profile
  export - shown once per launch. A "Show this guide on startup"
  checkbox controls whether it keeps appearing (`mainForm.showStartupGuide`,
  defaults to on); it's reachable any time afterward from Help → Startup
  Guide regardless of that setting. Skipped when starting minimized to
  the tray, since popping a modal dialog would defeat a quiet start.
  Separate from `FirstActivationDialog` (which only ever covers the
  one-time "what sensors go in the gadget" choice and can't be reopened).
  Built and tested locally (both targets, 61/61) - not yet visually
  reconfirmed.
- **Made Mini/Full defaults actually distinguishable.** Every default
  rule in `WidgetDefaultSelections.cs` that was in Mini was also in Full
  (Mini-implies-Full), and the Full-only rules (CPU/GPU temperature,
  GPU load/memory) all depend on hardware that isn't always present -
  so on a machine with no discrete GPU and no reported CPU temperature
  (e.g. some VMs), toggling Mini/Full showed identical content by
  default. Moved "Used Space" (drive % used) from Mini to Full-only, and
  added a broader board/Super I/O temperature rule (first available
  Temperature sensor under `HardwareType.SuperIO`, no name filter -
  board sensor names vary too much by vendor to name-match the way CPU
  Package's short list does) as a fallback alongside the existing
  CPU-specific one. Only affects a *fresh* `DefaultWidgetSelections.json`
  - an existing one on disk isn't touched, since the file is meant to be
  user-editable and is only ever regenerated if missing. Built and
  tested locally (both targets, 61/61) - not yet visually reconfirmed.
- **Fresh-install defaults changed: dark theme, gadget shown, hardware
  names split by mode.**
  - Main window theme now defaults to `"dark"` instead of `"auto"`
    (follow the OS's light/dark setting) - both the startup theme-apply
    and the Theme menu's checked-state sync had to move to this new
    default together, or a fresh install would run dark but show
    nothing checked in the Theme menu.
  - `_showGadget` now defaults to `true` (was `false`, hidden until the
    user opted in) - `UserOption.Changed`'s add-accessor invokes its
    handler immediately on subscription, so this correctly cascades
    into showing the gadget and triggering `FirstActivationDialog` on a
    fresh install, same as if the user had just checked "Show Gadget"
    themselves.
  - "Hardware Names" split from one `sensorGadget.Hardwarenames` setting
    shared by both modes into two independent ones,
    `sensorGadget.HardwarenamesFull` (defaults on) and
    `sensorGadget.HardwarenamesMini` (defaults off) - the single shared
    setting was one more way Mini and Full looked identical by default
    (see the Mini/Full defaults entry above for the sensor-selection
    half of the same problem). The "Hardware Names" checkbox is now
    manually resynced to whichever setting is currently active (on mode
    toggle and Theme/Profile import) instead of a `UserOption` owning it,
    since `UserOption` is wired to one fixed settings key for its whole
    lifetime. `ThemeProfileManager.ThemeKeys` updated to export both new
    keys in place of the old one.
  - **Gadget could end up hidden behind the main window/startup dialogs
    on a fresh install.** Making the gadget visible early in `MainForm`'s
    constructor puts it naturally front-most at that instant (nothing
    else from this app is on screen yet), but `Show()` and the modal
    `FirstActivationDialog`/`StartupGuideDialog` that follow it each
    become the active window in turn and can end up stacked in front of
    it by the time startup actually finishes - not something the
    previous default (`_showGadget` off) ever exposed, since the gadget
    was never shown at startup at all. Added `GadgetWindow.BringToFront`
    (exposed via `Gadget`) - a one-time, non-sticky `SetWindowPos(...,
    HWND_TOP, ...)` nudge, distinct from `AlwaysOnTop`'s `HWND_TOPMOST` -
    called last in `MainForm`'s constructor, after everything else above
    has already run.
  - Built and tested locally (both targets, 61/61) - not yet visually
    reconfirmed.
- **Context menu now names what it's about to modify.** Right-clicking a
  sensor row shows "Device: <hardware.Name>" and "Sensor: <sensor.Name>"
  (both grayed-out, unclickable) at the very top, above Move Up/Down and
  everything else; right-clicking a hardware header row shows just the
  device line. Deliberately the sensor's/hardware's *real* name
  (`ISensor.Name`/`IHardware.Name`), never `ResolveSensorDisplayName`'s
  gadget-only override, so it stays useful as a "what is this actually"
  readout even after a custom Display Name no longer resembles it.
  User-requested 2026-09-28, to make it obvious what's being edited
  before clicking further into Bar/Text/Name Color, Gradient, Value
  Display, etc. Built and tested locally (both targets, 61/61) - not yet
  visually reconfirmed.
- **Dynamic gradient background instead of a stretched skin image.** The
  gadget's background skin (`Resources/gadget.png`) has a subtle baked-in
  lighter-top/darker-bottom "glass panel" shade in its 9-slice-stretchable
  middle region. Stretched only a little it looks fine, but once the
  gadget is resized far taller than the skin's native ~130px (via the
  fork's vertical drag-to-resize) the fixed-resolution source region gets
  stretched into a visible hard-edged band instead of a smooth fade -
  reported 2026-09-28 as visible banding in Mini mode. `SensorGadget`'s
  background draw (`DrawBackgroundImage`, a background-only sibling of
  the shared `DrawImageWidthBorder` used for the `_fore` overlay) now
  fills the middle region with a `LinearGradientBrush` instead of a
  stretched image; its two endpoint colors are sampled from the actual
  current background image (`_backTinted ?? _back`), so a custom
  Background Color/tint still comes through, and the gradient stays
  mathematically smooth at any window height instead of running out of
  source pixels. Built and tested locally (both targets, 61/61) - not yet
  visually reconfirmed.
- **Row Icons.** Gadget background right-click menu -> "Row Icons": Off /
  "Icons + Name" / "Icons Only". When on, the *first* sensor row of each
  hardware group is marked with its hardware-type icon
  (`HardwareTypeImage` - the same icon the group's own header row already
  shows when Hardware Names is on); every other row in the group is left
  as-is. "First" is positional (`isFirstSensorInGroup` in `OnPaint`), not
  tied to a specific sensor, so it stays on whichever sensor Move Up/Move
  Down has sorted to the top of the group instead of sticking to whichever
  one happened to be first originally. "Icons Only" drops that one row's
  text name in favor of the icon, and - per explicit user choice - every
  other row in the group is left with no name and no icon at all, not
  just the first (rows past the first aren't meant to be individually
  identified in this mode). Gadget-wide (`sensorGadget.RowIcons`), off by
  default, included in the Theme-export allowlist
  (`ThemeProfileManager.ThemeKeys`) since it's pure visual style.
  User-requested 2026-09-28 - initially shipped as an icon on every row
  (device icon + a sensor-category icon), narrowed the same day per
  follow-up feedback to just the device icon, once per group. Every row's
  name column is indented by the same icon-width gap whether or not that
  particular row draws an icon, so names stay aligned down the whole list
  instead of only the icon row's name being pushed over. The icon's
  size/position went through four attempts the same day, the last three
  still visibly wrong per user screenshots: centering the header row's
  icon size (`_iconSize`, 1.5x font size - sized for the taller
  `_hardwareLineHeight`) against the full row+spacing slot, then against
  `_sensorLineHeight` alone, then against just the font's ascent
  (`FontFamily.GetCellAscent`) - all three still centered a fixed
  `_iconSize`-sized icon, and `_iconSize` (~1.5x scaledFontSize) turned
  out to be nearly as tall as `_sensorLineHeight` itself (~1.55x) -
  diagnostic logging (`DebugLog` tag "RowIcons", added on the third
  attempt) confirmed this directly: the computed Y went negative for the
  first row, meaning the icon had nowhere to fit without clipping no
  matter which point it centered on. The actual fix: a dedicated,
  smaller row-icon size derived from the ascent itself
  (`ComputeRowIconSize`/`ComputeRowIconLayout`) instead of reusing
  `_iconSize` (which stays used for the header row, unaffected) - sized
  and centered on the same ascent band, so it actually fits the tighter
  sensor row with room to center. Built and tested locally (both targets,
  62/62) - not yet visually reconfirmed.
- **Group Spacing.** Gadget right-click menu -> "Group Spacing..." opens a
  small dialog to set an optional extra gap, in raw pixels, drawn between
  each hardware group (CPU, RAM, Storage, ...) - on top of normal row
  spacing. 0 (the default) means no change from before. Independent of
  the existing vertical drag-to-resize feature's per-row spacing
  (`extraPerStep`, which stretches every row/header step evenly): this is
  a fixed amount added only at each group boundary, applied whether or
  not Hardware Names is on, so groups stay visually separated even in the
  flat/no-headers look. Gadget-wide (`sensorGadget.GroupSpacingExtra`),
  deliberately excluded from the Theme-export allowlist
  (`ThemeProfileManager.ThemeKeys`) - same reasoning as
  `lineSpacingExtra`/`widthConfigured`: a raw pixel count tuned to this
  gadget's current size/scale, not a proportional style choice that would
  still look right after being copied onto a differently sized/scaled
  gadget. New `GroupSpacingDialog.cs`, modeled on
  `GradientThresholdDialog`/`DisplayNameDialog`. User-requested
  2026-09-28. Built and tested locally (both targets, 62/62) - not yet
  visually reconfirmed.

### Changed
- **Right-click menu redesign.** The gadget-wide section (always present,
  below the per-sensor block when a sensor row was right-clicked) used to
  be one flat, ungrouped list in the order features happened to ship in -
  Hardware Names, Font Size, Scale, Row Icons, Font Color, Background
  Color, Gradient Bar Background, Mini Mode, then window-behavior/theme
  items, with "Hide/Show Main Window" stranded at the very bottom.
  Reorganized into labeled groups, separated visually: "Hide/Show Main
  Window" first (a navigation action, not a gadget setting, moved to the
  top per user feedback so it's not lost at the bottom of a long menu) -
  Hardware Names + Row Icons together (both control what identifies a
  row/group - the specific grouping the user asked for) - Font Size +
  Scale + Font Color (text/icon rendering) - Background Color + Gradient
  Bar Background (background/bar appearance) - Mini Mode - Lock Position
  and Size + Always on Top + Opacity (window behavior, previously split
  across three separate single-item sections) - Theme / Profile. The
  per-sensor block (Device/Sensor header, Move Up/Down, colors, content,
  Remove from Widget) similarly gained two internal separators (after
  Move Down, and before Remove from Widget) instead of running all 9
  items together with no breaks. Note "Display Name..." (per-sensor)
  can't be grouped next to Hardware Names/Row Icons (gadget-wide) since
  it needs a specific right-clicked sensor and only ever appears in the
  per-sensor block. User-requested 2026-09-28. Built and tested locally
  (both targets, 61/61) - not yet visually reconfirmed.

### Fixed
- Two dialogs (`GradientThresholdDialog`, `DisplayNameDialog`) had a
  fixed-height instruction label that could overflow onto the input
  field below it if the text wrapped to more lines than expected (e.g.
  at a larger system font size) - the label now sizes itself to its
  actual wrapped height and everything below it is laid out relative to
  that instead of a fixed guess.
- Autostart Registry/Scheduled-Task entry and CSV log filename no longer
  collide with a real LibreHardwareMonitor install on the same machine
  (see [CHANGES.md](CHANGES.md)).
- 12 files under `LibreHardwareMonitor.Windows.Forms/` were missing the
  required MPL-2.0 header (`LicenseHeaderTests` now passes with 0
  failures instead of 12).
- `GradientThresholdDialog`, `DisplayNameDialog`, and `FirstActivationDialog`
  (the "Use Default Values" prompt shown at gadget startup) computed
  their layout - instruction label wrapping, button positions,
  `ClientSize` - from `AutoSize` controls' `.Bottom`/`.Height` *before*
  adding those controls to the form. An unparented control resolves its
  preferred size against stale Font/DPI metrics, so on a real Windows
  machine (as opposed to the static/logic-only checks this fork's Linux
  session can run) the actual rendered layout came out taller than
  computed, clipping the bottom button row and cramping the instruction
  text - visually confirmed by the user 2026-09-17. All three now parent
  every control first and compute positions/`ClientSize` inside the
  form's `Load` handler instead.
- Double-clicking the gadget toggled Mini/Full *and* hid/showed the main
  window (two `MouseDoubleClick` handlers were both wired up). Removed
  the hide/show handler - double-click on the gadget now only flips
  Mini/Full; hiding/showing the main window is still reachable via its
  own tray icon double-click and "Hide/Show" menu items.
- The gadget started out behind other open windows even with
  "Always on Top" off: `GadgetWindow`'s constructor unconditionally sent
  the newly created window to the very bottom of the Z-order
  (`MoveToBottom`) before it was ever shown, so it appeared hidden
  behind whatever else was open the moment the gadget became visible.
  Removed that call - the window now keeps its natural (front-most,
  non-topmost) creation position; `AlwaysOnTop` and the show-desktop
  handler still move it to the top/bottom afterward exactly as before.
- The gadget's "Scale" setting (100/125/150/200%) grew the icons,
  margins, bar width, and window size, but not the actual text - fonts
  were created at the raw, unscaled font-size preset instead of also
  being multiplied by the scale ratio like everything else in
  `SetFontSize`, so picking a bigger Scale didn't make anything more
  readable. Separately, the gadget's default/auto-computed width used
  upstream's original ratio, sized to fit a bar *or* a number per row -
  not accounting for the wider of the two, which is what a sensor
  defaulting to "Both" display (Load-type sensors, see the Value
  Display entry above) actually needs - so the bar could render clipped
  past the right edge. Font creation now scales with the same factor as
  the rest of the layout, and the default-width formula measures the
  actual worst-case number ("100 %") against the bar's own width
  instead of guessing - reported by the user 2026-09-17, not yet
  visually reconfirmed fixed on Windows.
- Sensor and hardware names still truncated with "..." even after the
  fix above: the *name* side of `ComputeDefaultWidth`'s formula was a
  flat "6x font size" guess, nowhere near long real names like
  "Intel Core i7-7700K" or "CPU Package" - the window itself was simply
  too narrow, independent of the bar/number fix. It now measures the
  actual longest hardware/sensor name currently shown instead of
  guessing, so the auto-computed width (on construction, and whenever
  Scale/Font Size changes or a Theme is imported) always fits what's
  actually on screen - reported by the user 2026-09-17 via screenshot,
  not yet visually reconfirmed fixed on Windows.
- The width fix above still had no effect on an *existing* install: the
  gadget's width has always been persisted (`sensorGadget.Width`) on
  every resize, including the too-narrow ones the bugs above caused -
  and that persisted value was unconditionally reapplied at startup,
  silently overriding whatever the (now-fixed) auto-fit formula would
  have computed. A user's genuinely-chosen width (dragging the right
  edge) is now tracked separately (`sensorGadget.widthConfigured`, set
  only by a real drag - see `GadgetWindow.UserResized` and
  `SensorGadget._widthManuallySet`); until that's ever happened, the
  gadget keeps auto-fitting its width to whatever hardware/sensor names,
  Mini/Full selection, etc. are actually on screen, on every resize
  trigger, instead of reloading a stale persisted value once at startup
  - self-healing existing installs that already have a too-narrow
  `sensorGadget.Width` saved from before this fix, with no settings
  reset needed. Reported by the user 2026-09-18 after rebuilding with
  the previous width fix and still seeing it squished; not yet visually
  reconfirmed fixed on Windows.
- **Fixed gadget window never growing past its initial ~195x95 size on high-DPI scaled displays (e.g. Windows 11 @ 150%).**
  The original implementation attempted to resize the layered window by calling `UpdateLayeredWindow` with a NULL source HDC and vetoed `WM_WINDOWPOSCHANGING` events via `SWP_NOSIZE | SWP_NOMOVE` to suppress default non-client updates.
  However, on modern scaled Windows environments, DWM (Desktop Window Manager) virtualization relies on standard window positioning paths (`SetWindowPos` / `DefWindowProc`) to allocate and size the compositor backing store for `WS_EX_LAYERED` windows. Vetoing the messages and bypassing `base.WndProc` caused DWM to continue clipping the window output to its initial compositing size, even though Win32-level bounds checks reported success inside our process.
  We updated the programmatic `Size` setter to use `SetWindowPos` to resize the window's physical bounds, and refactored the `WM_WINDOWPOSCHANGING` handler to let position/size updates pass to `base.WndProc` instead of being vetoed. This ensures the compositor backing store remains in sync with the layered bitmap's dimensions, fixing both programmatic resize clipping (at startup as sensors are loaded) and manual left/right-edge drag clipping. Confirmed fixed by the user 2026-09-18 (screenshot of a correctly-sized, non-clipped gadget on Windows 11 @ 150%).
- **Row spacing bumped (`_hardwareLineHeight`/`_sensorLineHeight` ratios
  1.66x/1.33x -> 1.9x/1.55x of scaled font size).** Once the resize fix
  above let PerMonitorV2 render text at its true size instead of being
  silently corrected by DPI virtualization's bitmap stretch, upstream's
  original ratios read as visibly cramped (hardware name touching the
  sensor row below it, sensor rows touching each other) - reported by
  the user via screenshot 2026-09-18, not yet reconfirmed at the new
  ratios.
- **Vertical drag-to-resize.** Dragging the gadget's top or bottom edge
  (mirroring the existing left/right-edge width drag) now resizes it
  vertically - but changes line spacing, not font/icon/bar size: the
  extra or reduced height is distributed evenly across every row-advance
  step (each hardware header and each sensor row,
  `SensorGadget.ComputeContentHeight`), so text and bars stay their
  normal size and only the gaps between rows grow or shrink. The chosen
  density is persisted (`sensorGadget.lineSpacingConfigured`/
  `lineSpacingExtra`, mirroring `widthConfigured`/`Width`) and survives
  a later sensor add/remove the same way a dragged width does, instead
  of snapping back to the natural auto-fit spacing.
  `GadgetWindow.UserResized` now carries the size just before the
  change (was a plain `EventHandler`) so a subscriber can tell a
  width-only drag apart from a height-only one.
  User reported vertical drag resizing up to a point then cropping
  (visually similar to the pre-fix horizontal clipping bug), with the
  point differing between Mini and Full mode. One plausible mechanism:
  `OnPaint`'s `catch (ArgumentException) { // #1425 }` wraps the entire
  paint body (inherited from upstream) and was completely silent - if
  anything throws partway through the row loop, the rest of that
  frame's drawing is skipped, which would look exactly like cropping
  even though the window itself is the right size, and since row count
  differs between Mini/Full a height-dependent trigger would naturally
  show up at a different point in each mode. Logged the caught
  exception (message + stack trace, `[Resize]` tag) instead of
  swallowing it silently, so the next repro's log confirms or rules
  this out - not a fix yet, a diagnostic.
  Got a code review from Gemini on the vertical-drag-to-resize commits
  (`from_gemini_to_claude.md`/`from_claude_to_gemini.md`) that found the
  actual mechanism ahead of the next repro - implemented:
  - **Event order + double redraw (Gemini Finding 1).** `SizeChanged`
    fired before `UserResized`, so `SensorGadget`'s `SizeChanged`
    handler (which already calls `Redraw()`) painted a frame with the
    *previous* `_lineSpacingExtra` before `UserResized` recomputed it
    and called `Redraw()` a second time - two full repaints per drag
    tick, the first visibly wrong. Swapped `GadgetWindow`'s invocation
    order and removed the now-redundant `Redraw()` inside
    `SensorGadget`'s `UserResized` handler.
  - **Discrete step quantization vs. continuous drag height (Gemini
    Finding 2) - the actual cropping mechanism.** `Size.Height` tracked
    the raw cursor position every drag tick, but `_lineSpacingExtra` is
    a whole number of pixels per row; the two almost never matched
    exactly, so the window and the rendered content height disagreed by
    up to `stepCount / 2` px throughout the drag (worse as it
    continued), snapping abruptly whenever a later `Resize()` (sensor
    add/remove, Mini/Full toggle, restart) recomputed height from
    content. Added a `Func<int, int> SnapHeight` hook on `GadgetWindow`
    (exposed via `Gadget`, mirroring `HitTest`'s per-subclass-behavior
    pattern but returning a value instead of a multicast notification)
    that `WM_WINDOWPOSCHANGING` calls on `wp.cy` before anything
    downstream sees it, wired to a new
    `SensorGadget.SnapHeightToRowSpacing` that snaps to the nearest
    whole-row-step height - re-anchoring the bottom edge on a top-edge
    drag (`wp.y = (wp.y + wp.cy) - snappedCy`) so snapping `cy` doesn't
    also silently shift the window. `WINDOWPOS.cx`/`cy` had to become
    writable (were `readonly`) to support this.
  - **Constructor read order (Gemini Finding 3).** `_lineSpacingManuallySet`/
    `_lineSpacingExtra` (and `_widthManuallySet`) are now read *before*
    `SetFontSize`, not after - `SetFontSize` calls `Resize()`
    immediately, so reading them after meant the placeholder ("no
    sensors yet") gadget briefly used natural (un-spaced) height until
    the first `Add()`/`Remove()` recomputed it for real.
  - **Rounding (Gemini Finding 5).** `MidpointRounding.AwayFromZero`
    instead of the default `ToEven`, so `SnapHeightToRowSpacing` and the
    `UserResized` persist step can't disagree at an exact half-step
    boundary.
  - **Dynamic re-clamping (Gemini Finding 6).** `_lineSpacingExtra` is
    now re-clamped against the *live* `_sensorLineHeight` everywhere
    it's consumed (`ComputeContentHeight`, `OnPaint`'s parallel
    computation), not just where it was originally dragged - a smaller
    font size or Scale since the drag means a smaller row height, and a
    stale negative extra could otherwise overlap rows that got shorter
    without ever being reclamped to fit.
  - **Deferred (Gemini Finding 4)**: gating `Redraw()`'s
    `SetWindowPos` call during a drag, on the grounds that horizontal
    (left/right-edge) dragging already goes through the identical
    reentrant path and was already confirmed working cleanly by the
    user after the DWM backing-store fix - Findings 1+2 are a
    sufficient, more direct explanation for the reported symptom, and
    gating it risks a position-drift regression that can't be verified
    without a Windows machine. Open to revisiting if artifacts persist.
  Not yet visually reconfirmed by the user.
  **Round 3**: user reported the crop still occurring after Findings
  1/2/3/5/6 landed (`b4a83c1`), with a full drag-session log (~2,600
  lines, Full and Mini mode, heights from ~150px to ~850px, growing and
  shrinking, including fast multi-hundred-pixel drag ticks). Analysis of
  that log found the entire geometry chain (`WM_WINDOWPOSCHANGING` ->
  `SnapHeight` -> `UserResized` -> `Redraw` -> `UpdateLayeredWindow` ->
  `GetWindowRect`) internally consistent throughout - zero
  `ArgumentException`s caught (the round-2 diagnostic never fired), zero
  failed native calls, and the requested/actual window size matched to
  the pixel on every single frame in both modes. This rules out the
  geometry layer as the round-3 cause and narrows it to the one
  remaining unverified layer: paint content itself, i.e. whether the
  row-drawing loop in `OnPaint` ever draws past the bottom of the buffer
  it's given despite the buffer being the right size. Added a diagnostic
  logging the row loop's final content-bottom `y` (+ `_bottomMargin`)
  against `Size.Height` on every paint with sensors shown, plus the
  inputs that feed it (`extraPerStep`, `_sensorLineHeight`,
  `_hardwareLineHeight`) - not a fix, a way to catch a content/buffer
  divergence directly if the round-3 repro recurs. Asked the user for a
  screenshot and the specific height/mode where the crop appears, since
  log analysis alone found nothing this round. Flagged to Gemini
  (`from_claude_to_gemini.md`) for a look at `OnPaint`'s render loop
  specifically, which the round-1/2 review didn't cover (it reviewed the
  geometry-layer commits only).
  User's follow-up clarified the symptom precisely: the window's outer
  box itself stops growing past roughly the size it was when a mode
  (Mini/Full) was entered, while the row content keeps shifting downward
  as the drag continues - text visibly drifting past a box that's no
  longer getting any taller. That pointed at a layer none of rounds 1-3
  had ever looked at: `GadgetWindow` is a plain `NativeWindow` created
  with `CreateParams.Style` left at its default (0) - no
  `WS_THICKFRAME`, no `WS_CAPTION`. All resizing is done manually via the
  `WM_NCHITTEST` `HitResult.Top`/`Bottom`/`Left`/`Right` override plus
  the OS's native interactive NC-drag loop, not a standard sizable-window
  style - and `WM_GETMINMAXINFO` was never intercepted. Windows' own
  default `ptMinTrackSize`/`ptMaxTrackSize` computation for a style-less
  window like this one can be considerably more conservative than for an
  ordinary resizable window, and every previous round's
  `WM_WINDOWPOSCHANGING` logging only ever saw `wp.cy` values the
  interactive drag loop was already willing to propose *after* that
  invisible clamp - so it could never have shown up as a mismatch the
  way the round-1/2 bugs did. Added a `WM_GETMINMAXINFO` handler that
  logs the OS's proposed default track sizes (for confirmation) and then
  overrides both to generous values (`ptMinTrackSize`=50x50,
  `ptMaxTrackSize`=10000x10000) so the interactive resize loop is never
  constrained by anything but the window's own screen-edge/`SnapHeight`
  logic. Not yet confirmed by the user - next repro's log will show the
  OS's actual default values either way.
  **Round 4**: user re-tested with the `WM_GETMINMAXINFO` fix in place
  (another ~2,600-line log: multiple Mini/Full toggles, drags from
  width 50px to 1945px, height 56px to 1676px). The theory is disproven
  - Windows' own default `ptMaxTrackSize` was `(3866,2186)` throughout,
  a generous full-virtual-desktop-sized bound, never a small restrictive
  one, so there was nothing there to have fixed (the override is
  harmless and stays in place, but isn't the answer). More significantly,
  this log shows zero internal inconsistency anywhere - every
  `WM_WINDOWPOSCHANGING` check, buffer recreate, `OnPaint`
  content-bottom-vs-`Size.Height` check, and `GetWindowRect` readback
  matches exactly, every time, across the whole session. Confirmed
  directly with the user (not from logs) that the crop happens while the
  gadget is still clearly within their visible monitor, ruling out an
  off-screen/multi-monitor explanation too. At this point the geometry
  layer, the buffer/content-height layer, and the screen-position layer
  have all been individually verified clean - genuinely stuck without a
  further evidence-backed hypothesis. Flagged to Gemini for a fresh angle
  (`from_claude_to_gemini.md`) and asked the user for a screenshot at the
  moment of the crop, since log-based diagnosis has run out of leads.
  **Round 5**: user reported a decisive new clue while away from a build/
  test setup: after the crop appears mid-drag, toggling Mini/Full shows
  the gadget correctly at the size it was actually dragged to - but only
  once toggled, not live during the drag. Combined with a fresh ~2,600-
  line log from the exact session where this was observed (another
  clean bill of health internally - `_size`/`GetWindowRect`/paint content
  agree at every single tick, same as rounds 3-4), this pinpoints the
  bug to a layer none of the internal-state logging could ever have
  caught: what DWM actually composites to the screen, as opposed to what
  our own bookkeeping and Win32 readbacks say. The Mini/Full toggle
  (`GadgetWindow.Size` setter) always shows correctly because it calls
  `SetWindowPos` and `Redraw()`/`UpdateLayeredWindow` fully outside any
  `WM_WINDOWPOSCHANGING` handling; the live-drag path did not; it fired
  `UserResized`/`SizeChanged` - which synchronously call `Redraw()` ->
  `UpdateLayeredWindow` - *before* forwarding the message to
  `base.WndProc`/`DefWindowProc`, i.e. before the OS/DWM had actually
  committed the new geometry and (per the comment already on that
  forwarding call, and per the original high-DPI clipping fix earlier in
  this section, both of which name the same mechanism) resized the
  compositor backing store to match. Every one of our own checks
  (`_size`, `GetWindowRect`, paint content vs. `Size.Height`) would
  still agree with each other in that state, since they're all just
  reading back values we set ourselves - only the actual on-screen
  pixels would lag. Reordered `WM_WINDOWPOSCHANGING` so `UserResized`/
  `SizeChanged` (and `LocationChanged`) now fire *after*
  `base.WndProc(ref message)` instead of before - this revives Gemini's
  deferred Finding 4 (`from_gemini_to_claude.md`), but for a different
  reason than originally proposed (visible staleness during a live drag,
  not position drift from a nested `SetWindowPos`), so it's applied here
  independently rather than as the originally-deferred change. Built and
  tested locally on this Linux session (`dotnet build`/`test` both pass,
  60/60, both `net10.0-windows` and `net472` targets) - not yet visually
  reconfirmed by the user on Windows.
  **Round 5, continued**: user tested the fix live on the Win11 VM
  (built/tested natively there for the first time - see "Build & verify"
  section). No sign of the original resize-crop bug across screenshots
  spanning a Mini/Full toggle and a fast width drag - every screenshot's
  background box matched its content, and pixel-level inspection found
  no bar/text pixels past the drawn box edge. Did find a real, separate
  bug while comparing screenshots: a hardware group's full name (e.g.
  "AMD Ryzen 5 PRO 2400GE w/ Radeon Vega Graphics") was hard-clipped
  mid-character with no "..." when the gadget was narrower than the
  name needed - `OnPaint`'s header `DrawString` used `_stringFormat`
  (no `Trimming` set), unlike the per-sensor display name row right
  below it which already used `_trimStringFormat`
  (`StringTrimming.EllipsisCharacter`). Switched the header to
  `_trimStringFormat` too, so a too-long hardware name now ellipsizes
  the same way a too-long sensor name already did. Built and tested
  locally (both targets, 60/60) - not yet visually reconfirmed.
  **Round 6**: Gemini reviewed round 5's fix in depth
  (`from_gemini_to_claude.md`, "Root Cause Identified", referencing
  commits `b00418a`/`44e7caf` and a user repro report) and found the
  actual reason it didn't fully work: `WM_WINDOWPOSCHANGING` is sent
  *before* the OS applies the geometry change, and `base.WndProc`
  (`DefWindowProc`) during that message only validates/queries
  constraints (e.g. `WM_GETMINMAXINFO`) - it does not resize the window
  or let DWM reallocate its compositor backing store. That only happens
  once `WM_WINDOWPOSCHANGING` returns to the OS and it sends the
  separate `WM_WINDOWPOSCHANGED` message afterward. Round 5's fix fired
  events after `base.WndProc` but still *inside* `WM_WINDOWPOSCHANGING`
  - still too early for exactly this reason, which also explains why
  four-plus rounds of internal-state logging never caught it: `_size`,
  `GetWindowRect`, and paint content all agreed with each other the
  entire time, because they're all just reading back values this code
  set itself, before DWM had actually caught up on screen.
  `WM_WINDOWPOSCHANGING` now only computes geometry (screen-edge
  clamping, `SnapHeight` snapping/re-anchoring) and forwards it - it no
  longer touches `_size`/`_location` or fires
  `UserResized`/`SizeChanged`/`LocationChanged` at all. A new
  `WM_WINDOWPOSCHANGED` handler does that instead, guarded by
  `_inProgrammaticResize` (that flag's `SetWindowPos` call in the `Size`
  setter uses `SWP_NOSENDCHANGING`, which only suppresses
  `WM_WINDOWPOSCHANGING` - `WM_WINDOWPOSCHANGED` still fires for it, so
  without the guard a Mini/Full toggle or any other programmatic resize
  would double-fire events and double-redraw, the original Gemini
  Finding 1 all over again). Built and tested locally (both targets,
  60/60). Gemini also flagged `Redraw()`'s own trailing `SetWindowPos`
  call (position re-assertion) as a secondary reentrancy concern (the
  long-deferred Finding 4) - left as-is for this round since it's no
  longer being called from inside an in-flight
  `WM_WINDOWPOSCHANGING`/`WM_WINDOWPOSCHANGED` pair under the new
  architecture. **Confirmed fixed by the user on the Win11 VM
  2026-09-28** - live drag no longer crops. Closes the resize-crop
  investigation that ran across rounds 1-6.
- **Diagnostic logging extended to every other area with a documented
  history of hard-to-pin-down bugs** (same `DebugLog`/
  `GoGoGadgetDebug.log`, one `[area]` tag per subsystem, always-on, no
  settings toggle needed): `[ZOrder]` for `AlwaysOnTop`/`Visible`/
  show-desktop transitions (see the "started out behind other windows"
  fix above); `[MiniFull]` for the Mini/Full toggle and double-click
  handler (see the "double-click also hid the main window" fix above);
  `[DialogLayout]` for every dynamically-built dialog's computed
  `ClientSize`/control heights (`GradientThresholdDialog`,
  `DisplayNameDialog`, `FirstActivationDialog`, and the sensor-row color
  picker - see the "clipped buttons/cramped text" fix above); and
  `[ThemeProfile]` for Theme/Profile export/import
  (`ThemeProfileManager`) and `ApplyThemeFromSettings`/
  `ResortFromSettings`. Complements `CrashLogger` (which only catches
  what actually throws) rather than replacing it - this is for behavior
  that's visibly wrong but doesn't crash. All of it is meant to stay
  long-term, unlike the resize-specific logging above which was scoped
  to one active bug.

<!--
Phase-by-phase, entries land here as they ship:
- 0.9.7-gogogadget.3: Scale multiplier
- 0.9.7-gogogadget.4: Theme & profile export/import
- 0.9.7-gogogadget.5: Value Display (Bar/%/Both) (this release) -
  drag-to-reorder and skins picker dropped from scope, see CLAUDE.md
- 1.0.0: First tagged release
-->
