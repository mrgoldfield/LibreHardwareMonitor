// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Fork additions Copyright (C) GoGoGadget Hardware Monitor Contributors.

using System;
using System.IO;

namespace LibreHardwareMonitor.Windows.Forms.Utilities;

// General-purpose, always-on diagnostic logger for the areas of the app
// that have actually produced hard-to-pin-down bugs before (see the
// call sites and CHANGELOG.md's Fixed section for the history each one
// is tied to): gadget resize/Z-order, dynamically-built dialog layout,
// and Theme/Profile import/export. Complements CrashLogger - this is
// for "something behaved wrong but didn't throw", CrashLogger is for
// "something threw". A single flat file with an [area] tag per line
// keeps it easy to grep instead of needing a different log per
// subsystem.
//
// Deliberately dependency-light and always-on (no settings toggle) -
// this is a handful of infrequent events (a resize, a dialog opening, a
// theme import), not a hot path, so the file-append cost is
// negligible, and a toggle the user would have to know to flip defeats
// the point of catching a bug on the first repro instead of a second
// "now turn on logging and try again" round trip.
internal static class DebugLog
{
    private const string FileName = "GoGoGadgetDebug.log";

    public static void Write(string area, string message)
    {
        try
        {
            File.AppendAllText(Path.Combine(AppPaths.LogDirectory, FileName),
                                DateTime.Now.ToString("HH:mm:ss.fff") + "  [" + area + "]  " + message + Environment.NewLine);
        }
        catch
        {
            // Best-effort only - never let logging itself break the app.
        }
    }
}
