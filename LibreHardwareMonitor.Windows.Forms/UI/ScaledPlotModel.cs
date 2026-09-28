// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using OxyPlot;
using OxyPlot.Legends;

namespace LibreHardwareMonitor.Windows.Forms.UI;

class ScaledPlotModel : PlotModel
{
    public ScaledPlotModel(double dpiXscale, double dpiYscale)
    {
        PlotMargins = new OxyThickness(PlotMargins.Left * dpiXscale,
                                       PlotMargins.Top * dpiYscale,
                                       PlotMargins.Right * dpiXscale,
                                       PlotMargins.Bottom * dpiYscale);

        Padding = new OxyThickness(Padding.Left * dpiXscale,
                                   Padding.Top * dpiYscale,
                                   Padding.Right * dpiXscale,
                                   Padding.Bottom * dpiYscale);

        TitlePadding *= dpiXscale;

        Legend legend = new();

        legend.LegendSymbolLength *= dpiXscale;
        legend.LegendSymbolMargin *= dpiXscale;
        legend.LegendPadding *= dpiXscale;
        legend.LegendColumnSpacing *= dpiXscale;
        legend.LegendItemSpacing *= dpiXscale;
        legend.LegendMargin *= dpiXscale;

        Legends.Add(legend);
    }
}