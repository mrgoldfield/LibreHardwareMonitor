// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition (Phase 3): small numeric-entry dialog for a sensor's
// gradient thresholds (see BarColorResolver). Deliberately separate from
// TrySelectColor in SensorGadget.cs - that dialog picks a single static
// color, this one edits two numbers plus a "Clear" action, so sharing a
// form would mean branching one dialog into two shapes rather than
// keeping each simple.
internal static class GradientThresholdDialog
{
    public enum Result
    {
        Cancel,
        Save,
        Clear
    }

    public static Result Show(string sensorName, float? currentWarnAt, float? currentCritAt, out float warnAt, out float critAt)
    {
        using Form form = new Form
        {
            Text = $"Gradient Colors - {sensorName}",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            AutoScaleMode = AutoScaleMode.Font
        };
        form.Icon = EmbeddedResources.GetIcon("icon.ico");

        // AutoSize + MaximumSize (width only, unlimited height) instead of
        // a fixed Size: a non-AutoSize Label doesn't clip text that's
        // taller than its box, it just draws past the bottom edge and
        // over whatever control comes next. Everything below is laid out
        // relative to this label's actual (wrapped) height instead of a
        // guessed pixel value, so it stays correct across font sizes/DPI.
        Label info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(316, 0),
            Text = "Below the threshold the bar/number is green. Between " +
                   "the threshold and max it shifts green to red. At or " +
                   "above max it is solid red."
        };
        Label warnLabel = new Label { AutoSize = true, Text = "Threshold (start of shift):" };
        TextBox warnBox = new TextBox { Width = 140, Text = currentWarnAt?.ToString(CultureInfo.InvariantCulture) ?? string.Empty };
        Label critLabel = new Label { AutoSize = true, Text = "Max (full red):" };
        TextBox critBox = new TextBox { Width = 140, Text = currentCritAt?.ToString(CultureInfo.InvariantCulture) ?? string.Empty };
        Label errorLabel = new Label { AutoSize = false, Size = new Size(316, 18), ForeColor = Color.Firebrick, Text = string.Empty };
        Button clearButton = new Button { Text = "Clear", AutoSize = true, DialogResult = DialogResult.Ignore };
        Button okButton = new Button { Text = "OK", AutoSize = true };
        Button cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

        Result result = Result.Cancel;
        float parsedWarn = 0f;
        float parsedCrit = 0f;

        okButton.Click += delegate
        {
            if (!float.TryParse(warnBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedWarn) ||
                !float.TryParse(critBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out parsedCrit))
            {
                errorLabel.Text = "Enter numeric values for both fields.";
                return;
            }

            if (parsedCrit <= parsedWarn)
            {
                errorLabel.Text = "Max must be greater than the threshold.";
                return;
            }

            result = Result.Save;
            form.DialogResult = DialogResult.OK;
            form.Close();
        };

        clearButton.Click += delegate
        {
            result = Result.Clear;
            form.DialogResult = DialogResult.Ignore;
            form.Close();
        };

        form.Controls.Add(info);
        form.Controls.Add(warnLabel);
        form.Controls.Add(warnBox);
        form.Controls.Add(critLabel);
        form.Controls.Add(critBox);
        form.Controls.Add(errorLabel);
        form.Controls.Add(clearButton);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        Theme.Current.Apply(form);

        // Fork fix: positions/ClientSize are computed here, after every
        // control is parented, instead of at construction time - an
        // AutoSize control's preferred size (info's word-wrap height,
        // each button's Height) can only be trusted once it has inherited
        // the form's real Font/DPI context via Controls.Add. Computing
        // this beforehand (the original bug this dialog already had one
        // fix for - see the class-level comment) left the instruction
        // label under-sized and the button row clipped at some DPI/font
        // scales.
        form.Load += delegate
        {
            info.Location = new Point(12, 10);

            int y = info.Bottom + 10;
            warnLabel.Location = new Point(12, y + 3);
            warnBox.Location = new Point(180, y);

            y += 30;
            critLabel.Location = new Point(12, y + 3);
            critBox.Location = new Point(180, y);

            y += 30;
            errorLabel.Location = new Point(12, y);

            y += 26;
            clearButton.Location = new Point(12, y);
            okButton.Location = new Point(178, y);
            cancelButton.Location = new Point(258, y);

            form.ClientSize = new Size(340, y + clearButton.Height + 12);
            DebugLog.Write("DialogLayout", $"GradientThresholdDialog: info.Height={info.Height} clearButton.Height={clearButton.Height} ClientSize={form.ClientSize}");
        };

        form.ShowDialog();

        warnAt = parsedWarn;
        critAt = parsedCrit;
        return result;
    }
}
