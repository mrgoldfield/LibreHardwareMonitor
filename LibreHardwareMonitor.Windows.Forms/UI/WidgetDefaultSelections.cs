// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Fork additions Copyright (C) GoGoGadget Hardware Monitor Contributors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition: the starting sensor selection offered on first gadget
// activation (see FirstActivationDialog), so a fresh install shows
// something useful immediately instead of an empty gadget. Rules match
// by hardware/sensor *type* and name, never a literal sensor Identifier
// (those are machine-specific), and are read from an editable JSON file
// rather than hardcoded - see WidgetDefaultsApplier for how a rule
// resolves to an actual sensor.
internal enum WidgetDefaultMode
{
    // "Add to Mini Gadget" - also shown in Full, per the gadget's own
    // Mini-implies-Full rule (see SensorGadget.AddToMini).
    Mini,

    // "Add to Full Gadget" only.
    Full
}

internal sealed class WidgetDefaultRule
{
    // "M"/"Mini" or "F"/"Full", case-insensitive - which list(s) this
    // sensor lands in if the rule matches.
    public string Mode { get; set; }

    // LibreHardwareMonitor.Hardware.HardwareType enum name, e.g. "Cpu",
    // "Memory", "Storage", "GpuNvidia".
    public string HardwareType { get; set; }

    // LibreHardwareMonitor.Hardware.SensorType enum name, e.g. "Load",
    // "Temperature".
    public string SensorType { get; set; }

    // If set, only a sensor whose Name exactly matches one of these
    // (case-insensitive) is considered. If empty/omitted, the first
    // matching sensor by Index is used instead.
    public string[] SensorNames { get; set; }

    // If set, only hardware whose Identifier contains this substring is
    // considered - e.g. distinguishing physical RAM ("/ram") from the
    // separate Virtual Memory/pagefile hardware node ("/vram"), which
    // share the same HardwareType.
    public string HardwareIdentifierContains { get; set; }

    // Storage-specific: only consider a Storage device that hosts the
    // drive Windows is installed on (see WidgetDefaultsApplier).
    public bool OsDriveOnly { get; set; }

    // Network-specific: only consider a network adapter that's actually
    // up (see WidgetDefaultsApplier) - a machine can have several
    // Network hardware nodes (Wi-Fi, Ethernet, VPN, disabled adapters)
    // and only the connected one is worth a default sensor.
    public bool ConnectedNetworkOnly { get; set; }

    // A short label shown nowhere in the UI - purely so someone editing
    // the JSON file can tell rules apart at a glance.
    public string Comment { get; set; }

    // Accepts "M"/"Mini" or "F"/"Full" (case-insensitive); defaults to
    // Full for anything else so a typo doesn't silently promote a rule
    // into Mini's small, always-visible set.
    public WidgetDefaultMode ParsedMode =>
        string.Equals(Mode, "M", StringComparison.OrdinalIgnoreCase) || string.Equals(Mode, "Mini", StringComparison.OrdinalIgnoreCase)
            ? WidgetDefaultMode.Mini
            : WidgetDefaultMode.Full;
}

internal sealed class WidgetDefaultsFile
{
    public List<WidgetDefaultRule> Rules { get; set; } = new();
}

internal static class WidgetDefaultSelections
{
    private const string FileName = "DefaultWidgetSelections.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    // The factory defaults, used both as the fallback if the file is
    // missing/unreadable and as the content written out the first time
    // so there's something for the user to look at and edit.
    public static WidgetDefaultsFile FactoryDefaults => new()
    {
        Rules =
        {
            new WidgetDefaultRule
            {
                Mode = "M",
                Comment = "Overall CPU usage",
                HardwareType = "Cpu",
                SensorType = "Load",
                SensorNames = new[] { "CPU Total" }
            },
            new WidgetDefaultRule
            {
                Mode = "M",
                Comment = "Physical RAM usage (not the Virtual Memory/pagefile hardware node)",
                HardwareType = "Memory",
                SensorType = "Load",
                SensorNames = new[] { "Memory" },
                HardwareIdentifierContains = "/ram"
            },
            new WidgetDefaultRule
            {
                Mode = "M",
                Comment = "Busy % of whichever physical disk hosts the Windows drive",
                HardwareType = "Storage",
                SensorType = "Load",
                SensorNames = new[] { "Total Activity" },
                OsDriveOnly = true
            },
            new WidgetDefaultRule
            {
                // Fork change 2026-09-28: was Mode "M" (Mini) - moved to
                // Full-only so Mini/Full defaults are actually
                // distinguishable out of the box. Every other Mini rule
                // above is also in Full already (Mini-implies-Full), so
                // with this one also in Mini, toggling the gadget's
                // Mini/Full mode showed identical content by default.
                Mode = "F",
                Comment = "Used capacity % of whichever physical disk hosts the Windows drive",
                HardwareType = "Storage",
                SensorType = "Load",
                SensorNames = new[] { "Used Space" },
                OsDriveOnly = true
            },
            new WidgetDefaultRule
            {
                Mode = "F",
                Comment = "CPU temperature - label differs by vendor/generation",
                HardwareType = "Cpu",
                SensorType = "Temperature",
                SensorNames = new[] { "CPU Package", "Core (Tctl/Tdie)", "Core (Tctl)", "Core (Tdie)" }
            },
            new WidgetDefaultRule { Mode = "F", Comment = "GPU temperature, only added if this GPU brand is present", HardwareType = "GpuNvidia", SensorType = "Temperature" },
            new WidgetDefaultRule { Mode = "F", Comment = "GPU temperature, only added if this GPU brand is present", HardwareType = "GpuAmd", SensorType = "Temperature" },
            new WidgetDefaultRule { Mode = "F", Comment = "GPU temperature, only added if this GPU brand is present", HardwareType = "GpuIntel", SensorType = "Temperature" },
            new WidgetDefaultRule
            {
                // Fork addition 2026-09-28: a broader complement to the
                // CPU-specific temperature rule above. Board/Super I/O
                // temperature sensor names vary too much by vendor to
                // name-match reliably (unlike CPU Package's short, known
                // list), so this takes the first Temperature sensor the
                // Super I/O chip reports, whatever it's called - reported
                // under HardwareType.SuperIO, not Motherboard (see
                // SuperIOHardware.cs), even though "the motherboard's
                // temperature" is how a user would describe it. Machines
                // with no Super I/O chip exposing a temperature (laptops,
                // some pre-builts, virtual machines) simply get nothing
                // from this rule, same as any other rule that finds no
                // match.
                Mode = "F",
                Comment = "First available board/Super I/O temperature sensor, whatever it's named",
                HardwareType = "SuperIO",
                SensorType = "Temperature"
            },
            new WidgetDefaultRule
            {
                Mode = "F",
                Comment = "GPU core usage %, only added if this GPU brand is present",
                HardwareType = "GpuNvidia",
                SensorType = "Load",
                SensorNames = new[] { "GPU Core" }
            },
            new WidgetDefaultRule
            {
                Mode = "F",
                Comment = "GPU core usage %, only added if this GPU brand is present",
                HardwareType = "GpuAmd",
                SensorType = "Load",
                SensorNames = new[] { "GPU Core" }
            },
            new WidgetDefaultRule
            {
                Mode = "F",
                Comment = "GPU core usage %, only added if this GPU brand is present",
                HardwareType = "GpuIntel",
                SensorType = "Load",
                SensorNames = new[] { "GPU Core" }
            },
            new WidgetDefaultRule
            {
                Mode = "F",
                Comment = "GPU memory usage %, only added if this GPU brand is present",
                HardwareType = "GpuNvidia",
                SensorType = "Load",
                SensorNames = new[] { "GPU Memory" }
            },
            new WidgetDefaultRule
            {
                Mode = "F",
                Comment = "GPU memory usage %, only added if this GPU brand is present",
                HardwareType = "GpuAmd",
                SensorType = "Load",
                SensorNames = new[] { "GPU Memory" }
            },
            new WidgetDefaultRule
            {
                Mode = "F",
                Comment = "GPU memory usage %, only added if this GPU brand is present",
                HardwareType = "GpuIntel",
                SensorType = "Load",
                SensorNames = new[] { "GPU Memory" }
            }
        }
    };

    // Loads UserSettings\DefaultWidgetSelections.json, creating it from
    // FactoryDefaults first if it doesn't exist yet. Never throws - a
    // missing/corrupt file falls back to FactoryDefaults in memory
    // without touching disk, per the "fail gracefully" rule this whole
    // feature is optional under.
    public static WidgetDefaultsFile Load()
    {
        string path = Path.Combine(AppPaths.UserSettingsDirectory, FileName);

        try
        {
            if (!File.Exists(path))
            {
                File.WriteAllText(path, JsonSerializer.Serialize(FactoryDefaults, JsonOptions));
                return FactoryDefaults;
            }

            string json = File.ReadAllText(path);
            WidgetDefaultsFile parsed = JsonSerializer.Deserialize<WidgetDefaultsFile>(json, JsonOptions);
            return parsed ?? FactoryDefaults;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return FactoryDefaults;
        }
    }
}
