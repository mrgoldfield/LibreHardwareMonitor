// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Fork additions Copyright (C) GoGoGadget Hardware Monitor Contributors.

using System.Collections.Generic;
using System.Drawing;
using LibreHardwareMonitor.Hardware;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition (Row Icons): a gadget-usable counterpart to
// HardwareTypeImage, for the per-sensor category icon (Load, Temperature,
// Fan, ...). Mirrors the same SensorType -> image mapping TypeNode already
// uses for the main window's tree, kept as its own small cache here rather
// than reworking TypeNode into a shared service, since TypeNode's version
// is entangled with building tree nodes and text labels this gadget-only
// use has no need for.
public class SensorTypeImage
{
    private readonly IDictionary<SensorType, Image> _images = new Dictionary<SensorType, Image>();

    private SensorTypeImage() { }

    public static SensorTypeImage Instance { get; } = new SensorTypeImage();

    public Image GetImage(SensorType sensorType)
    {
        if (_images.TryGetValue(sensorType, out Image image))
            return image;

        switch (sensorType)
        {
            case SensorType.Voltage:
                image = Utilities.EmbeddedResources.GetImage("voltage.png");
                break;
            case SensorType.Current:
                image = Utilities.EmbeddedResources.GetImage("voltage.png");
                break;
            case SensorType.Energy:
                image = Utilities.EmbeddedResources.GetImage("battery.png");
                break;
            case SensorType.Clock:
                image = Utilities.EmbeddedResources.GetImage("clock.png");
                break;
            case SensorType.Load:
                image = Utilities.EmbeddedResources.GetImage("load.png");
                break;
            case SensorType.Temperature:
                image = Utilities.EmbeddedResources.GetImage("temperature.png");
                break;
            case SensorType.Fan:
                image = Utilities.EmbeddedResources.GetImage("fan.png");
                break;
            case SensorType.Flow:
                image = Utilities.EmbeddedResources.GetImage("flow.png");
                break;
            case SensorType.Control:
                image = Utilities.EmbeddedResources.GetImage("control.png");
                break;
            case SensorType.Level:
                image = Utilities.EmbeddedResources.GetImage("level.png");
                break;
            case SensorType.Power:
                image = Utilities.EmbeddedResources.GetImage("power.png");
                break;
            case SensorType.Data:
                image = Utilities.EmbeddedResources.GetImage("data.png");
                break;
            case SensorType.SmallData:
                image = Utilities.EmbeddedResources.GetImage("data.png");
                break;
            case SensorType.Factor:
                image = Utilities.EmbeddedResources.GetImage("factor.png");
                break;
            case SensorType.Frequency:
                image = Utilities.EmbeddedResources.GetImage("clock.png");
                break;
            case SensorType.Throughput:
                image = Utilities.EmbeddedResources.GetImage("throughput.png");
                break;
            case SensorType.TimeSpan:
                image = Utilities.EmbeddedResources.GetImage("time.png");
                break;
            case SensorType.Timing:
                image = Utilities.EmbeddedResources.GetImage("time.png");
                break;
            case SensorType.Noise:
                image = Utilities.EmbeddedResources.GetImage("loudspeaker.png");
                break;
            case SensorType.Conductivity:
                image = Utilities.EmbeddedResources.GetImage("voltage.png");
                break;
            case SensorType.Humidity:
                image = Utilities.EmbeddedResources.GetImage("humidity.png");
                break;
            default:
                image = new Bitmap(1, 1);
                break;
        }

        _images.Add(sensorType, image);
        return image;
    }
}
