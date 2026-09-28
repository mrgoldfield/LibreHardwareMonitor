// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Fork additions Copyright (C) GoGoGadget Hardware Monitor Contributors.

using System.Drawing;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

// Fork addition: a short onboarding screen for the gadget's fork-specific
// features (reordering, colors, alarms, themes, ...). Shown once per app
// launch, gated by MainForm's mainForm.showStartupGuide setting rather
// than a one-time flag like sensorGadget.hasBeenConfigured, since the
// user can turn it back on from the Help menu at any point rather than
// only ever seeing it once.
internal static class StartupGuideDialog
{
    // Caller (MainForm) persists mainForm.showStartupGuide from this -
    // this dialog never touches PersistentSettings itself, matching
    // every other dialog in this fork (GradientThresholdDialog,
    // DisplayNameDialog, GroupSpacingDialog).
    public static bool Show(bool initiallyShowOnStartup)
    {
        using Form form = new Form
        {
            Text = "Welcome to GoGoGadget Hardware Monitor",
            FormBorderStyle = FormBorderStyle.FixedDialog,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            AutoScaleMode = AutoScaleMode.Font
        };
        form.Icon = EmbeddedResources.GetIcon("icon.ico");

        Label heading = new Label
        {
            AutoSize = true,
            Font = new Font(SystemFonts.MessageBoxFont, FontStyle.Bold),
            Text = "GoGoGadget Hardware Monitor"
        };
        Label subheading = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Text = "A fork of LibreHardwareMonitor with a fully customizable Sensor Gadget:"
        };

        // AutoSize + MaximumSize (width only, unlimited height) instead of
        // a fixed Size - see DisplayNameDialog's comment on why: a
        // non-AutoSize Label draws past its box instead of clipping.
        Label guide = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(360, 0),
            Text = "•  Right-click a sensor in the tree, then \"Add to Full Gadget\", to add it.\n" +
                   "•  Right-click a sensor in the gadget to reorder it, set a bar/name/text color, or arm alarm coloring.\n" +
                   "•  Double-click the gadget to switch between Mini and Full mode.\n" +
                   "•  Drag any edge of the gadget to resize it.\n" +
                   "•  Right-click the gadget's background for Theme/Profile export, scale, and more.\n\n" +
                   "Come back to this guide any time from Help → Startup Guide."
        };

        CheckBox showOnStartupCheckBox = new CheckBox
        {
            AutoSize = true,
            Text = "Show this guide on startup",
            Checked = initiallyShowOnStartup
        };

        Button okButton = new Button { Text = "OK", AutoSize = true };

        bool showOnStartup = initiallyShowOnStartup;

        okButton.Click += delegate
        {
            showOnStartup = showOnStartupCheckBox.Checked;
            form.DialogResult = DialogResult.OK;
            form.Close();
        };

        form.Controls.Add(heading);
        form.Controls.Add(subheading);
        form.Controls.Add(guide);
        form.Controls.Add(showOnStartupCheckBox);
        form.Controls.Add(okButton);
        form.AcceptButton = okButton;
        form.CancelButton = okButton;
        Theme.Current.Apply(form);

        // Fork fix: positions/ClientSize computed here, after every
        // control is parented - see GradientThresholdDialog's Load
        // handler for why (an AutoSize control's preferred size can't be
        // trusted before it inherits the form's real Font/DPI context).
        form.Load += delegate
        {
            heading.Location = new Point(12, 10);

            int y = heading.Bottom + 4;
            subheading.Location = new Point(12, y);

            y = subheading.Bottom + 10;
            guide.Location = new Point(12, y);

            y = guide.Bottom + 14;
            showOnStartupCheckBox.Location = new Point(12, y);

            y = showOnStartupCheckBox.Bottom + 14;
            okButton.Location = new Point(297, y);

            form.ClientSize = new Size(384, y + okButton.Height + 12);
            DebugLog.Write("DialogLayout", $"StartupGuideDialog: guide.Height={guide.Height} okButton.Height={okButton.Height} ClientSize={form.ClientSize}");
        };

        form.ShowDialog();

        return showOnStartup;
    }
}
