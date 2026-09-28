// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Fork additions Copyright (C) GoGoGadget Hardware Monitor Contributors.

using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition: shown once, the first time any sensor is about to be
// shown in the gadget (MainForm gates this on sensorGadget.hasBeenConfigured
// - see WidgetDefaultsApplier for what "Use Default Values" actually adds).
internal static class FirstActivationDialog
{
    public enum Result
    {
        UseDefaults,
        StartFromScratch
    }

    public static Result Show()
    {
        using Form form = new Form
        {
            Text = "Set Up the Gadget",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            AutoScaleMode = AutoScaleMode.Font
        };
        form.Icon = EmbeddedResources.GetIcon("icon.ico");

        Label info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(356, 0),
            Text = "This is the first sensor being shown in the gadget. Start " +
                   "with a small default selection (CPU/RAM/disk usage, plus " +
                   "temperatures), or build it up yourself from scratch? Either " +
                   "way, everything is editable afterward."
        };
        Button startFromScratchButton = new Button { Text = "Start From Scratch", AutoSize = true };
        Button useDefaultsButton = new Button { Text = "Use Default Values", AutoSize = true };

        Result result = Result.StartFromScratch;

        useDefaultsButton.Click += delegate
        {
            result = Result.UseDefaults;
            form.Close();
        };

        startFromScratchButton.Click += delegate
        {
            result = Result.StartFromScratch;
            form.Close();
        };

        form.Controls.Add(info);
        form.Controls.Add(startFromScratchButton);
        form.Controls.Add(useDefaultsButton);
        form.AcceptButton = useDefaultsButton;
        form.CancelButton = startFromScratchButton;
        Theme.Current.Apply(form);

        // Fork fix: positions/ClientSize computed here, after every
        // control is parented - see GradientThresholdDialog's Load
        // handler for why (an AutoSize control's preferred size can't be
        // trusted before it inherits the form's real Font/DPI context).
        // This is the dialog shown at gadget startup, on first activation.
        form.Load += delegate
        {
            info.Location = new Point(12, 10);

            int y = info.Bottom + 14;
            startFromScratchButton.Location = new Point(12, y);
            useDefaultsButton.Location = new Point(178, y);

            form.ClientSize = new Size(380, y + useDefaultsButton.Height + 12);
            DebugLog.Write("DialogLayout", $"FirstActivationDialog: info.Height={info.Height} useDefaultsButton.Height={useDefaultsButton.Height} ClientSize={form.ClientSize}");
        };

        form.ShowDialog();

        return result;
    }
}
