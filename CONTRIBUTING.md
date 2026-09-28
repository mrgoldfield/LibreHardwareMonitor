# Contributing to GoGoGadget Hardware Monitor

Thanks for considering a contribution. This is a small hobby fork of
[LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor)
focused on one feature area — the Sensor Gadget — so contributions here are
narrower in scope than upstream's.

**Before filing an issue**, a quick check: is this about hardware detection,
a wrong sensor reading, or a motherboard/GPU/CPU not being recognized at
all? That's almost certainly an upstream issue, not this fork's — please
file it at [LibreHardwareMonitor/LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues)
instead. This fork periodically rebases onto upstream, so a fix there
reaches here too. Issues here should be about the gadget's reordering,
colors, alarms, themes, or profiles.

## Building

Requirements: the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
and Git, on Windows (the app is WinForms and doesn't run cross-platform).

```powershell
git clone https://github.com/mrgoldfield/gogogadget.git
cd gogogadget
dotnet build LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj -c Release -p:Platform=x64
```

Output lands in `bin\Release\x64\net10.0-windows\` (swap `-p:Platform=x64`
for `-p:Platform=ARM64` for a native Windows-on-ARM build, which lands in
`bin\Release\ARM64\net10.0-windows\`).

## Where the gadget code lives

- `LibreHardwareMonitor.Windows.Forms/UI/SensorGadget.cs` — the floating
  gadget's layout, drawing, and (as fork features land) reordering/coloring
  logic.
- `LibreHardwareMonitor.Windows.Forms/UI/Gadget.cs` / `GadgetWindow.cs` —
  the underlying always-on-top borderless window the gadget renders into.
  Rarely needs touching for a gadget-content feature.
- `LibreHardwareMonitor.Windows.Forms/Utilities/PersistentSettings.cs` —
  the flat key-value settings store everything (including new fork
  settings) is persisted through.

## Settings-key naming convention

Every per-sensor setting is namespaced by sensor identifier, using the
existing pattern (see `SensorGadget.Add`/`SensorAdded` for the established
example, `"gadget"` and `"penColor"`):

```c#
settings.SetValue(new Identifier(sensor.Identifier, "gadget.myNewKey").ToString(), value);
```

New per-sensor gadget settings should be prefixed `gadget.` (e.g.
`gadget.order`, `gadget.barColor`, `gadget.warnAt`, `gadget.critAt`) so
they're easy to find and don't collide with unrelated settings on the same
sensor. Gadget-wide (not per-sensor) settings follow the existing
`sensorGadget.*` prefix already used for things like `sensorGadget.FontSize`.

## Tests

`GoGoGadgetHardwareMonitor.Tests` is a plain xUnit project (no WinForms
dependency) for logic that doesn't need a rendered UI to test — settings
persistence, threshold/color resolution, that kind of thing. Run it with:

```powershell
dotnet test GoGoGadgetHardwareMonitor.Tests\GoGoGadgetHardwareMonitor.Tests.csproj
```

The owner-drawn gadget painting itself isn't realistically unit-testable;
that's covered by manual QA before a release instead.

## Code style

The existing `.editorconfig` is authoritative — most IDEs pick it up
automatically. Before opening a PR:

```powershell
dotnet format LibreHardwareMonitor.Windows.Forms\LibreHardwareMonitor.Windows.Forms.csproj --verify-no-changes
```

## Keeping the fork mergeable with upstream

Two habits that keep future rebases from upstream tractable, please follow
them in PRs:

1. **New logic goes in new files** where reasonable (a new
   `SensorGadgetItemSettings.cs`, a new `BarColorResolver.cs`) rather than
   large rewrites of existing methods, so an upstream change to a file this
   fork didn't touch merges cleanly.
2. **C# namespaces stay `LibreHardwareMonitor.*`** — don't rename them to
   match the fork's product name. See [CHANGES.md](CHANGES.md) for why.

## License

By contributing, you agree your contribution is licensed under MPL-2.0,
matching the rest of the codebase (see [LICENSE](LICENSE)). New files
should carry the standard MPL-2.0 header comment already used throughout
the codebase.

## Pull requests

- One logical change per PR.
- Update [CHANGELOG.md](CHANGELOG.md) under `[Unreleased]`.
- CI (build for `net472` + `net10.0-windows`) runs automatically on PRs
  — please make sure it's green before requesting review.
