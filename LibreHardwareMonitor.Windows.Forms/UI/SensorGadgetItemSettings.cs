// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Drawing;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition: how a bar-capable sensor (Load/Control/Level/Humidity -
// see SensorGadget.IsBarCapableSensorType) renders its value. Every other
// sensor type always renders as a number and this setting is irrelevant
// to it.
internal enum ValueDisplayMode
{
    Bar,
    Percent,
    Both
}

// Fork addition (Phase 2/3): centralizes the per-sensor gadget setting
// keys added after Phase 1's gadget.order (see SensorGadget.OrderKey),
// so call sites don't rebuild Identifier strings by hand. Keys follow the
// gadget.<name> convention documented in CLAUDE.md.
internal static class SensorGadgetItemSettings
{
    private const string BarColorSuffix = "gadget.barColor";
    private const string TextColorSuffix = "gadget.textColor";
    private const string NameColorSuffix = "gadget.nameColor";
    private const string WarnAtSuffix = "gadget.warnAt";
    private const string CritAtSuffix = "gadget.critAt";
    private const string GradientOffSuffix = "gadget.gradientOff";
    private const string DisplayNameSuffix = "gadget.displayName";
    private const string MiniSuffix = "gadget.mini";
    private const string ValueDisplaySuffix = "gadget.valueDisplay";

    // Fork addition (Mini/Full gadget modes): a sensor shown in Mini mode
    // is always also shown in Full mode - see SensorGadget.AddToMini/
    // RemoveFromMini, which are the only places this flag is written, so
    // that invariant can't be violated from here.
    public static bool IsMini(PersistentSettings settings, ISensor sensor)
    {
        return settings.GetValue(Key(sensor, MiniSuffix), false);
    }

    public static void SetMini(PersistentSettings settings, ISensor sensor, bool value)
    {
        if (value)
            settings.SetValue(Key(sensor, MiniSuffix), true);
        else
            settings.Remove(Key(sensor, MiniSuffix));
    }

    // Fork addition: a display name used only by the gadget row - never
    // ISensor.Name itself, so the main window, CSV log header, and tray
    // all keep showing the sensor's real name. See DisplayNameDialog.
    public static bool TryGetDisplayName(PersistentSettings settings, ISensor sensor, out string displayName)
    {
        string key = Key(sensor, DisplayNameSuffix);
        if (settings.Contains(key))
        {
            displayName = settings.GetValue(key, string.Empty);
            return true;
        }

        displayName = null;
        return false;
    }

    public static void SetDisplayName(PersistentSettings settings, ISensor sensor, string displayName)
    {
        settings.SetValue(Key(sensor, DisplayNameSuffix), displayName);
    }

    public static void ClearDisplayName(PersistentSettings settings, ISensor sensor)
    {
        settings.Remove(Key(sensor, DisplayNameSuffix));
    }

    public static bool TryGetBarColor(PersistentSettings settings, ISensor sensor, out Color color)
    {
        string key = Key(sensor, BarColorSuffix);
        if (settings.Contains(key))
        {
            color = settings.GetValue(key, Color.Empty);
            return true;
        }

        color = Color.Empty;
        return false;
    }

    public static void SetBarColor(PersistentSettings settings, ISensor sensor, Color color)
    {
        settings.SetValue(Key(sensor, BarColorSuffix), color);
    }

    public static void ClearBarColor(PersistentSettings settings, ISensor sensor)
    {
        settings.Remove(Key(sensor, BarColorSuffix));
    }

    // Fork addition: lets a sensor's drawn number use a different color
    // than its bar/gradient - most useful for "Both" display mode, where
    // the number is drawn on top of the bar and needs to stay legible
    // against it. Falls back to the bar's resolved color (today's
    // behavior) when unset - see SensorGadget.ResolveSensorTextColor.
    public static bool TryGetTextColor(PersistentSettings settings, ISensor sensor, out Color color)
    {
        string key = Key(sensor, TextColorSuffix);
        if (settings.Contains(key))
        {
            color = settings.GetValue(key, Color.Empty);
            return true;
        }

        color = Color.Empty;
        return false;
    }

    public static void SetTextColor(PersistentSettings settings, ISensor sensor, Color color)
    {
        settings.SetValue(Key(sensor, TextColorSuffix), color);
    }

    public static void ClearTextColor(PersistentSettings settings, ISensor sensor)
    {
        settings.Remove(Key(sensor, TextColorSuffix));
    }

    // Fork addition: lets a sensor's drawn DISPLAY NAME (the label, e.g.
    // "CPU Total") use a different color than the gadget's normal font
    // color - independent of Bar Color/Text Color, which only affect the
    // bar and the drawn number. Falls back to the gadget's normal font
    // color when unset - see SensorGadget.ResolveSensorNameColor.
    public static bool TryGetNameColor(PersistentSettings settings, ISensor sensor, out Color color)
    {
        string key = Key(sensor, NameColorSuffix);
        if (settings.Contains(key))
        {
            color = settings.GetValue(key, Color.Empty);
            return true;
        }

        color = Color.Empty;
        return false;
    }

    public static void SetNameColor(PersistentSettings settings, ISensor sensor, Color color)
    {
        settings.SetValue(Key(sensor, NameColorSuffix), color);
    }

    public static void ClearNameColor(PersistentSettings settings, ISensor sensor)
    {
        settings.Remove(Key(sensor, NameColorSuffix));
    }

    // Three states per sensor: explicit thresholds (warnAt/critAt keys
    // present) always win; failing that, an explicit opt-out
    // (gradientOff) forces gradient coloring off even for a sensor type
    // that has a built-in default; failing that, GradientDefaults
    // supplies an on-by-default threshold for common sensor types
    // (Temperature, Load) so gradient coloring works out of the box.
    public static bool TryGetGradientThresholds(PersistentSettings settings, ISensor sensor, out float warnAt, out float critAt)
    {
        string warnKey = Key(sensor, WarnAtSuffix);
        string critKey = Key(sensor, CritAtSuffix);
        if (settings.Contains(warnKey) && settings.Contains(critKey))
        {
            warnAt = settings.GetValue(warnKey, 0f);
            critAt = settings.GetValue(critKey, 0f);
            return true;
        }

        if (!settings.GetValue(Key(sensor, GradientOffSuffix), false) &&
            GradientDefaults.TryGetDefault(sensor.SensorType, out warnAt, out critAt))
        {
            return true;
        }

        warnAt = 0f;
        critAt = 0f;
        return false;
    }

    public static void SetGradientThresholds(PersistentSettings settings, ISensor sensor, float warnAt, float critAt)
    {
        settings.SetValue(Key(sensor, WarnAtSuffix), warnAt);
        settings.SetValue(Key(sensor, CritAtSuffix), critAt);
        settings.Remove(Key(sensor, GradientOffSuffix));
    }

    public static void ClearGradientThresholds(PersistentSettings settings, ISensor sensor)
    {
        settings.Remove(Key(sensor, WarnAtSuffix));
        settings.Remove(Key(sensor, CritAtSuffix));

        // For a sensor type with a built-in default, "Clear" means
        // "turn gradient coloring off for this one sensor" - without
        // this flag, removing the keys above would just fall back to
        // GradientDefaults again and Clear would appear to do nothing.
        if (GradientDefaults.TryGetDefault(sensor.SensorType, out _, out _))
            settings.SetValue(Key(sensor, GradientOffSuffix), true);
        else
            settings.Remove(Key(sensor, GradientOffSuffix));
    }

    // Fork addition (Value Display %/Bar/Both): default is "Percent" -
    // user preferred the plain number over "Both"'s number-on-bar look
    // (2026-09-22).
    public static ValueDisplayMode GetValueDisplayMode(PersistentSettings settings, ISensor sensor)
    {
        string raw = settings.GetValue(Key(sensor, ValueDisplaySuffix), nameof(ValueDisplayMode.Percent));
        return Enum.TryParse(raw, out ValueDisplayMode mode) ? mode : ValueDisplayMode.Percent;
    }

    public static void SetValueDisplayMode(PersistentSettings settings, ISensor sensor, ValueDisplayMode mode)
    {
        settings.SetValue(Key(sensor, ValueDisplaySuffix), mode.ToString());
    }

    private static string Key(ISensor sensor, string suffix)
    {
        return new Identifier(sensor.Identifier, suffix).ToString();
    }
}
