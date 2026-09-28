// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Fork additions Copyright (C) GoGoGadget Hardware Monitor Contributors.

using System;
using System.IO;
using System.Windows.Forms;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

// Fork addition: earlier builds wrote the settings (.config), CSV log,
// and crash log files directly next to the exe, mixed in with the
// binaries. Keeps them in two clearly-named subfolders instead - easier
// to find, back up, or wipe without touching the app itself.
internal static class AppPaths
{
    private const string UserSettingsDirectoryName = "UserSettings";
    private const string LogDirectoryName = "log";

    // Renamed from "WidgetConfig" - kept only so GetSettingsFileName can
    // migrate a settings file that already landed there for anyone who
    // ran a build between that name and this one.
    private const string PreviousUserSettingsDirectoryName = "WidgetConfig";

    public static string UserSettingsDirectory => EnsureDirectory(UserSettingsDirectoryName);
    public static string LogDirectory => EnsureDirectory(LogDirectoryName);

    // Every user-changed gadget/app setting lives in this one file - see
    // PersistentSettings. One-time migration below moves a pre-existing
    // settings file from an older location instead of silently starting
    // the user over on an update.
    public static string GetSettingsFileName()
    {
        string legacyFileName = Path.ChangeExtension(Application.ExecutablePath, ".config");
        string fileName = Path.Combine(UserSettingsDirectory, Path.GetFileName(legacyFileName));

        if (!File.Exists(fileName))
        {
            string previousDirFileName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, PreviousUserSettingsDirectoryName, Path.GetFileName(legacyFileName));
            TryMigrate(previousDirFileName, fileName);
            TryMigrate(legacyFileName, fileName);
        }

        return fileName;
    }

    private static bool TryMigrate(string sourceFileName, string destinationFileName)
    {
        if (File.Exists(destinationFileName) || !File.Exists(sourceFileName))
            return false;

        try
        {
            File.Move(sourceFileName, destinationFileName);

            string sourceBackup = sourceFileName + ".backup";
            if (File.Exists(sourceBackup))
                File.Move(sourceBackup, destinationFileName + ".backup");

            return true;
        }
        catch
        {
            // Best-effort - if migration fails, the caller just loads an
            // empty settings file from UserSettings instead of crashing
            // over it, and the old file is left in place.
            return false;
        }
    }

    private static string EnsureDirectory(string name)
    {
        string path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, name);
        Directory.CreateDirectory(path);
        return path;
    }
}
