// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Fork additions Copyright (C) GoGoGadget Hardware Monitor Contributors.

using System.Drawing;
using System.Globalization;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition (Group Spacing, user-requested 2026-09-28): a small
// numeric-entry dialog for the extra gap drawn between hardware groups
// (see SensorGadget's group-spacing addition in OnPaint/ComputeContentHeight).
// Modeled on GradientThresholdDialog/DisplayNameDialog rather than sharing
// either - this edits one raw pixel count with its own valid range, not a
// color or free text.
internal static class GroupSpacingDialog
{
    public enum Result
    {
        Cancel,
        Save,
        Clear
    }

    private const int MaxPixels = 500;

    public static Result Show(int currentValue, out int value)
    {
        using Form form = new Form
        {
            Text = "Group Spacing",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            AutoScaleMode = AutoScaleMode.Font
        };
        form.Icon = EmbeddedResources.GetIcon("icon.ico");

        // AutoSize + MaximumSize (width only, unlimited height) instead of
        // a fixed Size - see GradientThresholdDialog's Load handler for
        // why (an AutoSize Label's wrapped height can't be trusted before
        // it inherits the form's real Font/DPI context).
        Label info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(316, 0),
            Text = "Extra vertical space drawn between each hardware group " +
                   "(CPU, RAM, Storage, ...), on top of normal row spacing. " +
                   "0 means no extra gap."
        };
        Label pixelsLabel = new Label { AutoSize = true, Text = "Pixels:" };
        TextBox pixelsBox = new TextBox { Width = 140, Text = currentValue.ToString(CultureInfo.InvariantCulture) };
        Label errorLabel = new Label { AutoSize = false, Size = new Size(316, 18), ForeColor = Color.Firebrick, Text = string.Empty };

        Button clearButton = new Button { Text = "Clear", AutoSize = true, DialogResult = DialogResult.Ignore };
        Button okButton = new Button { Text = "OK", AutoSize = true };
        Button cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

        Result result = Result.Cancel;
        int parsedValue = 0;

        okButton.Click += delegate
        {
            if (!int.TryParse(pixelsBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsedValue) || parsedValue < 0)
            {
                errorLabel.Text = "Enter a whole number of 0 or more.";
                return;
            }

            if (parsedValue > MaxPixels)
            {
                errorLabel.Text = $"Enter a value of {MaxPixels} or less.";
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
        form.Controls.Add(pixelsLabel);
        form.Controls.Add(pixelsBox);
        form.Controls.Add(errorLabel);
        form.Controls.Add(clearButton);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        Theme.Current.Apply(form);

        // Fork fix: positions/ClientSize computed here, after every
        // control is parented - see GradientThresholdDialog's Load
        // handler for why.
        form.Load += delegate
        {
            info.Location = new Point(12, 10);

            int y = info.Bottom + 10;
            pixelsLabel.Location = new Point(12, y + 3);
            pixelsBox.Location = new Point(180, y);

            y += 30;
            errorLabel.Location = new Point(12, y);

            y += 26;
            clearButton.Location = new Point(12, y);
            okButton.Location = new Point(178, y);
            cancelButton.Location = new Point(258, y);

            form.ClientSize = new Size(340, y + clearButton.Height + 12);
            DebugLog.Write("DialogLayout", $"GroupSpacingDialog: info.Height={info.Height} clearButton.Height={clearButton.Height} ClientSize={form.ClientSize}");
        };

        form.ShowDialog();

        value = parsedValue;
        return result;
    }
}
