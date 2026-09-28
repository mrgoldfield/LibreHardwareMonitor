# GoGoGadget Hardware Monitor

[![GitHub license](https://img.shields.io/github/license/mrgoldfield/gogogadget)](LICENSE) [![Build status](https://github.com/mrgoldfield/gogogadget/actions/workflows/master.yml/badge.svg)](https://github.com/mrgoldfield/gogogadget/actions)

GoGoGadget Hardware Monitor is a fork of [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) — free software that monitors the temperature sensors, fan speeds, voltages, load and clock speeds of your computer — focused on one thing upstream doesn't do: a **fully customizable Sensor Gadget** (the small floating on-desktop widget).

> This is a hobby fork, not affiliated with the LibreHardwareMonitor project. All credit for the underlying sensor library and application goes to [LibreHardwareMonitor and its contributors](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/graphs/contributors). See [CHANGES.md](CHANGES.md) for exactly what this fork adds on top.

## What does the fork add?

The gadget you get from "Add to Full/Mini Gadget" now supports:

- **Mini/Full modes** — double-click the gadget (or its "Mini Mode" checkbox) to switch between a small, curated sensor set and everything you've added. A sensor added to Mini is always shown in Full too. The first time you ever show the gadget, it starts with a small default selection (CPU/RAM/disk usage, plus temperatures) instead of empty — fully editable in `UserSettings\DefaultWidgetSelections.json`.
- **Reordering** — move a sensor up/down within its hardware block, or move a whole hardware block (CPU, GPU, RAM, ...) up/down relative to the others, instead of a fixed automatic order.
- **Per-sensor bar color** — pick a color for any one sensor's bar, not just one color for the whole gadget.
- **Alarm-level coloring** — set a warning/critical threshold per sensor; its bar recolors automatically when crossed. Defaults are suggested from the hardware's own reported safe limits where available.
- **Themes & profiles you can share** — save your whole visual setup as a `.lhmtheme.json` (portable, works on anyone's hardware) or your whole gadget setup as a `.lhmprofile.json` (for backup / another machine of your own).

See [CHANGELOG.md](CHANGELOG.md) for the detailed history of how each of these shipped, and [CHANGES.md](CHANGES.md) for the structural differences from upstream.

Everything else — sensor detection, the main window, remote web server, logging, and so on — is unmodified LibreHardwareMonitor.

## What's included?

| Name | .NET | Build Status |
| --- | --- | --- |
| **GoGoGadgetHardwareMonitor** <br /> Windows Forms based application that presents all data in a graphical interface, gadget included | .NET Framework 4.7.2 <br/> .NET 10.0 | [![Build status](https://github.com/mrgoldfield/gogogadget/actions/workflows/master.yml/badge.svg)](https://github.com/mrgoldfield/gogogadget/actions) |
| **LibreHardwareMonitorLib** <br /> Unmodified upstream sensor library | .NET Framework 4.7.2 <br/> .NET Standard 2.0 <br/> .NET 8.0, .NET 9.0, and .NET 10.0 | [![Build status](https://github.com/mrgoldfield/gogogadget/actions/workflows/master.yml/badge.svg)](https://github.com/mrgoldfield/gogogadget/actions) |

## What can it do?

You can read information from devices such as:
- Motherboards
- Intel and AMD processors
- NVIDIA, AMD and Intel graphics cards
- HDD, SSD and NVMe hard drives
- Network cards

## Where can I download it?

Releases are published as a plain `.zip` on the [Releases page](https://github.com/mrgoldfield/gogogadget/releases) — no installer, no code signing. Windows SmartScreen may flag the executable on first run since it isn't signed; that's expected for an unsigned hobby build.

## How can I help improve it?

Contributions welcome — see [CONTRIBUTING.md](CONTRIBUTING.md) for how to build the project, where the gadget code lives, and the settings-key naming convention to follow for new per-sensor settings.

Bug reports and hardware-support issues that aren't specific to this fork (a sensor reading wrong, a motherboard not detected) are usually better filed upstream at [LibreHardwareMonitor/LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/issues) — this fork periodically rebases onto upstream, so fixes there reach here too.

## Developer information

**Integrate the library in your own application**
1. Add the [LibreHardwareMonitorLib](https://www.nuget.org/packages/LibreHardwareMonitorLib/) NuGet package to your application (unchanged, published by upstream).
2. Use the sample code below.

**Sample code**
```c#
Computer computer = new Computer
{
    IsCpuEnabled = true,
    IsGpuEnabled = true,
    IsMemoryEnabled = true,
    IsMotherboardEnabled = true,
    IsControllerEnabled = true,
    IsNetworkEnabled = true,
    IsStorageEnabled = true,
	IsPowerMonitorEnabled = true,
};

computer.Open();
computer.Accept(new UpdateVisitor());

foreach (IHardware hardware in computer.Hardware)
{
    Console.WriteLine("Hardware: {0}", hardware.Name);
    
    foreach (IHardware subhardware in hardware.SubHardware)
    {
        Console.WriteLine("\tSubhardware: {0}", subhardware.Name);
        
        foreach (ISensor sensor in subhardware.Sensors)
            Console.WriteLine("\t\tSensor: {0}, value: {1}", sensor.Name, sensor.Value);
    }

    foreach (ISensor sensor in hardware.Sensors)
        Console.WriteLine("\tSensor: {0}, value: {1}", sensor.Name, sensor.Value);
}

computer.Close();

public class UpdateVisitor : IVisitor
{
    public void VisitComputer(IComputer computer) => computer.Traverse(this);

    public void VisitHardware(IHardware hardware)
    {
        hardware.Update();
        foreach (IHardware subHardware in hardware.SubHardware)
            subHardware.Accept(this);
    }

    public void VisitSensor(ISensor sensor) { }

    public void VisitParameter(IParameter parameter) { }
}
```

**Administrator rights**

Some sensors require administrator privileges to access the data. Restart your IDE with admin privileges, or add an [app.manifest](https://learn.microsoft.com/en-us/windows/win32/sbscs/application-manifests) file to your project with requestedExecutionLevel on requireAdministrator.

## Warning

Neither this fork nor upstream LibreHardwareMonitor is affiliated with `librehardwaremonitor.com`. For your safety, please avoid using that site.

## Acknowledgements

Everything this fork is built on is the work of the [LibreHardwareMonitor team and contributors](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor/graphs/contributors) — this repository exists to add one specific feature set on top, not to replace or compete with it.

## License

GoGoGadget Hardware Monitor, like the LibreHardwareMonitor code it's built on, is free and open source software licensed under MPL 2.0 — see [LICENSE](LICENSE). Some parts are licensed under different terms; see [THIRD-PARTY-NOTICES.txt](THIRD-PARTY-NOTICES.txt). See [CHANGES.md](CHANGES.md) for what this fork modified.
