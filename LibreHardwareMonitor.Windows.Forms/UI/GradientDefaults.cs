// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition (Phase 3): suggested starting values for
// GradientThresholdDialog when a sensor has no thresholds configured
// yet, so setting up gradient coloring on a common sensor type doesn't
// start from a blank form. Purely a dialog pre-fill - never applied
// until the user opens the dialog and clicks OK, so it doesn't change
// how any sensor renders on its own.
internal static class GradientDefaults
{
    public static bool TryGetDefault(SensorType type, out float warnAt, out float critAt)
    {
        switch (type)
        {
            // Percent-based sensors (CPU/GPU/memory load, storage used
            // space - see StorageDevice.cs's "Used Space" sensor) and
            // temperatures both read comfortably on a 50/90 scale: below
            // half is unremarkable, and most consumer hardware's safe
            // ceiling sits close to 90 (% or degrees C).
            case SensorType.Temperature:
            case SensorType.Load:
                warnAt = 50f;
                critAt = 90f;
                return true;

            default:
                warnAt = 0f;
                critAt = 0f;
                return false;
        }
    }
}
