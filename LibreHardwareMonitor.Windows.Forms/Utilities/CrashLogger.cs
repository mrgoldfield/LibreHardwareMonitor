// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Fork additions Copyright (C) GoGoGadget Hardware Monitor Contributors.

using System;
using System.IO;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

// Upstream LibreHardwareMonitor has no crash/exception logging at all - an
// unhandled exception just vanishes, leaving only whatever Windows Event
// Viewer happened to capture. This fork adds a minimal, dependency-free
// crash logger so a report can point at an actual exception and stack
// trace instead of "it disappeared".
//
// Deliberately does not depend on PersistentSettings or the hardware
// library, so it still works if the crash happens during startup, before
// those are initialized.
internal static class CrashLogger
{
    // Distinct filename prefix, same reasoning as Logger.FileNameFormat:
    // don't collide with a real LibreHardwareMonitor install's own files
    // if both are present on the same machine.
    private const string FileNameFormat = "GoGoGadgetHardwareMonitorCrash-{0:yyyy-MM-dd_HH-mm-ss}.log";

    // Call once, early in Main(), before Application.Run.
    public static void Register()
    {
        Application.ThreadException += (_, e) => Handle(e.Exception, fatal: false);
        AppDomain.CurrentDomain.UnhandledException += (_, e) => Handle(e.ExceptionObject as Exception, fatal: true);
    }

    private static void Handle(Exception exception, bool fatal)
    {
        string path = TryWriteLog(exception, fatal);

        try
        {
            string message = fatal
                ? "GoGoGadget Hardware Monitor hit an unrecoverable error and needs to close."
                : "GoGoGadget Hardware Monitor hit an error but will try to keep running.";

            if (path != null)
                message += "\n\nDetails were saved to:\n" + path;

            MessageBox.Show(message,
                             "GoGoGadget Hardware Monitor",
                             MessageBoxButtons.OK,
                             fatal ? MessageBoxIcon.Error : MessageBoxIcon.Warning);
        }
        catch
        {
            // If we can't even show a dialog, there's nothing left to do -
            // never let the crash handler itself throw.
        }
    }

    private static string TryWriteLog(Exception exception, bool fatal)
    {
        try
        {
            string fileName = Path.Combine(AppPaths.LogDirectory, string.Format(FileNameFormat, DateTime.Now));

            StringBuilder text = new StringBuilder();
            text.AppendLine(fatal ? "Fatal (process terminating)" : "Non-fatal (caught on UI thread)");
            text.AppendLine("Time: " + DateTime.Now.ToString("O"));
            text.AppendLine("App version: " + Assembly.GetExecutingAssembly().GetName().Version);
            text.AppendLine("OS: " + Environment.OSVersion);
            text.AppendLine("64-bit OS: " + Environment.Is64BitOperatingSystem + ", 64-bit process: " + Environment.Is64BitProcess);
            text.AppendLine();

            for (Exception ex = exception; ex != null; ex = ex.InnerException)
            {
                text.AppendLine(ex.GetType().FullName + ": " + ex.Message);
                text.AppendLine(ex.StackTrace);
                text.AppendLine("---");
            }

            File.WriteAllText(fileName, text.ToString());
            return fileName;
        }
        catch
        {
            // Best-effort only - a failure writing the crash log must never
            // itself crash the crash handler.
            return null;
        }
    }
}
