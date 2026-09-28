// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Fork additions Copyright (C) GoGoGadget Hardware Monitor Contributors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Hardware.Storage;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition: resolves a WidgetDefaultsFile's rules against the
// actually-detected hardware and applies the matches to a SensorGadget.
// A rule that matches nothing on this machine (no discrete GPU, etc.) is
// silently skipped rather than treated as an error - "keep the
// selections small" means this is a best-effort starting point, not a
// guarantee every rule finds something.
internal static class WidgetDefaultsApplier
{
    public static void Apply(WidgetDefaultsFile config, IComputer computer, SensorGadget gadget)
    {
        List<IHardware> allHardware = FlattenHardware(computer).ToList();
        char? osDriveLetter = GetOsDriveLetter();

        foreach (WidgetDefaultRule rule in config.Rules)
        {
            ISensor sensor = FindMatch(rule, allHardware, osDriveLetter);
            if (sensor == null)
                continue;

            if (rule.ParsedMode == WidgetDefaultMode.Mini)
                gadget.AddToMini(sensor);
            else
                gadget.Add(sensor);
        }
    }

    private static ISensor FindMatch(WidgetDefaultRule rule, List<IHardware> allHardware, char? osDriveLetter)
    {
        if (!Enum.TryParse(rule.HardwareType, ignoreCase: true, out HardwareType hardwareType) ||
            !Enum.TryParse(rule.SensorType, ignoreCase: true, out SensorType sensorType))
        {
            return null;
        }

        IEnumerable<IHardware> candidates = allHardware.Where(h => h.HardwareType == hardwareType);

        if (!string.IsNullOrEmpty(rule.HardwareIdentifierContains))
            candidates = candidates.Where(h => h.Identifier.ToString().Contains(rule.HardwareIdentifierContains, StringComparison.OrdinalIgnoreCase));

        if (rule.OsDriveOnly)
            candidates = candidates.Where(h => HostsDrive(h, osDriveLetter));

        if (rule.ConnectedNetworkOnly)
            candidates = candidates.Where(IsConnectedNetworkAdapter);

        // Deterministic "first" match: hardware.Identifier already sorts
        // devices in discovery order (see SensorGadget.HardwareComparer).
        IHardware hardware = candidates.OrderBy(h => h.Identifier.ToString(), StringComparer.Ordinal).FirstOrDefault();
        if (hardware == null)
            return null;

        IEnumerable<ISensor> sensors = hardware.Sensors.Where(s => s.SensorType == sensorType);

        if (rule.SensorNames is { Length: > 0 })
            sensors = sensors.Where(s => rule.SensorNames.Any(name => string.Equals(name, s.Name, StringComparison.OrdinalIgnoreCase)));

        return sensors.OrderBy(s => s.Index).FirstOrDefault();
    }

    private static bool HostsDrive(IHardware hardware, char? driveLetter)
    {
        if (driveLetter == null || hardware is not StorageDevice storageDevice)
            return false;

        return storageDevice.Storage.Partitions?.Any(p =>
            p.DriveLetter.HasValue && char.ToUpperInvariant(p.DriveLetter.Value) == char.ToUpperInvariant(driveLetter.Value)) ?? false;
    }

    // Network hardware is internal to LibreHardwareMonitorLib, so there's
    // no public type to cast to the way HostsDrive casts to StorageDevice
    // - instead, match this IHardware back to a real NetworkInterface by
    // reconstructing the same Identifier the library builds for it
    // (new Identifier("nic", networkInterface.Id) - see Network.cs) and
    // checking that interface is actually up. Excludes loopback/tunnel
    // adapters, which report Up but aren't a "connected network" in any
    // useful sense for a default sensor.
    private static bool IsConnectedNetworkAdapter(IHardware hardware)
    {
        foreach (NetworkInterface networkInterface in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (networkInterface.OperationalStatus != OperationalStatus.Up)
                continue;

            if (networkInterface.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel)
                continue;

            if (new Identifier("nic", networkInterface.Id).ToString() == hardware.Identifier.ToString())
                return true;
        }

        return false;
    }

    private static char? GetOsDriveLetter()
    {
        try
        {
            string root = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows));
            return string.IsNullOrEmpty(root) ? null : root[0];
        }
        catch (Exception ex) when (ex is ArgumentException or PlatformNotSupportedException)
        {
            return null;
        }
    }

    private static IEnumerable<IHardware> FlattenHardware(IComputer computer)
    {
        foreach (IHardware hardware in computer.Hardware)
        {
            foreach (IHardware flattened in FlattenHardware(hardware))
                yield return flattened;
        }
    }

    private static IEnumerable<IHardware> FlattenHardware(IHardware hardware)
    {
        yield return hardware;

        foreach (IHardware sub in hardware.SubHardware)
        {
            foreach (IHardware flattened in FlattenHardware(sub))
                yield return flattened;
        }
    }
}
