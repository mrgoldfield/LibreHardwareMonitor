// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Fork additions Copyright (C) GoGoGadget Hardware Monitor Contributors.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition (Phase 5 - Theme & Profile export/import): exports and
// re-imports a whitelisted slice of PersistentSettings' flat key-value
// store as one of two file kinds - see CLAUDE.md's "Theme vs. Profile"
// decision.
//
// Both file kinds are a flat string->string dump of the *raw* values
// PersistentSettings already stores - every typed Get/SetValue overload
// (int/float/bool/Color) serializes to a string internally (see
// PersistentSettings.GetAll), so a round-trip through this class needs
// no type-specific parsing: import writes the same raw string back with
// the plain string SetValue overload.
internal static class ThemeProfileManager
{
    private const string ThemeKind = "gogogadget-theme";
    private const string ProfileKind = "gogogadget-profile";

    // The gadget-wide settings that are pure visual style - shareable
    // with anyone regardless of what hardware they have. Deliberately
    // excludes gadget-wide keys that are window/session state instead
    // (position, lock, width, mini-mode, the first-run flag) - see
    // CLAUDE.md's settings-keys note.
    private static readonly string[] ThemeKeys =
    {
        "sensorGadget.FontSize",
        "sensorGadget.FontColor",
        "sensorGadget.BackgroundColor",
        "sensorGadget.Opacity",
        "sensorGadget.HardwarenamesFull",
        "sensorGadget.HardwarenamesMini",
        "sensorGadget.gradientBarBackground",
        "sensorGadget.ScaleMultiplier",
        "sensorGadget.RowIcons",
        "sensorGadget.GroupLayout"
    };

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void ExportTheme(PersistentSettings settings, string filePath)
    {
        Write(filePath, ThemeKind, CollectTheme(settings));
    }

    // A Profile embeds the Theme keys plus every per-sensor/per-hardware
    // key - see IsPerSensorOrHardwareKey. Those are tied to this
    // machine's specific sensor identifiers; on another machine (or
    // different hardware on the same machine) they simply won't match
    // anything and are silently inert, same as WidgetDefaultsApplier's
    // "a rule that matches nothing is skipped" rule elsewhere.
    public static void ExportProfile(PersistentSettings settings, string filePath)
    {
        Dictionary<string, string> values = CollectTheme(settings);
        foreach (KeyValuePair<string, string> pair in settings.GetAll())
        {
            if (IsPerSensorOrHardwareKey(pair.Key))
                values[pair.Key] = pair.Value;
        }

        Write(filePath, ProfileKind, values);
    }

    public static bool TryImportTheme(PersistentSettings settings, string filePath, out string error)
    {
        return TryImport(settings, filePath, key => Array.IndexOf(ThemeKeys, key) >= 0, out error);
    }

    // Importing a Profile also applies its embedded Theme keys - a
    // Profile is "a theme plus sensor-specific settings", not a
    // replacement for one, per CLAUDE.md.
    public static bool TryImportProfile(PersistentSettings settings, string filePath, out string error)
    {
        return TryImport(settings, filePath, key => Array.IndexOf(ThemeKeys, key) >= 0 || IsPerSensorOrHardwareKey(key), out error);
    }

    private static Dictionary<string, string> CollectTheme(PersistentSettings settings)
    {
        Dictionary<string, string> values = new();
        IReadOnlyDictionary<string, string> all = settings.GetAll();
        foreach (string key in ThemeKeys)
        {
            if (all.TryGetValue(key, out string value))
                values[key] = value;
        }

        return values;
    }

    private static bool TryImport(PersistentSettings settings, string filePath, Func<string, bool> keyFilter, out string error)
    {
        DebugLog.Write("ThemeProfile", $"Import starting: {filePath}");

        ThemeProfileFile file;
        try
        {
            string json = File.ReadAllText(filePath);
            file = JsonSerializer.Deserialize<ThemeProfileFile>(json, JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            error = "Couldn't read that file: " + ex.Message;
            DebugLog.Write("ThemeProfile", $"Import failed reading/parsing {filePath}: {ex.GetType().Name}: {ex.Message}");
            return false;
        }

        if (file?.Values == null || (file.Kind != ThemeKind && file.Kind != ProfileKind))
        {
            error = "That file doesn't look like a GoGoGadget theme/profile file.";
            DebugLog.Write("ThemeProfile", $"Import rejected {filePath}: kind={file?.Kind ?? "(null)"} valueCount={file?.Values?.Count.ToString() ?? "(null)"}");
            return false;
        }

        // Not a hard error to import a Theme file through ImportProfile
        // (or vice versa via a renamed extension) - the key filter above
        // just naturally applies fewer or more keys depending on which
        // import was asked for, rather than refusing outright.
        int applied = 0;
        foreach (KeyValuePair<string, string> pair in file.Values)
        {
            if (keyFilter(pair.Key))
            {
                settings.SetValue(pair.Key, pair.Value);
                applied++;
            }
        }

        DebugLog.Write("ThemeProfile", $"Import succeeded: {filePath} (kind={file.Kind}, {applied}/{file.Values.Count} keys applied)");
        error = null;
        return true;
    }

    private static void Write(string filePath, string kind, Dictionary<string, string> values)
    {
        DebugLog.Write("ThemeProfile", $"Export starting: {filePath} (kind={kind}, {values.Count} keys)");
        try
        {
            ThemeProfileFile file = new() { Kind = kind, Values = values };
            File.WriteAllText(filePath, JsonSerializer.Serialize(file, JsonOptions));
        }
        catch (Exception ex)
        {
            // Logged, then rethrown as-is - this doesn't add error handling
            // that wasn't there before (the caller/CrashLogger still see
            // the same exception), just a record of what was being
            // attempted when it happened.
            DebugLog.Write("ThemeProfile", $"Export threw: {filePath}: {ex.GetType().Name}: {ex.Message}");
            throw;
        }

        DebugLog.Write("ThemeProfile", $"Export succeeded: {filePath}");
    }

    // A key that PersistentSettings stores per-sensor or per-hardware
    // always ends in "/gadget" or "/gadget.<name>" - see Identifier and
    // SensorGadget's OrderKey/HardwareOrderKey and
    // SensorGadgetItemSettings. Gadget-wide keys never start with "/" at
    // all (they're plain literals like "sensorGadget.FontSize"), so this
    // can't collide with ThemeKeys.
    private static bool IsPerSensorOrHardwareKey(string key)
    {
        if (string.IsNullOrEmpty(key) || key[0] != '/')
            return false;

        int lastSlash = key.LastIndexOf('/');
        string suffix = key.Substring(lastSlash + 1);
        return suffix == "gadget" || suffix.StartsWith("gadget.", StringComparison.Ordinal);
    }

    private sealed class ThemeProfileFile
    {
        public string Kind { get; set; }
        public Dictionary<string, string> Values { get; set; }
    }
}
