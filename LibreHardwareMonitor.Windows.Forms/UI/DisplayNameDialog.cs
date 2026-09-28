// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Fork additions Copyright (C) GoGoGadget Hardware Monitor Contributors.

using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition: a per-sensor display name used only by the gadget, kept
// deliberately separate from the main window's "Rename" (which edits
// ISensor.Name itself, so it also changes the CSV log header, tray
// label, and every other place the sensor's real name is used). This
// dialog only ever writes to gadget.displayName - the sensor's actual
// Name is never touched.
internal static class DisplayNameDialog
{
    public enum Result
    {
        Cancel,
        Save,
        Clear
    }

    public static Result Show(string sensorName, string currentDisplayName, out string displayName)
    {
        using Form form = new Form
        {
            Text = $"Display Name - {sensorName}",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            AutoScaleMode = AutoScaleMode.Font
        };
        form.Icon = EmbeddedResources.GetIcon("icon.ico");

        // AutoSize + MaximumSize (width only, unlimited height) instead of
        // a fixed Size: a non-AutoSize Label doesn't clip text taller than
        // its box, it draws past the bottom edge and over the next
        // control - see GradientThresholdDialog, which had the same bug.
        Label info = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(316, 0),
            Text = "Shown only in the gadget - the sensor's real name " +
                   "elsewhere (main window, CSV log, tray) is unchanged."
        };
        Label originalNameLabel = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(316, 0),
            Text = $"Original name: {sensorName}"
        };

        // Blank, not the real name, is the default so leaving it empty
        // reads as "no override" - Save with a blank box clears the
        // override and falls back to the original name, same as Clear.
        TextBox nameBox = new TextBox
        {
            Width = 316,
            Text = currentDisplayName ?? string.Empty
        };

        Button clearButton = new Button { Text = "Clear", AutoSize = true, DialogResult = DialogResult.Ignore };
        Button okButton = new Button { Text = "OK", AutoSize = true };
        Button cancelButton = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };

        Result result = Result.Cancel;
        string enteredName = null;

        okButton.Click += delegate
        {
            enteredName = nameBox.Text.Trim();
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
        form.Controls.Add(originalNameLabel);
        form.Controls.Add(nameBox);
        form.Controls.Add(clearButton);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        Theme.Current.Apply(form);

        // Fork fix: positions/ClientSize computed here, after every
        // control is parented - see GradientThresholdDialog's Load
        // handler for why (an AutoSize control's preferred size can't be
        // trusted before it inherits the form's real Font/DPI context).
        form.Load += delegate
        {
            info.Location = new Point(12, 10);

            int y = info.Bottom + 10;
            originalNameLabel.Location = new Point(12, y);

            y = originalNameLabel.Bottom + 6;
            nameBox.Location = new Point(12, y);

            y += 30;
            clearButton.Location = new Point(12, y);
            okButton.Location = new Point(178, y);
            cancelButton.Location = new Point(258, y);

            form.ClientSize = new Size(340, y + clearButton.Height + 12);
            DebugLog.Write("DialogLayout", $"DisplayNameDialog: info.Height={info.Height} clearButton.Height={clearButton.Height} ClientSize={form.ClientSize}");
        };

        form.ShowDialog();

        displayName = enteredName;
        return result;
    }
}
