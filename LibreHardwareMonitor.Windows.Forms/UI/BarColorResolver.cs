// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Drawing;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition (Phase 2/3): resolves the green -> yellow -> red gradient
// color for a sensor value between a user-defined threshold and max.
// Quantized to a fixed bucket count so the caller's tint cache
// (Dictionary<Color, Image> in SensorGadget) stays bounded no matter how
// often the sensor value ticks - see CLAUDE.md / memory on "stable and
// low overhead".
public static class BarColorResolver
{
    public const int GradientBuckets = 32;

    /// <summary>
    /// Resolves the gradient color for <paramref name="value"/> between
    /// <paramref name="warnAt"/> (solid green, or where the shift starts)
    /// and <paramref name="critAt"/> (solid red). Values below
    /// <paramref name="warnAt"/> are green; at/above <paramref name="critAt"/>
    /// are red. The 0..1 position is quantized to <see cref="GradientBuckets"/>
    /// steps before conversion so repeated calls for nearby values return
    /// one of a fixed, small set of colors.
    /// </summary>
    public static Color ResolveGradientColor(float value, float warnAt, float critAt)
    {
        float t;
        if (critAt <= warnAt)
        {
            // Degenerate/inverted range: treat as "already at max".
            t = 1f;
        }
        else
        {
            t = (value - warnAt) / (critAt - warnAt);
            // Math.Clamp isn't available on this project's net472 target.
            t = Math.Min(1f, Math.Max(0f, t));
        }

        int bucket = (int)Math.Round(t * (GradientBuckets - 1));
        float bucketedT = bucket / (float)(GradientBuckets - 1);

        // Hue sweeps from green (120 deg) down to red (0 deg); yellow
        // (60 deg) falls out naturally at the midpoint, giving a
        // green -> yellow -> red gradient from a single hue lerp instead
        // of piecewise RGB blending.
        float hue = 120f * (1f - bucketedT);
        return ColorFromHsv(hue, 1f, 1f);
    }

    private static Color ColorFromHsv(float hueDegrees, float saturation, float value)
    {
        float h = (hueDegrees % 360f + 360f) % 360f / 60f;
        int i = (int)Math.Floor(h);
        float f = h - i;
        float p = value * (1 - saturation);
        float q = value * (1 - saturation * f);
        float t = value * (1 - saturation * (1 - f));

        (float r, float g, float b) = i switch
        {
            0 => (value, t, p),
            1 => (q, value, p),
            2 => (p, value, t),
            3 => (p, q, value),
            4 => (t, p, value),
            _ => (value, p, q)
        };

        return Color.FromArgb(255,
                              (int)Math.Round(r * 255),
                              (int)Math.Round(g * 255),
                              (int)Math.Round(b * 255));
    }
}
