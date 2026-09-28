// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael Möller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Windows.Forms;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using LibreHardwareMonitor.Hardware;
using LibreHardwareMonitor.Windows.Forms.UI.Themes;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public class SensorGadget : Gadget
{
    private const int TopBorder = 6;
    private const int BottomBorder = 7;
    private const int LeftBorder = 6;
    private const int RightBorder = 7;

    private readonly UnitManager _unitManager;
    private Image _back = Utilities.EmbeddedResources.GetImage("gadget.png");
    private Image _image;
    private Image _fore;
    private Image _barBack = Utilities.EmbeddedResources.GetImage("barback.png");
    private Image _barFore = Utilities.EmbeddedResources.GetImage("bar.png");
    private bool _customBarBack;
    private bool _customBarFore;
    private Image _backTinted;

    // Fork addition (Phase 2/3 - per-sensor color/gradient): bar tints and
    // text brushes used to be computed once for the gadget-wide font
    // color. Now that each sensor can resolve to a different color (a
    // per-sensor override, or a gradient bucket - see BarColorResolver),
    // both are cached per resolved Color instead, built lazily on first
    // use. Bounded in practice: a real gadget only ever touches a few
    // distinct colors (the default font color, a handful of manual
    // overrides, up to 32 gradient buckets), never one per paint call.
    private readonly Dictionary<Color, Image> _barBackTintCache = new Dictionary<Color, Image>();
    private readonly Dictionary<Color, Image> _barForeTintCache = new Dictionary<Color, Image>();
    private readonly Dictionary<Color, SolidBrush> _colorBrushCache = new Dictionary<Color, SolidBrush>();

    private Image _background = new Bitmap(1, 1);
    private bool _backgroundDirty = true;
    private readonly float _scale;

    // Fork addition (scale multiplier): a coarse user multiplier on top
    // of _scale (the fixed DPI ratio) and the font-size presets, for
    // sizing the whole gadget up/down without hunting for the "right"
    // font size - see SetFontSize.
    private float _scaleMultiplier;
    private float _fontSize;
    private double _scaledFontSize;

    // Fork addition: true once the user has actually dragged the
    // gadget's right edge (see GadgetWindow.UserResized) - persisted so
    // a manually-chosen width survives a restart. Until then, Resize()
    // keeps recomputing the width from whatever sensors/hardware are
    // actually shown (ComputeDefaultWidth), instead of the old
    // behavior of taking a possibly-stale persisted width from a
    // previous run - which is what kept width fixes from ever taking
    // effect for an existing settings file (only construction's
    // ComputeDefaultWidth ran before any hardware was known, so a
    // narrow width computed once early on kept getting persisted right
    // back by SizeChanged and reloaded forever after) - reported by the
    // user 2026-09-18, not yet visually reconfirmed fixed on Windows.
    private bool _widthManuallySet;
    private int _iconSize;
    private int _hardwareLineHeight;
    private int _sensorLineHeight;
    private int _rightMargin;
    private int _leftMargin;
    private int _topMargin;
    private int _bottomMargin;
    private int _progressWidth;

    // Fork addition (vertical drag-to-resize): mirrors _widthManuallySet
    // but for height - true once the user has dragged the gadget's top
    // or bottom edge. _lineSpacingExtra is extra pixels added to every
    // row-advance step (each hardware header and each sensor row) so a
    // taller/shorter drag reads as denser/looser line spacing rather
    // than changing font/icon/bar size - see ComputeContentHeight. Until
    // dragged, height keeps auto-fitting to content with no extra
    // spacing, same as width's ComputeDefaultWidth behavior.
    private bool _lineSpacingManuallySet;
    private int _lineSpacingExtra;

    // Fork change (hardware-group reordering): a SortedDictionary can't
    // have its order safely mutated after items are inserted (it assumes
    // the comparer's results never change while keys are present, which
    // is exactly what Move Group Up/Down needs to do), so hardware
    // groups are looked up in a plain Dictionary and rendered in the
    // explicit order kept in _hardwareOrder instead.
    private readonly IDictionary<IHardware, IList<ISensor>> _sensors = new Dictionary<IHardware, IList<ISensor>>();
    private readonly List<IHardware> _hardwareOrder = new List<IHardware>();
    private static readonly HardwareComparer DefaultHardwareComparer = new HardwareComparer();
    private readonly PersistentSettings _settings;

    // Fork change (2026-09-28): was a single UserOption/setting shared by
    // both modes. Split into two independently-persisted settings
    // (HardwareNamesEnabled resolves which one based on _miniMode) so
    // Mini and Full can default differently - Full showing hardware
    // names on and Mini off is one of the few things that made toggling
    // Mini/Full look identical by default (see the "Made Mini/Full
    // defaults actually distinguishable" CHANGELOG entry for the
    // sensor-selection half of that same problem). _hardwareNamesItem is
    // the single checkbox both settings share; its Checked state is
    // manually resynced to whichever one is currently active (here and
    // in _miniMode.Changed/ApplyThemeFromSettings) instead of a
    // UserOption owning it, since UserOption is wired to exactly one
    // fixed settings key for its whole lifetime.
    private readonly ToolStripMenuItem _hardwareNamesItem = new ToolStripMenuItem("Hardware Names");

    // Full defaults to true (was the fork's only prior behavior - a
    // single always-on-by-default UserOption), Mini defaults to false.
    // Written on _hardwareNamesItem's own click, and re-read (with the
    // checkbox resynced) whenever mode changes or a Theme/Profile
    // imports - see the field comment above and _miniMode.Changed/
    // ApplyThemeFromSettings.
    private bool HardwareNamesEnabled
    {
        get => _settings.GetValue(_miniMode.Value ? "sensorGadget.HardwarenamesMini" : "sensorGadget.HardwarenamesFull", !_miniMode.Value);
        set => _settings.SetValue(_miniMode.Value ? "sensorGadget.HardwarenamesMini" : "sensorGadget.HardwarenamesFull", value);
    }

    private UserOption _gradientBarBackground;

    // Fork addition: Mini shows only sensors explicitly added to it
    // (SensorGadgetItemSettings.IsMini); Full shows everything. Toggled
    // by double-clicking the gadget (MouseDoubleClick, already exposed by
    // GadgetWindow/Gadget) or via this menu checkbox - both just flip the
    // same UserOption. See AddToMini/RemoveFromMini for the invariant
    // that Mini membership always implies Full membership.
    private UserOption _miniMode;

    // Fork addition (Phase 1 - reordering): the on-screen y-range of each
    // sensor row from the most recent paint, used to figure out which
    // sensor a right-click landed on so the context menu can offer
    // Move Up/Move Down for it. Rebuilt every OnPaint.
    private readonly List<(ISensor Sensor, int Top, int Bottom)> _rowBounds = new List<(ISensor, int, int)>();

    // Fork addition (hardware-group reordering): same idea as
    // _rowBounds, but for the hardware-name header rows, so a right-click
    // on "CPU" / "GPU" / etc. can offer Move Group Up/Down instead of the
    // per-sensor items.
    private readonly List<(IHardware Hardware, int Top, int Bottom)> _hardwareHeaderBounds = new List<(IHardware, int, int)>();
    private readonly ToolStripMenuItem _moveUpItem = new ToolStripMenuItem("Move Up");
    private readonly ToolStripMenuItem _moveDownItem = new ToolStripMenuItem("Move Down");
    private readonly ToolStripMenuItem _moveGroupUpItem = new ToolStripMenuItem("Move Group Up");
    private readonly ToolStripMenuItem _moveGroupDownItem = new ToolStripMenuItem("Move Group Down");
    private readonly ToolStripSeparator _sensorMenuSeparator = new ToolStripSeparator();

    // Fork addition (menu redesign): breaks the per-sensor block into
    // Move Up/Down, then appearance/content settings, then the
    // destructive-ish Remove action - was one flat run of 9 items with
    // no internal separators before. A distinct ToolStripSeparator
    // instance per gap, since the same instance can't appear twice in
    // one ContextMenuStrip.Items collection.
    private readonly ToolStripSeparator _sensorMenuMoveSeparator = new ToolStripSeparator();
    private readonly ToolStripSeparator _sensorMenuRemoveSeparator = new ToolStripSeparator();

    // Fork addition: a non-clickable header at the very top of the
    // per-row context menu naming exactly what's about to be modified -
    // deliberately the sensor's/hardware's real name (ISensor.Name/
    // IHardware.Name), never ResolveSensorDisplayName's gadget-only
    // override, so this stays a reliable "what is this really" readout
    // even when a custom Display Name no longer resembles it. Enabled=
    // false (grayed out, unclickable) rather than a ToolStripLabel -
    // simpler to keep looking consistent with the rest of this
    // ContextMenuStrip's rendering (Theme/ThemedToolStripRenderer etc.)
    // without a second control type to theme.
    private readonly ToolStripMenuItem _contextMenuDeviceItem = new ToolStripMenuItem { Enabled = false };
    private readonly ToolStripMenuItem _contextMenuSensorItem = new ToolStripMenuItem { Enabled = false };
    private readonly ToolStripSeparator _contextMenuInfoSeparator = new ToolStripSeparator();
    private IHardware _contextMenuHardware;

    // Fork addition (Phase 2/3 - per-sensor color/gradient): "Bar Color"
    // and "Gradient Colors..." items on the same per-row context menu
    // Phase 1 introduced, rather than a second mechanism - see
    // CLAUDE.md's ContextMenuOpening note.
    private readonly ToolStripMenuItem _barColorItem = new ToolStripMenuItem("Bar Color");
    private readonly ToolStripMenuItem _barColorChooseItem = new ToolStripMenuItem("Choose...");
    private readonly ToolStripMenuItem _barColorAutoItem = new ToolStripMenuItem("Auto");
    private readonly ToolStripMenuItem _gradientItem = new ToolStripMenuItem("Gradient Colors...");

    // Fork addition (Text Color): lets a sensor's drawn number use a
    // different color than its bar/gradient - see ResolveSensorTextColor.
    private readonly ToolStripMenuItem _textColorItem = new ToolStripMenuItem("Text Color");
    private readonly ToolStripMenuItem _textColorChooseItem = new ToolStripMenuItem("Choose...");
    private readonly ToolStripMenuItem _textColorAutoItem = new ToolStripMenuItem("Auto");

    // Fork addition (Name Color): lets a sensor's drawn DISPLAY NAME (the
    // label, e.g. "CPU Total") use a different color than the gadget's
    // normal font color - independent of Bar Color/Text Color, which
    // only affect the bar and the drawn number - see
    // ResolveSensorNameColor. Unlike Bar/Text Color, "Choose.../Auto"
    // each open a scope submenu (This Sensor/This Group/All Sensors)
    // rather than acting on just the right-clicked sensor - user
    // request 2026-09-22: naming a color once and reusing it across a
    // whole hardware group or the whole gadget is common enough to be
    // worth a dedicated scope choice, applied as a one-time bulk write
    // to each affected sensor's own gadget.nameColor key (no new
    // group-/gadget-level setting tier - see ApplyNameColorScope).
    private readonly ToolStripMenuItem _nameColorItem = new ToolStripMenuItem("Name Color");
    private readonly ToolStripMenuItem _nameColorChooseItem = new ToolStripMenuItem("Choose...");
    private readonly ToolStripMenuItem _nameColorChooseThisItem = new ToolStripMenuItem("This Sensor");
    private readonly ToolStripMenuItem _nameColorChooseGroupItem = new ToolStripMenuItem("This Group");
    private readonly ToolStripMenuItem _nameColorChooseAllItem = new ToolStripMenuItem("All Sensors");
    private readonly ToolStripMenuItem _nameColorAutoItem = new ToolStripMenuItem("Auto");
    private readonly ToolStripMenuItem _nameColorAutoThisItem = new ToolStripMenuItem("This Sensor");
    private readonly ToolStripMenuItem _nameColorAutoGroupItem = new ToolStripMenuItem("This Group");
    private readonly ToolStripMenuItem _nameColorAutoAllItem = new ToolStripMenuItem("All Sensors");

    // Fork addition (Value Display): only shown for bar-capable sensor
    // types (Load/Control/Level/Humidity - see IsBarCapableSensorType).
    // Lets a sensor that used to only ever draw as a bar show its number
    // instead, or both together.
    private readonly ToolStripMenuItem _valueDisplayItem = new ToolStripMenuItem("Value Display");
    private readonly ToolStripMenuItem _valueDisplayBarItem = new ToolStripMenuItem("Bar");
    private readonly ToolStripMenuItem _valueDisplayPercentItem = new ToolStripMenuItem("%");
    private readonly ToolStripMenuItem _valueDisplayBothItem = new ToolStripMenuItem("Both");

    // Fork addition: a gadget-only display name, separate from the main
    // window's own "Rename" (left untouched - that one edits ISensor.Name
    // itself and is out of scope here).
    private readonly ToolStripMenuItem _displayNameItem = new ToolStripMenuItem("Display Name...");

    // Fork addition: bottom-most item in the per-row menu - same effect
    // as unchecking "Show in Gadget" from the main window's tree, just
    // reachable without leaving the gadget.
    private readonly ToolStripMenuItem _removeFromWidgetItem = new ToolStripMenuItem("Remove from Widget");
    private ISensor _contextMenuSensor;

    // Fork addition (Theme/Profile export): kept as fields (rather than
    // constructor locals) so ApplyThemeFromSettings can refresh their
    // Checked marks after an import, via each item's Tag - see
    // SyncCheckedByTag.
    private readonly ToolStripMenuItem _fontSizeMenu = new ToolStripMenuItem("Font Size");
    private readonly ToolStripMenuItem _opacityMenu = new ToolStripMenuItem("Opacity");
    private readonly ToolStripMenuItem _scaleMenu = new ToolStripMenuItem("Scale");

    // Fork addition (Row Icons, user-requested 2026-09-28): a gadget-wide
    // choice to mark the first sensor row of each hardware group with its
    // hardware-type icon (HardwareTypeImage - the same icon the group's
    // header row already shows, when Hardware Names is on). "First" is
    // positional, not tied to a specific sensor - see isFirstSensorInGroup
    // in OnPaint - so it follows Move Up/Move Down reordering
    // automatically instead of sticking to whichever sensor happened to
    // be first originally. "Icons Only" drops that one row's text name in
    // favor of the icon, and - per explicit user choice, since rows past
    // the first aren't meant to be individually identified in this mode -
    // leaves every other row in the group with no name and no icon at
    // all, not just the first. Off by default - a purely opt-in cosmetic
    // change, like Mini Mode/gradient bar background before it.
    private enum RowIconMode
    {
        Off,
        WithName,
        IconsOnly
    }

    private RowIconMode _rowIconMode;
    private readonly ToolStripMenuItem _rowIconsMenu = new ToolStripMenuItem("Row Icons");

    private static RowIconMode GetRowIconMode(PersistentSettings settings)
    {
        string raw = settings.GetValue("sensorGadget.RowIcons", nameof(RowIconMode.Off));
        return Enum.TryParse(raw, out RowIconMode mode) ? mode : RowIconMode.Off;
    }

    // Fork addition (Group Spacing, user-requested 2026-09-28): an
    // optional extra gap, in raw pixels, drawn between each hardware
    // group - on top of normal row spacing, and independent of the
    // vertical drag-to-resize feature's per-row extraPerStep (which
    // stretches every row/header step evenly; this instead adds a fixed
    // amount only at each group boundary, whether or not Hardware Names
    // is on - see the group loop in OnPaint/ComputeContentHeight, which
    // must stay in sync with each other exactly like extraPerStep
    // already does there). Deliberately excluded from
    // ThemeProfileManager.ThemeKeys, same reasoning as
    // lineSpacingExtra/widthConfigured: it's a raw pixel count tuned to
    // this gadget's current size/scale, not a proportional style choice
    // that would still look right after being copied onto a differently
    // sized/scaled gadget.
    private int _groupSpacingExtra;
    private readonly ToolStripMenuItem _groupSpacingItem = new ToolStripMenuItem("Group Spacing...");

    private Font _largeFont;
    private Font _smallFont;
    private Brush _textBrush;
    private StringFormat _stringFormat;
    private StringFormat _trimStringFormat;
    private StringFormat _alignRightStringFormat;
    private Color _fontColor;
    private Color _backgroundColor;

    public SensorGadget(IComputer computer, PersistentSettings settings, UnitManager unitManager)
    {
        _unitManager = unitManager;
        _settings = settings;
        computer.HardwareAdded += HardwareAdded;
        computer.HardwareRemoved += HardwareRemoved;

        _stringFormat = new StringFormat { FormatFlags = StringFormatFlags.NoWrap };
        _trimStringFormat = new StringFormat { Trimming = StringTrimming.EllipsisCharacter, FormatFlags = StringFormatFlags.NoWrap };
        _alignRightStringFormat = new StringFormat { Alignment = StringAlignment.Far, FormatFlags = StringFormatFlags.NoWrap };

        if (File.Exists("gadget_background.png"))
        {
            try
            {
                Image newBack = new Bitmap("gadget_background.png");
                _back.Dispose();
                _back = newBack;
            }
            catch { }
        }

        if (File.Exists("gadget_image.png"))
        {
            try
            {
                _image = new Bitmap("gadget_image.png");
            }
            catch { }
        }

        if (File.Exists("gadget_foreground.png"))
        {
            try
            {
                _fore = new Bitmap("gadget_foreground.png");
            }
            catch { }
        }

        if (File.Exists("gadget_bar_background.png"))
        {
            try
            {
                Image newBarBack = new Bitmap("gadget_bar_background.png");
                _barBack.Dispose();
                _barBack = newBarBack;
                _customBarBack = true;
            }
            catch { }
        }

        if (File.Exists("gadget_bar_foreground.png"))
        {
            try
            {
                Image newBarColor = new Bitmap("gadget_bar_foreground.png");
                _barFore.Dispose();
                _barFore = newBarColor;
                _customBarFore = true;
            }
            catch { }
        }

        Location = new Point(settings.GetValue("sensorGadget.Location.X", 100), settings.GetValue("sensorGadget.Location.Y", 100));
        LocationChanged += delegate
        {
            settings.SetValue("sensorGadget.Location.X", Location.X);
            settings.SetValue("sensorGadget.Location.Y", Location.Y);
        };

        // get the custom to default dpi ratio
        using (Bitmap b = new Bitmap(1, 1))
        {
            _scale = b.HorizontalResolution / 96.0f;
        }

        _scaleMultiplier = settings.GetValue("sensorGadget.ScaleMultiplier", 1.0f);

        // Fork fix (Gemini review Finding 3, see from_gemini_to_claude.md):
        // these must be read *before* SetFontSize, not after -
        // SetFontSize's own Resize(ComputeDefaultWidth(...)) call runs
        // immediately and needs _lineSpacingManuallySet/_lineSpacingExtra
        // already populated, or the placeholder ("no sensors yet") gadget
        // briefly renders at natural (un-spaced) height until the first
        // Add()/Remove() call recomputes it for real.
        //
        // Fork fix: only reapply a persisted width if the user actually
        // chose it by dragging - see _widthManuallySet. Otherwise leave
        // the auto-fit width SetFontSize is about to compute alone; no
        // hardware is known yet at this point in the constructor, so
        // it'll be recomputed for real (see Resize()) as sensors get
        // added right after this.
        _widthManuallySet = settings.GetValue("sensorGadget.widthConfigured", false);
        _lineSpacingManuallySet = settings.GetValue("sensorGadget.lineSpacingConfigured", false);
        _lineSpacingExtra = settings.GetValue("sensorGadget.lineSpacingExtra", 0);
        DebugLog.Write("Resize", $"SensorGadget ctor: widthConfigured(persisted)={_widthManuallySet} persistedWidth={settings.GetValue("sensorGadget.Width", -1)} lineSpacingConfigured(persisted)={_lineSpacingManuallySet} persistedLineSpacingExtra={_lineSpacingExtra}");

        // Fork addition (vertical drag-to-resize): snap a raw drag
        // height to a whole number of row-spacing steps before
        // GadgetWindow ever delivers it as Size/UserResized - see
        // GadgetWindow.SnapHeight and SnapHeightToRowSpacing below.
        SnapHeight = SnapHeightToRowSpacing;

        SetFontSize(settings.GetValue("sensorGadget.FontSize", 7.5f));
        DebugLog.Write("Resize", $"SensorGadget ctor: sizeAfterSetFontSize={Size}");
        if (_widthManuallySet)
            Resize(settings.GetValue("sensorGadget.Width", Size.Width));

        // Fork addition (vertical drag-to-resize): a drag only ever
        // changes one dimension at a time (the OS constrains it to
        // whichever edge - Left/Right or Top/Bottom - the cursor was
        // over, see the HitTest handler below), so previousSize tells
        // us which one actually moved. Width keeps its existing
        // behavior (persist the dragged width verbatim); height instead
        // solves for how much extra space to add per row so the same
        // density survives a later Add()/Remove() recomputing height
        // from content - see ComputeContentHeight. Thanks to SnapHeight
        // above, Size.Height here always already divides evenly into
        // naturalHeight + stepCount*extra, so this recomputation is
        // exact, not approximate (Gemini review Finding 2).
        UserResized += delegate (object sender, Size previousSize)
        {
            DebugLog.Write("Resize", $"SensorGadget: UserResized fired, currentSize={Size}, previousSize={previousSize}");

            if (Size.Width != previousSize.Width)
            {
                _widthManuallySet = true;
                settings.SetValue("sensorGadget.widthConfigured", true);
            }

            if (Size.Height != previousSize.Height)
            {
                int naturalHeight = ComputeContentHeight(0, out int stepCount);
                if (stepCount > 0)
                {
                    // Fork fix (Gemini review Finding 5): AwayFromZero,
                    // not the default ToEven ("banker's rounding") - so
                    // this and SnapHeightToRowSpacing's identical
                    // computation can never disagree at an exact
                    // half-step boundary.
                    int extra = (int)Math.Round((Size.Height - naturalHeight) / (double)stepCount, MidpointRounding.AwayFromZero);
                    // Fork fix (Gemini review Finding 6): re-clamp
                    // against the *live* _sensorLineHeight, not whatever
                    // it was when originally dragged - a smaller font
                    // size or Scale since then means a smaller row
                    // height, and a stale negative extra could overlap
                    // rows that got shorter without ever being clamped
                    // to fit.
                    _lineSpacingExtra = Math.Max(-(_sensorLineHeight - 2), extra);
                    _lineSpacingManuallySet = true;
                    settings.SetValue("sensorGadget.lineSpacingConfigured", true);
                    settings.SetValue("sensorGadget.lineSpacingExtra", _lineSpacingExtra);
                    DebugLog.Write("Resize", $"SensorGadget: vertical drag -> naturalHeight={naturalHeight} stepCount={stepCount} lineSpacingExtra={_lineSpacingExtra}");
                    // No Redraw() here (Gemini review Finding 1): SizeChanged
                    // fires right after this and its own handler already
                    // redraws - a second call here painted an intermediate
                    // frame with the stale _lineSpacingExtra every drag tick.
                }
            }
        };

        // Fork addition (menu redesign, user-requested 2026-09-28): the
        // gadget-wide section below groups related settings together -
        // row labeling (Hardware Names/Row Icons), text/size appearance,
        // background/bar appearance, layout mode, window behavior, then
        // persistence actions - instead of the original flat, ungrouped
        // list. "Hide/Show Main Window" moved to the very top per
        // explicit follow-up, since it's a navigation action rather than
        // a gadget setting and got lost at the bottom of a long menu.
        // Note this is the top of the *gadget-wide* section specifically:
        // when the right-click landed on a sensor row, UpdateSensorMenuItems
        // inserts that row's own Device/Sensor/actions block above
        // everything here (see its own grouping there), since those items
        // need a specific sensor and can't live in this always-present
        // section.
        ContextMenuStrip contextMenuStrip = new ContextMenuStrip();
        ToolStripMenuItem hideShowItem = new ToolStripMenuItem("Hide/Show Main Window");
        contextMenuStrip.Items.Add(hideShowItem);
        contextMenuStrip.Items.Add(new ToolStripSeparator());

        contextMenuStrip.Items.Add(_hardwareNamesItem);

        _rowIconMode = GetRowIconMode(settings);
        (RowIconMode value, string label)[] rowIconOptions =
        {
            (RowIconMode.Off, "Off"),
            (RowIconMode.WithName, "Icons + Name"),
            (RowIconMode.IconsOnly, "Icons Only")
        };
        foreach ((RowIconMode value, string label) in rowIconOptions)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label) { Checked = _rowIconMode == value, Tag = value };
            item.Click += delegate
            {
                _rowIconMode = value;
                settings.SetValue("sensorGadget.RowIcons", value.ToString());
                SyncCheckedByTag(_rowIconsMenu, value);
                Resize();
            };
            _rowIconsMenu.DropDownItems.Add(item);
        }
        contextMenuStrip.Items.Add(_rowIconsMenu);

        _groupSpacingExtra = settings.GetValue("sensorGadget.GroupSpacingExtra", 0);
        _groupSpacingItem.Click += delegate
        {
            GroupSpacingDialog.Result result = GroupSpacingDialog.Show(_groupSpacingExtra, out int newValue);
            switch (result)
            {
                case GroupSpacingDialog.Result.Save:
                    _groupSpacingExtra = newValue;
                    settings.SetValue("sensorGadget.GroupSpacingExtra", _groupSpacingExtra);
                    Resize();
                    break;
                case GroupSpacingDialog.Result.Clear:
                    _groupSpacingExtra = 0;
                    settings.Remove("sensorGadget.GroupSpacingExtra");
                    Resize();
                    break;
            }
        };
        contextMenuStrip.Items.Add(_groupSpacingItem);
        contextMenuStrip.Items.Add(new ToolStripSeparator());

        for (int i = 0; i < 5; i++)
        {
            float size;
            string name;
            switch (i)
            {
                case 0: size = 6.5f; name = "Small"; break;
                case 1: size = 7.5f; name = "Medium"; break;
                case 2: size = 9f; name = "Large"; break;
                case 3: size = 11f; name = "Very Large"; break;
                case 4: size = 22f; name = "Extremely Large"; break;
                default: throw new NotImplementedException();
            }

            ToolStripMenuItem item = new ToolStripMenuItem(name) { Checked = _fontSize == size, Tag = size };
            item.Click += delegate
            {
                SetFontSize(size);
                settings.SetValue("sensorGadget.FontSize", size);
                SyncCheckedByTag(_fontSizeMenu, size);
            };
            _fontSizeMenu.DropDownItems.Add(item);
        }
        contextMenuStrip.Items.Add(_fontSizeMenu);

        // Fork addition (scale multiplier): sizes the whole gadget -
        // font, icons, margins, bar width - up or down together, on top
        // of whichever Font Size preset is selected above.
        (float value, string label)[] scaleOptions =
        {
            (1.0f, "100%"),
            (1.25f, "125%"),
            (1.5f, "150%"),
            (2.0f, "200%")
        };
        foreach ((float value, string label) in scaleOptions)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(label) { Checked = _scaleMultiplier == value, Tag = value };
            item.Click += delegate
            {
                _scaleMultiplier = value;
                settings.SetValue("sensorGadget.ScaleMultiplier", _scaleMultiplier);
                SetFontSize(_fontSize);
                Redraw();
                SyncCheckedByTag(_scaleMenu, value);
            };
            _scaleMenu.DropDownItems.Add(item);
        }
        contextMenuStrip.Items.Add(_scaleMenu);

        Color fontColor = settings.GetValue("sensorGadget.FontColor", Color.White);
        SetFontColor(fontColor);

        ToolStripMenuItem fontColorMenu = new ToolStripMenuItem("Font Color");
        ToolStripItem chooseFontColorItem = new ToolStripMenuItem("Choose...");
        chooseFontColorItem.Click += delegate
        {
            if (TrySelectColor(fontColor, out Color selectedColor))
            {
                fontColor = selectedColor;
                SetFontColor(fontColor);
                settings.SetValue("sensorGadget.FontColor", fontColor);
                Redraw();
            }
        };
        fontColorMenu.DropDownItems.Add(chooseFontColorItem);

        ToolStripItem defaultFontColorItem = new ToolStripMenuItem("Default");
        defaultFontColorItem.Click += delegate
        {
            fontColor = Color.White;
            SetFontColor(fontColor);
            settings.Remove("sensorGadget.FontColor");
            Redraw();
        };
        fontColorMenu.DropDownItems.Add(defaultFontColorItem);
        contextMenuStrip.Items.Add(fontColorMenu);
        contextMenuStrip.Items.Add(new ToolStripSeparator());

        Color backgroundColor = settings.GetValue("sensorGadget.BackgroundColor", Color.FromArgb(0));
        SetBackgroundColor(backgroundColor);

        ToolStripMenuItem backgroundColorMenu = new ToolStripMenuItem("Background Color");
        ToolStripItem chooseBackgroundItem = new ToolStripMenuItem("Choose...");
        chooseBackgroundItem.Click += delegate
        {
            if (TrySelectColor(backgroundColor.A == 0 ? Color.White : backgroundColor, out Color selectedColor))
            {
                backgroundColor = selectedColor;
                SetBackgroundColor(backgroundColor);
                settings.SetValue("sensorGadget.BackgroundColor", backgroundColor);
            }
        };
        backgroundColorMenu.DropDownItems.Add(chooseBackgroundItem);

        ToolStripItem defaultBackgroundItem = new ToolStripMenuItem("Default");
        defaultBackgroundItem.Click += delegate
        {
            Color color = Color.FromArgb(0);
            backgroundColor = color;
            SetBackgroundColor(color);
            settings.Remove("sensorGadget.BackgroundColor");
        };
        backgroundColorMenu.DropDownItems.Add(defaultBackgroundItem);
        contextMenuStrip.Items.Add(backgroundColorMenu);

        // Fork addition: the empty ("back") part of a sensor's bar used to
        // always be tinted the same resolved color as the filled part
        // (gradient bucket or per-sensor override), which reads as the
        // whole bar glowing instead of just the value indicator. Off by
        // default: the bar track is a neutral shade of the gadget's own
        // background instead - see GetBarTrackColor. Still switchable
        // back to the old look for anyone who preferred it.
        ToolStripMenuItem gradientBarBackgroundItem = new ToolStripMenuItem("Gradient Bar Background");
        contextMenuStrip.Items.Add(gradientBarBackgroundItem);
        contextMenuStrip.Items.Add(new ToolStripSeparator());

        // Fork addition (Mini/Full gadget modes): Mini shows only
        // sensors explicitly marked for it (Add to Mini Gadget from the
        // main window's tree); Full shows everything. Also toggled by
        // double-clicking the gadget itself - see MouseDoubleClick below.
        ToolStripMenuItem miniModeItem = new ToolStripMenuItem("Mini Mode");
        contextMenuStrip.Items.Add(miniModeItem);
        contextMenuStrip.Items.Add(new ToolStripSeparator());

        ToolStripMenuItem lockItem = new ToolStripMenuItem("Lock Position and Size");
        contextMenuStrip.Items.Add(lockItem);
        ToolStripMenuItem alwaysOnTopItem = new ToolStripMenuItem("Always on Top");
        contextMenuStrip.Items.Add(alwaysOnTopItem);
        contextMenuStrip.Items.Add(_opacityMenu);
        Opacity = (byte)settings.GetValue("sensorGadget.Opacity", 255);

        for (int i = 0; i < 5; i++)
        {
            byte o = (byte)(51 * (i + 1));
            ToolStripMenuItem item = new ToolStripMenuItem((20 * (i + 1)).ToString() + " %") { Checked = Opacity == o, Tag = o };
            item.Click += delegate
            {
                Opacity = o;
                settings.SetValue("sensorGadget.Opacity", Opacity);
                SyncCheckedByTag(_opacityMenu, o);
            };
            _opacityMenu.DropDownItems.Add(item);
        }
        contextMenuStrip.Items.Add(new ToolStripSeparator());

        // Fork addition (Phase 5 - Theme & Profile export/import): see
        // ThemeProfileManager for the file format and the Theme/Profile
        // key split, and ApplyThemeFromSettings/ResortFromSettings for
        // how an import takes effect on the already-running gadget.
        ToolStripMenuItem themeProfileMenu = new ToolStripMenuItem("Theme / Profile");

        ToolStripItem exportThemeItem = new ToolStripMenuItem("Export Theme...");
        exportThemeItem.Click += delegate
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "GoGoGadget Theme (*.lhmtheme.json)|*.lhmtheme.json",
                FileName = "gadget.lhmtheme.json",
                InitialDirectory = AppPaths.UserSettingsDirectory
            };
            if (dialog.ShowDialog() == DialogResult.OK)
                ThemeProfileManager.ExportTheme(settings, dialog.FileName);
        };
        themeProfileMenu.DropDownItems.Add(exportThemeItem);

        ToolStripItem importThemeItem = new ToolStripMenuItem("Import Theme...");
        importThemeItem.Click += delegate
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "GoGoGadget Theme (*.lhmtheme.json)|*.lhmtheme.json|All files (*.*)|*.*",
                InitialDirectory = AppPaths.UserSettingsDirectory
            };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            if (ThemeProfileManager.TryImportTheme(settings, dialog.FileName, out string error))
                ApplyThemeFromSettings();
            else
                MessageBox.Show(error, "Import Theme", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        };
        themeProfileMenu.DropDownItems.Add(importThemeItem);
        themeProfileMenu.DropDownItems.Add(new ToolStripSeparator());

        ToolStripItem exportProfileItem = new ToolStripMenuItem("Export Profile...");
        exportProfileItem.Click += delegate
        {
            using SaveFileDialog dialog = new SaveFileDialog
            {
                Filter = "GoGoGadget Profile (*.lhmprofile.json)|*.lhmprofile.json",
                FileName = "gadget.lhmprofile.json",
                InitialDirectory = AppPaths.UserSettingsDirectory
            };
            if (dialog.ShowDialog() == DialogResult.OK)
                ThemeProfileManager.ExportProfile(settings, dialog.FileName);
        };
        themeProfileMenu.DropDownItems.Add(exportProfileItem);

        ToolStripItem importProfileItem = new ToolStripMenuItem("Import Profile...");
        importProfileItem.Click += delegate
        {
            using OpenFileDialog dialog = new OpenFileDialog
            {
                Filter = "GoGoGadget Profile (*.lhmprofile.json)|*.lhmprofile.json|All files (*.*)|*.*",
                InitialDirectory = AppPaths.UserSettingsDirectory
            };
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            if (ThemeProfileManager.TryImportProfile(settings, dialog.FileName, out string error))
            {
                ApplyThemeFromSettings();
                ResortFromSettings();
                MessageBox.Show(
                    "Profile imported. Colors, gradients, and order took effect immediately - " +
                    "restart the app if the profile also added or removed sensors from the widget.",
                    "Import Profile",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            else
            {
                MessageBox.Show(error, "Import Profile", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        };
        themeProfileMenu.DropDownItems.Add(importProfileItem);
        contextMenuStrip.Items.Add(themeProfileMenu);

        ContextMenuStrip = contextMenuStrip;

        // Fork addition (Phase 1 - reordering): per-sensor "Move Up" /
        // "Move Down" items, inserted at the top of the existing gadget
        // menu only when the right-click landed on a sensor row.
        _moveUpItem.Click += delegate
        {
            if (_contextMenuSensor != null)
                MoveSensor(_contextMenuSensor, -1);
        };
        _moveDownItem.Click += delegate
        {
            if (_contextMenuSensor != null)
                MoveSensor(_contextMenuSensor, 1);
        };

        // Fork addition (hardware-group reordering): "Move Group Up" /
        // "Move Group Down" on a right-click over a hardware-name header
        // row, reordering that whole block (CPU, GPU, RAM, ...) relative
        // to the others - never mixing individual sensors across blocks.
        _moveGroupUpItem.Click += delegate
        {
            if (_contextMenuHardware != null)
                MoveHardwareGroup(_contextMenuHardware, -1);
        };
        _moveGroupDownItem.Click += delegate
        {
            if (_contextMenuHardware != null)
                MoveHardwareGroup(_contextMenuHardware, 1);
        };

        // Fork addition (Phase 2 - per-sensor bar color): "Auto" clears
        // the override and falls back to the gradient (if armed) or the
        // gadget's normal font color - see ResolveSensorColor.
        _barColorItem.DropDownItems.Add(_barColorChooseItem);
        _barColorItem.DropDownItems.Add(_barColorAutoItem);
        _barColorChooseItem.Click += delegate
        {
            if (_contextMenuSensor == null)
                return;

            if (!SensorGadgetItemSettings.TryGetBarColor(_settings, _contextMenuSensor, out Color initial))
                initial = _fontColor;

            if (TrySelectColor(initial, out Color selected))
            {
                SensorGadgetItemSettings.SetBarColor(_settings, _contextMenuSensor, selected);
                Redraw();
            }
        };
        _barColorAutoItem.Click += delegate
        {
            if (_contextMenuSensor == null)
                return;

            SensorGadgetItemSettings.ClearBarColor(_settings, _contextMenuSensor);
            Redraw();
        };

        // Fork addition (Text Color): "Auto" clears the override and
        // falls back to whatever the bar/gradient resolves to - see
        // ResolveSensorTextColor.
        _textColorItem.DropDownItems.Add(_textColorChooseItem);
        _textColorItem.DropDownItems.Add(_textColorAutoItem);
        _textColorChooseItem.Click += delegate
        {
            if (_contextMenuSensor == null)
                return;

            if (!SensorGadgetItemSettings.TryGetTextColor(_settings, _contextMenuSensor, out Color initial))
                initial = ResolveSensorColor(_contextMenuSensor);

            if (TrySelectColor(initial, out Color selected))
            {
                SensorGadgetItemSettings.SetTextColor(_settings, _contextMenuSensor, selected);
                Redraw();
            }
        };
        _textColorAutoItem.Click += delegate
        {
            if (_contextMenuSensor == null)
                return;

            SensorGadgetItemSettings.ClearTextColor(_settings, _contextMenuSensor);
            Redraw();
        };

        // Fork addition (Name Color): "Choose.../Auto" each open a scope
        // submenu (This Sensor/This Group/All Sensors) - the color
        // picker (or "clear") only runs once, then applies to every
        // sensor the chosen scope resolves to via ApplyNameColorScope/
        // ClearNameColorScope. "Auto" falls back to the gadget's normal
        // font color - see ResolveSensorNameColor.
        _nameColorItem.DropDownItems.Add(_nameColorChooseItem);
        _nameColorItem.DropDownItems.Add(_nameColorAutoItem);
        _nameColorChooseItem.DropDownItems.Add(_nameColorChooseThisItem);
        _nameColorChooseItem.DropDownItems.Add(_nameColorChooseGroupItem);
        _nameColorChooseItem.DropDownItems.Add(_nameColorChooseAllItem);
        _nameColorAutoItem.DropDownItems.Add(_nameColorAutoThisItem);
        _nameColorAutoItem.DropDownItems.Add(_nameColorAutoGroupItem);
        _nameColorAutoItem.DropDownItems.Add(_nameColorAutoAllItem);

        _nameColorChooseThisItem.Click += delegate { ChooseNameColorForScope(NameColorScope.ThisSensor); };
        _nameColorChooseGroupItem.Click += delegate { ChooseNameColorForScope(NameColorScope.Group); };
        _nameColorChooseAllItem.Click += delegate { ChooseNameColorForScope(NameColorScope.All); };
        _nameColorAutoThisItem.Click += delegate { ClearNameColorForScope(NameColorScope.ThisSensor); };
        _nameColorAutoGroupItem.Click += delegate { ClearNameColorForScope(NameColorScope.Group); };
        _nameColorAutoAllItem.Click += delegate { ClearNameColorForScope(NameColorScope.All); };

        // Fork addition (Phase 3 - gradient coloring): a bar/number
        // sensor whose value crosses a user-defined threshold shifts
        // from green through yellow to red by the user-defined max - see
        // BarColorResolver. The gradient wins over a static "Bar Color"
        // override when armed, since an alarm color should never be
        // silently masked by a decorative choice.
        _gradientItem.Click += delegate
        {
            if (_contextMenuSensor == null)
                return;

            bool hasThresholds = SensorGadgetItemSettings.TryGetGradientThresholds(_settings, _contextMenuSensor, out float currentWarn, out float currentCrit);
            GradientThresholdDialog.Result result = GradientThresholdDialog.Show(
                _contextMenuSensor.Name,
                hasThresholds ? currentWarn : null,
                hasThresholds ? currentCrit : null,
                out float warnAt,
                out float critAt);

            switch (result)
            {
                case GradientThresholdDialog.Result.Save:
                    SensorGadgetItemSettings.SetGradientThresholds(_settings, _contextMenuSensor, warnAt, critAt);
                    Redraw();
                    break;
                case GradientThresholdDialog.Result.Clear:
                    SensorGadgetItemSettings.ClearGradientThresholds(_settings, _contextMenuSensor);
                    Redraw();
                    break;
            }
        };

        // Fork addition (Value Display): "%"/"Bar" per bar-capable sensor -
        // see IsBarCapableSensorType and UpdateSensorMenuItems, which only
        // inserts this item for those sensor types. "Both" (number layered
        // on the bar) exists in the model/paint code below and is still
        // wired up here, but deliberately left out of the menu - doesn't
        // look good yet (2026-09-28) and GetValueDisplayMode clamps any
        // already-persisted "Both" back to "%" so nobody's stuck seeing
        // it. Re-add the DropDownItems.Add line below to bring it back
        // once the look is fixed.
        _valueDisplayItem.DropDownItems.Add(_valueDisplayBarItem);
        _valueDisplayItem.DropDownItems.Add(_valueDisplayPercentItem);
        _valueDisplayBarItem.Click += delegate
        {
            if (_contextMenuSensor == null)
                return;

            SensorGadgetItemSettings.SetValueDisplayMode(_settings, _contextMenuSensor, ValueDisplayMode.Bar);
            Redraw();
        };
        _valueDisplayPercentItem.Click += delegate
        {
            if (_contextMenuSensor == null)
                return;

            SensorGadgetItemSettings.SetValueDisplayMode(_settings, _contextMenuSensor, ValueDisplayMode.Percent);
            Redraw();
        };
        _valueDisplayBothItem.Click += delegate
        {
            if (_contextMenuSensor == null)
                return;

            SensorGadgetItemSettings.SetValueDisplayMode(_settings, _contextMenuSensor, ValueDisplayMode.Both);
            Redraw();
        };

        // Fork addition: gadget-only display name. Deliberately does not
        // touch ISensor.Name or the main window's own "Rename" - only
        // what this one row shows.
        _displayNameItem.Click += delegate
        {
            if (_contextMenuSensor == null)
                return;

            SensorGadgetItemSettings.TryGetDisplayName(_settings, _contextMenuSensor, out string currentName);
            DisplayNameDialog.Result result = DisplayNameDialog.Show(_contextMenuSensor.Name, currentName, out string newName);

            switch (result)
            {
                case DisplayNameDialog.Result.Save:
                    if (string.IsNullOrEmpty(newName) || newName == _contextMenuSensor.Name)
                        SensorGadgetItemSettings.ClearDisplayName(_settings, _contextMenuSensor);
                    else
                        SensorGadgetItemSettings.SetDisplayName(_settings, _contextMenuSensor, newName);
                    Redraw();
                    break;
                case DisplayNameDialog.Result.Clear:
                    SensorGadgetItemSettings.ClearDisplayName(_settings, _contextMenuSensor);
                    Redraw();
                    break;
            }
        };

        // Fork addition: same effect as unchecking "Show in Gadget" from
        // the main window's tree. Remove(sensor, true) only clears the
        // "gadget"/"gadget.order" keys, same as it always has - a bar
        // color, gradient, or widget name set on this sensor survives and
        // comes back if it's ever re-added, rather than being lost.
        _removeFromWidgetItem.Click += delegate
        {
            if (_contextMenuSensor == null)
                return;

            Remove(_contextMenuSensor);
            _contextMenuSensor = null;
        };

        ContextMenuOpening += delegate (object sender, Point location)
        {
            _contextMenuSensor = FindSensorAt(location);
            _contextMenuHardware = _contextMenuSensor == null ? FindHardwareHeaderAt(location) : null;
            UpdateSensorMenuItems();
        };

        _gradientBarBackground = new UserOption("sensorGadget.gradientBarBackground", false, gradientBarBackgroundItem, settings);
        _gradientBarBackground.Changed += delegate
        {
            Redraw();
        };

        // Fork change: _miniMode must exist before HardwareNamesEnabled
        // is ever read/written (its resolver depends on _miniMode.Value)
        // - constructed here, above the Hardware Names wiring below,
        // instead of its original position further down.
        _miniMode = new UserOption("sensorGadget.miniMode", false, miniModeItem, settings);
        _miniMode.Changed += delegate
        {
            DebugLog.Write("MiniFull", $"miniMode.Changed -> {_miniMode.Value}");
            // Fork addition: HardwareNamesEnabled resolves to a different
            // setting per mode - resync the shared checkbox to whichever
            // one is now active, since toggling mode alone (without ever
            // clicking "Hardware Names" itself) can change its effective
            // value.
            _hardwareNamesItem.Checked = HardwareNamesEnabled;
            Resize();
            Redraw();
        };

        _hardwareNamesItem.Checked = HardwareNamesEnabled;
        _hardwareNamesItem.Click += delegate
        {
            HardwareNamesEnabled = !HardwareNamesEnabled;
            Resize();
        };

        // Fork addition: double-click anywhere on the gadget flips Mini/
        // Full, same as the "Mini Mode" menu checkbox above - just faster
        // to reach without a right-click. MouseDoubleClick already exists
        // on GadgetWindow/Gadget (WM_NCLBUTTONDBLCLK), unused until now.
        // Fork fix history: this used to also toggle the main window's
        // hide/show via a second MouseDoubleClick handler - see
        // CHANGELOG.md. Logging every fire here so a regression of that
        // double-firing shows up immediately instead of needing another
        // screenshot round trip.
        MouseDoubleClick += delegate
        {
            DebugLog.Write("MiniFull", $"MouseDoubleClick -> flipping miniMode from {_miniMode.Value} to {!_miniMode.Value}");
            _miniMode.Value = !_miniMode.Value;
        };

        UserOption alwaysOnTop = new UserOption("sensorGadget.AlwaysOnTop", false, alwaysOnTopItem, settings);
        alwaysOnTop.Changed += delegate
        {
            AlwaysOnTop = alwaysOnTop.Value;
        };
        UserOption lockPositionAndSize = new UserOption("sensorGadget.LockPositionAndSize", false, lockItem, settings);
        lockPositionAndSize.Changed += delegate
        {
            LockPositionAndSize = lockPositionAndSize.Value;
        };

        hideShowItem.Click += delegate
        {
            SendHideShowCommand();
        };

        HitTest += delegate (object sender, HitTestEventArgs e)
        {
            if (lockPositionAndSize.Value)
                return;

            if (e.Location.X < LeftBorder)
            {
                e.HitResult = HitResult.Left;
                return;
            }
            if (e.Location.X > Size.Width - 1 - RightBorder)
            {
                e.HitResult = HitResult.Right;
                return;
            }

            // Fork addition (vertical drag-to-resize): mirrors the
            // Left/Right edges above - dragging the top or bottom edge
            // resizes vertically, changing line spacing rather than
            // font/icon/bar size (see the UserResized handler above and
            // ComputeContentHeight).
            if (e.Location.Y < TopBorder)
            {
                e.HitResult = HitResult.Top;
                return;
            }
            if (e.Location.Y > Size.Height - 1 - BottomBorder)
            {
                e.HitResult = HitResult.Bottom;
            }
        };

        SizeChanged += delegate
        {
            settings.SetValue("sensorGadget.Width", Size.Width);
            Redraw();
        };

        VisibleChanged += delegate
        {
            Rectangle bounds = new Rectangle(Location, Size);
            Screen screen = Screen.FromRectangle(bounds);
            Rectangle intersection = Rectangle.Intersect(screen.WorkingArea, bounds);
            if (intersection.Width < Math.Min(16, bounds.Width) || intersection.Height < Math.Min(16, bounds.Height))
            {
                Location = new Point(screen.WorkingArea.Width / 2 - bounds.Width / 2, screen.WorkingArea.Height / 2 - bounds.Height / 2);
            }
        };
    }

    public override void Dispose()
    {

        _largeFont.Dispose();
        _largeFont = null;

        _smallFont.Dispose();
        _smallFont = null;

        _textBrush.Dispose();
        _textBrush = null;

        _stringFormat.Dispose();
        _stringFormat = null;

        _trimStringFormat.Dispose();
        _trimStringFormat = null;

        _alignRightStringFormat.Dispose();
        _alignRightStringFormat = null;

        _back.Dispose();
        _back = null;

        _barFore.Dispose();
        _barFore = null;

        _barBack.Dispose();
        _barBack = null;

        if (_backTinted != null)
        {
            _backTinted.Dispose();
            _backTinted = null;
        }

        // Fork addition (Phase 2/3): dispose every cached per-color tint/
        // brush, not just a single gadget-wide instance.
        foreach (Image image in _barBackTintCache.Values)
            image.Dispose();
        _barBackTintCache.Clear();

        foreach (Image image in _barForeTintCache.Values)
            image.Dispose();
        _barForeTintCache.Clear();

        foreach (SolidBrush brush in _colorBrushCache.Values)
            brush.Dispose();
        _colorBrushCache.Clear();

        _background.Dispose();
        _background = null;

        if (_image != null)
        {
            _image.Dispose();
            _image = null;
        }

        if (_fore != null)
        {
            _fore.Dispose();
            _fore = null;
        }

        base.Dispose();
    }

    private void HardwareRemoved(IHardware hardware)
    {
        hardware.SensorAdded -= SensorAdded;
        hardware.SensorRemoved -= SensorRemoved;

        foreach (ISensor sensor in hardware.Sensors)
            SensorRemoved(sensor);

        foreach (IHardware subHardware in hardware.SubHardware)
            HardwareRemoved(subHardware);
    }

    private void HardwareAdded(IHardware hardware)
    {
        foreach (ISensor sensor in hardware.Sensors)
            SensorAdded(sensor);

        hardware.SensorAdded += SensorAdded;
        hardware.SensorRemoved += SensorRemoved;

        foreach (IHardware subHardware in hardware.SubHardware)
            HardwareAdded(subHardware);
    }

    private void SensorAdded(ISensor sensor)
    {
        if (_settings.GetValue(new Identifier(sensor.Identifier, "gadget").ToString(), false))
            Add(sensor);
    }

    private void SensorRemoved(ISensor sensor)
    {
        if (Contains(sensor))
            Remove(sensor, false);
    }

    public bool Contains(ISensor sensor)
    {
        return _sensors.Values.Any(list => list.Contains(sensor));
    }

    // Fork addition (Mini/Full gadget modes) ------------------------------

    public bool ContainsMini(ISensor sensor)
    {
        return Contains(sensor) && SensorGadgetItemSettings.IsMini(_settings, sensor);
    }

    // Mini membership always implies Full membership - add to Full first
    // if this sensor isn't shown there yet, same as checking "Add to Full
    // Gadget" would.
    public void AddToMini(ISensor sensor)
    {
        if (!Contains(sensor))
            Add(sensor);

        SensorGadgetItemSettings.SetMini(_settings, sensor, true);
        Resize();
        Redraw();
    }

    // Leaves the sensor in Full - only "Remove from Widget"/unchecking
    // "Add to Full Gadget" removes it entirely (and clears Mini with it,
    // see Remove(sensor, true) below).
    public void RemoveFromMini(ISensor sensor)
    {
        SensorGadgetItemSettings.SetMini(_settings, sensor, false);
        Resize();
        Redraw();
    }

    // ----------------------------------------------------------------------

    public void Add(ISensor sensor)
    {
        if (Contains(sensor))
            return;


        // get the right hardware
        IHardware hardware = sensor.Hardware;
        while (hardware.Parent != null)
            hardware = hardware.Parent;

        // get the sensor list associated with the hardware
        if (!_sensors.TryGetValue(hardware, out IList<ISensor> list))
        {
            list = new List<ISensor>();
            _sensors.Add(hardware, list);
            InsertHardwareGroup(hardware);
        }

        if (_settings.Contains(OrderKey(sensor)))
        {
            // Fork addition: this sensor has an explicit position from an
            // earlier Move Up/Down in this hardware block - respect it
            // instead of recomputing from SensorType/Index.
            int order = _settings.GetValue(OrderKey(sensor), int.MaxValue);
            int pos = 0;
            while (pos < list.Count && _settings.GetValue(OrderKey(list[pos]), int.MaxValue) <= order)
                pos++;

            list.Insert(pos, sensor);
        }
        else
        {
            // insert the sensor at the right position
            int i = 0;
            while (i < list.Count && (list[i].SensorType < sensor.SensorType || (list[i].SensorType == sensor.SensorType && list[i].Index < sensor.Index)))
                i++;

            list.Insert(i, sensor);

            // Fork addition: if the rest of this block already has an
            // explicit order (it's been manually reordered before), anchor
            // this newly-shown sensor at the end of that order instead of
            // leaving it to jump to a SensorType-derived position that no
            // longer matches what's on screen.
            if (list.Count > 1 && list.Any(s => !ReferenceEquals(s, sensor) && _settings.Contains(OrderKey(s))))
            {
                list.Remove(sensor);
                list.Add(sensor);

                // Use one past the highest surviving order value, not
                // list.Count - 1: if a sensor was previously removed from
                // the middle of this block, the remaining order values are
                // no longer a contiguous 0..count-1 range, and Count - 1
                // could collide with a value a surviving sensor still has.
                int maxOrder = list.Where(s => !ReferenceEquals(s, sensor))
                                    .Select(s => _settings.GetValue(OrderKey(s), -1))
                                    .DefaultIfEmpty(-1)
                                    .Max();
                _settings.SetValue(OrderKey(sensor), maxOrder + 1);
            }
        }

        _settings.SetValue(new Identifier(sensor.Identifier, "gadget").ToString(), true);
        Resize();
    }

    public void Remove(ISensor sensor)
    {
        Remove(sensor, true);
    }

    private void Remove(ISensor sensor, bool deleteConfig)
    {
        if (deleteConfig)
        {
            _settings.Remove(new Identifier(sensor.Identifier, "gadget").ToString());
            _settings.Remove(OrderKey(sensor));

            // Mini membership can never outlive Full membership - removing
            // from Full (this call) must clear Mini with it, or the
            // invariant AddToMini/ContainsMini rely on breaks the moment
            // this sensor is re-added to Full without going through
            // AddToMini again.
            SensorGadgetItemSettings.SetMini(_settings, sensor, false);
        }

        foreach (KeyValuePair<IHardware, IList<ISensor>> keyValue in _sensors)
        {
            if (keyValue.Value.Contains(sensor))
            {
                keyValue.Value.Remove(sensor);
                if (keyValue.Value.Count == 0)
                {
                    _sensors.Remove(keyValue.Key);
                    _hardwareOrder.Remove(keyValue.Key);
                    break;
                }
            }
        }
        Resize();
    }

    public event EventHandler HideShowCommand;

    public void SendHideShowCommand()
    {
        HideShowCommand?.Invoke(this, null);
    }

    // Fork addition (Phase 1 - reordering) -----------------------------

    private static string OrderKey(ISensor sensor)
    {
        return new Identifier(sensor.Identifier, "gadget.order").ToString();
    }

    // Moves a sensor one place up (direction -1) or down (direction +1)
    // within its own hardware block only - sensors never move between
    // blocks. Persists an explicit order for every sensor in that block
    // so the arrangement survives a restart and so a sensor added to the
    // block later (Add) knows to anchor onto the end of it.
    private void MoveSensor(ISensor sensor, int direction)
    {
        IList<ISensor> list = _sensors.Values.FirstOrDefault(l => l.Contains(sensor));
        if (list == null)
            return;

        int index = list.IndexOf(sensor);
        int newIndex = index + direction;
        if (newIndex < 0 || newIndex >= list.Count)
            return;

        (list[newIndex], list[index]) = (list[index], list[newIndex]);

        for (int i = 0; i < list.Count; i++)
            _settings.SetValue(OrderKey(list[i]), i);

        Redraw();
    }

    private ISensor FindSensorAt(Point location)
    {
        foreach ((ISensor sensor, int top, int bottom) in _rowBounds)
        {
            if (location.Y >= top && location.Y < bottom)
                return sensor;
        }

        return null;
    }

    private void UpdateSensorMenuItems()
    {
        ContextMenuStrip.Items.Remove(_moveUpItem);
        ContextMenuStrip.Items.Remove(_moveDownItem);
        ContextMenuStrip.Items.Remove(_barColorItem);
        ContextMenuStrip.Items.Remove(_textColorItem);
        ContextMenuStrip.Items.Remove(_nameColorItem);
        ContextMenuStrip.Items.Remove(_gradientItem);
        ContextMenuStrip.Items.Remove(_valueDisplayItem);
        ContextMenuStrip.Items.Remove(_displayNameItem);
        ContextMenuStrip.Items.Remove(_removeFromWidgetItem);
        ContextMenuStrip.Items.Remove(_moveGroupUpItem);
        ContextMenuStrip.Items.Remove(_moveGroupDownItem);
        ContextMenuStrip.Items.Remove(_sensorMenuSeparator);
        ContextMenuStrip.Items.Remove(_sensorMenuMoveSeparator);
        ContextMenuStrip.Items.Remove(_sensorMenuRemoveSeparator);
        ContextMenuStrip.Items.Remove(_contextMenuDeviceItem);
        ContextMenuStrip.Items.Remove(_contextMenuSensorItem);
        ContextMenuStrip.Items.Remove(_contextMenuInfoSeparator);

        if (_contextMenuHardware != null)
        {
            int hwIndex = _hardwareOrder.IndexOf(_contextMenuHardware);
            _moveGroupUpItem.Text = "Move \"" + _contextMenuHardware.Name + "\" Up";
            _moveGroupDownItem.Text = "Move \"" + _contextMenuHardware.Name + "\" Down";
            _moveGroupUpItem.Enabled = hwIndex > 0;
            _moveGroupDownItem.Enabled = hwIndex >= 0 && hwIndex < _hardwareOrder.Count - 1;

            ContextMenuStrip.Items.Insert(0, _sensorMenuSeparator);
            ContextMenuStrip.Items.Insert(0, _moveGroupDownItem);
            ContextMenuStrip.Items.Insert(0, _moveGroupUpItem);
            ContextMenuStrip.Items.Insert(0, _contextMenuInfoSeparator);
            _contextMenuDeviceItem.Text = "Device: " + _contextMenuHardware.Name;
            ContextMenuStrip.Items.Insert(0, _contextMenuDeviceItem);
            return;
        }

        if (_contextMenuSensor == null)
            return;

        IList<ISensor> list = _sensors.Values.FirstOrDefault(l => l.Contains(_contextMenuSensor));
        if (list == null)
            return;

        int index = list.IndexOf(_contextMenuSensor);
        _moveUpItem.Text = "Move \"" + _contextMenuSensor.Name + "\" Up";
        _moveDownItem.Text = "Move \"" + _contextMenuSensor.Name + "\" Down";
        _moveUpItem.Enabled = index > 0;
        _moveDownItem.Enabled = index < list.Count - 1;

        bool hasOverride = SensorGadgetItemSettings.TryGetBarColor(_settings, _contextMenuSensor, out _);
        _barColorAutoItem.Enabled = hasOverride;

        bool hasTextOverride = SensorGadgetItemSettings.TryGetTextColor(_settings, _contextMenuSensor, out _);
        _textColorAutoItem.Enabled = hasTextOverride;

        bool hasNameOverride = SensorGadgetItemSettings.TryGetNameColor(_settings, _contextMenuSensor, out _);
        _nameColorAutoThisItem.Enabled = hasNameOverride;

        bool hasThresholds = SensorGadgetItemSettings.TryGetGradientThresholds(_settings, _contextMenuSensor, out _, out _);
        _gradientItem.Text = hasThresholds ? "Gradient Colors..." : "Gradient Colors... (off)";

        bool isBarCapable = IsBarCapableSensorType(_contextMenuSensor.SensorType);
        if (isBarCapable)
        {
            ValueDisplayMode mode = SensorGadgetItemSettings.GetValueDisplayMode(_settings, _contextMenuSensor);
            _valueDisplayBarItem.Checked = mode == ValueDisplayMode.Bar;
            _valueDisplayPercentItem.Checked = mode == ValueDisplayMode.Percent;
            _valueDisplayBothItem.Checked = mode == ValueDisplayMode.Both;
        }

        ContextMenuStrip.Items.Insert(0, _sensorMenuSeparator);
        ContextMenuStrip.Items.Insert(0, _removeFromWidgetItem);
        ContextMenuStrip.Items.Insert(0, _sensorMenuRemoveSeparator);
        ContextMenuStrip.Items.Insert(0, _displayNameItem);
        if (isBarCapable)
            ContextMenuStrip.Items.Insert(0, _valueDisplayItem);
        ContextMenuStrip.Items.Insert(0, _gradientItem);
        ContextMenuStrip.Items.Insert(0, _nameColorItem);
        ContextMenuStrip.Items.Insert(0, _textColorItem);
        ContextMenuStrip.Items.Insert(0, _barColorItem);
        ContextMenuStrip.Items.Insert(0, _sensorMenuMoveSeparator);
        ContextMenuStrip.Items.Insert(0, _moveDownItem);
        ContextMenuStrip.Items.Insert(0, _moveUpItem);

        ContextMenuStrip.Items.Insert(0, _contextMenuInfoSeparator);
        _contextMenuSensorItem.Text = "Sensor: " + _contextMenuSensor.Name;
        ContextMenuStrip.Items.Insert(0, _contextMenuSensorItem);
        _contextMenuDeviceItem.Text = "Device: " + _contextMenuSensor.Hardware.Name;
        ContextMenuStrip.Items.Insert(0, _contextMenuDeviceItem);
    }

    // Fork addition (Value Display): the sensor types that render as a
    // 0-100 bar by default (DrawProgress's 0.01f * sensor.Value.Value
    // assumes this range) - single source of truth shared by the paint
    // loop and the context menu instead of duplicating the type list.
    private static bool IsBarCapableSensorType(SensorType type)
    {
        return type == SensorType.Load || type == SensorType.Control || type == SensorType.Level || type == SensorType.Humidity;
    }

    // Fork addition (hardware-group reordering) --------------------------

    private static string HardwareOrderKey(IHardware hardware)
    {
        return new Identifier(hardware.Identifier, "gadget.hardwareOrder").ToString();
    }

    // Places a newly-shown hardware group into _hardwareOrder: respects
    // an explicit position from an earlier Move Group Up/Down if one is
    // persisted, otherwise falls back to the original HardwareType/
    // Identifier ordering - mirrors Add()'s per-sensor logic above.
    private void InsertHardwareGroup(IHardware hardware)
    {
        if (_settings.Contains(HardwareOrderKey(hardware)))
        {
            int order = _settings.GetValue(HardwareOrderKey(hardware), int.MaxValue);
            int pos = 0;
            while (pos < _hardwareOrder.Count && _settings.GetValue(HardwareOrderKey(_hardwareOrder[pos]), int.MaxValue) <= order)
                pos++;

            _hardwareOrder.Insert(pos, hardware);
        }
        else
        {
            int i = 0;
            while (i < _hardwareOrder.Count && DefaultHardwareComparer.Compare(_hardwareOrder[i], hardware) < 0)
                i++;

            _hardwareOrder.Insert(i, hardware);

            if (_hardwareOrder.Count > 1 && _hardwareOrder.Any(h => !ReferenceEquals(h, hardware) && _settings.Contains(HardwareOrderKey(h))))
            {
                _hardwareOrder.Remove(hardware);
                _hardwareOrder.Add(hardware);

                int maxOrder = _hardwareOrder.Where(h => !ReferenceEquals(h, hardware))
                                              .Select(h => _settings.GetValue(HardwareOrderKey(h), -1))
                                              .DefaultIfEmpty(-1)
                                              .Max();
                _settings.SetValue(HardwareOrderKey(hardware), maxOrder + 1);
            }
        }
    }

    // Moves a whole hardware group (all its sensors together) one place
    // up or down relative to the other groups - never interleaving
    // individual sensors from different hardware.
    private void MoveHardwareGroup(IHardware hardware, int direction)
    {
        int index = _hardwareOrder.IndexOf(hardware);
        if (index < 0)
            return;

        int newIndex = index + direction;
        if (newIndex < 0 || newIndex >= _hardwareOrder.Count)
            return;

        (_hardwareOrder[newIndex], _hardwareOrder[index]) = (_hardwareOrder[index], _hardwareOrder[newIndex]);

        for (int i = 0; i < _hardwareOrder.Count; i++)
            _settings.SetValue(HardwareOrderKey(_hardwareOrder[i]), i);

        Redraw();
    }

    private IHardware FindHardwareHeaderAt(Point location)
    {
        foreach ((IHardware hardware, int top, int bottom) in _hardwareHeaderBounds)
        {
            if (location.Y >= top && location.Y < bottom)
                return hardware;
        }

        return null;
    }

    // Fork addition (Phase 2/3 - per-sensor color/gradient) --------------

    // Single call site for "what color should this sensor's bar/number
    // be" - a gradient (if armed and the sensor has a value) wins over a
    // static per-sensor override, which wins over the gadget's normal
    // font color. See BarColorResolver and CLAUDE.md's note that alarm
    // coloring should never be silently masked by a decorative choice.
    private Color ResolveSensorColor(ISensor sensor)
    {
        if (sensor.Value.HasValue &&
            SensorGadgetItemSettings.TryGetGradientThresholds(_settings, sensor, out float warnAt, out float critAt))
        {
            return BarColorResolver.ResolveGradientColor(sensor.Value.Value, warnAt, critAt);
        }

        if (SensorGadgetItemSettings.TryGetBarColor(_settings, sensor, out Color overrideColor))
            return overrideColor;

        return _fontColor;
    }

    // Fork addition (Text Color): an explicit per-sensor override for
    // the drawn NUMBER only, independent of ResolveSensorColor (which
    // still governs the bar/gradient). Falls back to ResolveSensorColor
    // when unset, matching the previous behavior of a number always
    // following the bar's color.
    private Color ResolveSensorTextColor(ISensor sensor)
    {
        if (SensorGadgetItemSettings.TryGetTextColor(_settings, sensor, out Color overrideColor))
            return overrideColor;

        return ResolveSensorColor(sensor);
    }

    // Fork addition (Name Color): an explicit per-sensor override for
    // the drawn DISPLAY NAME only, independent of ResolveSensorColor/
    // ResolveSensorTextColor (which govern the bar/gradient and the
    // drawn number). Falls back to the gadget's normal font color when
    // unset, matching the previous behavior of the name always using
    // that color.
    private Color ResolveSensorNameColor(ISensor sensor)
    {
        if (SensorGadgetItemSettings.TryGetNameColor(_settings, sensor, out Color overrideColor))
            return overrideColor;

        return _fontColor;
    }

    // Fork addition (Name Color scope): how far "Choose.../Auto" reaches
    // from the right-clicked sensor - see the Name Color menu-item
    // comment. Not persisted itself; each scope just expands to the set
    // of sensors whose own gadget.nameColor key gets written/cleared in
    // one bulk operation.
    private enum NameColorScope
    {
        ThisSensor,
        Group,
        All
    }

    private IEnumerable<ISensor> SensorsInNameColorScope(NameColorScope scope)
    {
        switch (scope)
        {
            case NameColorScope.ThisSensor:
                if (_contextMenuSensor != null)
                    yield return _contextMenuSensor;
                break;
            case NameColorScope.Group:
                if (_contextMenuSensor != null)
                {
                    IList<ISensor> list = _sensors.Values.FirstOrDefault(l => l.Contains(_contextMenuSensor));
                    if (list != null)
                        foreach (ISensor sensor in list)
                            yield return sensor;
                }

                break;
            case NameColorScope.All:
                foreach (ISensor sensor in _sensors.Values.SelectMany(l => l))
                    yield return sensor;
                break;
        }
    }

    private void ChooseNameColorForScope(NameColorScope scope)
    {
        if (_contextMenuSensor == null)
            return;

        if (!SensorGadgetItemSettings.TryGetNameColor(_settings, _contextMenuSensor, out Color initial))
            initial = _fontColor;

        if (!TrySelectColor(initial, out Color selected))
            return;

        foreach (ISensor sensor in SensorsInNameColorScope(scope))
            SensorGadgetItemSettings.SetNameColor(_settings, sensor, selected);

        Redraw();
    }

    private void ClearNameColorForScope(NameColorScope scope)
    {
        if (_contextMenuSensor == null)
            return;

        foreach (ISensor sensor in SensorsInNameColorScope(scope))
            SensorGadgetItemSettings.ClearNameColor(_settings, sensor);

        Redraw();
    }

    // Fork addition: the name this one gadget row shows, if the user set
    // one via "Display Name..." - falls back to the sensor's own real
    // name, which is never touched by this override.
    private static string ResolveSensorDisplayName(PersistentSettings settings, ISensor sensor)
    {
        if (SensorGadgetItemSettings.TryGetDisplayName(settings, sensor, out string displayName))
            return displayName;

        return sensor.Name;
    }

    private Image GetTintedBarBack(Color color)
    {
        if (_customBarBack)
            return _barBack;

        if (!_barBackTintCache.TryGetValue(color, out Image image))
        {
            image = CreateBarTint(_barBack, color);
            _barBackTintCache[color] = image;
        }

        return image;
    }

    private Image GetTintedBarFore(Color color)
    {
        if (_customBarFore)
            return _barFore;

        if (!_barForeTintCache.TryGetValue(color, out Image image))
        {
            image = CreateBarTint(_barFore, color);
            _barForeTintCache[color] = image;
        }

        return image;
    }

    private SolidBrush GetColorBrush(Color color)
    {
        if (!_colorBrushCache.TryGetValue(color, out SolidBrush brush))
        {
            brush = new SolidBrush(color);
            _colorBrushCache[color] = brush;
        }

        return brush;
    }

    // The bar track's "shade of the widget background" - a fixed lighten/
    // darken step off the gadget's own Background Color (or off black, if
    // that's left on its transparent default), not the per-sensor
    // resolved color. Only used when _gradientBarBackground is off, which
    // it is by default - see DrawProgress. One color for the whole
    // gadget, so this adds at most a single extra entry to the bar-back
    // tint cache regardless of how many distinct sensor colors exist.
    private Color GetBarTrackColor()
    {
        Color bg = _backgroundColor.A > 0 ? _backgroundColor : Color.Black;
        float luminance = (0.299f * bg.R + 0.587f * bg.G + 0.114f * bg.B) / 255f;
        int shift = luminance > 0.5f ? -40 : 40;
        return Color.FromArgb(ClampByte(bg.R + shift), ClampByte(bg.G + shift), ClampByte(bg.B + shift));
    }

    private static int ClampByte(int value)
    {
        return value < 0 ? 0 : value > 255 ? 255 : value;
    }

    // --------------------------------------------------------------------

    private Font CreateFont(float size, FontStyle style)
    {
        try
        {
            return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style);
        }
        catch (ArgumentException)
        {
            // if the style is not supported, fall back to the original one
            return new Font(SystemFonts.MessageBoxFont.FontFamily, size,
                            SystemFonts.MessageBoxFont.Style);
        }
    }

    // Fork addition (Theme/Profile export): re-checks whichever
    // DropDownItem's Tag equals currentValue and un-checks the rest -
    // Font Size/Opacity/Scale tag each preset with its own value at
    // construction (see the constructor) specifically so this can find
    // the right one again after an import changes the setting out from
    // under whatever was checked before.
    private static void SyncCheckedByTag(ToolStripMenuItem menu, object currentValue)
    {
        foreach (ToolStripItem dropDownItem in menu.DropDownItems)
        {
            if (dropDownItem is ToolStripMenuItem item)
                item.Checked = Equals(item.Tag, currentValue);
        }
    }

    // Fork addition (Theme/Profile export): re-reads every gadget-wide
    // visual setting from _settings and re-applies it to the already-
    // running gadget - the same values the constructor reads once at
    // startup, made callable again for "Import Theme"/"Import Profile"
    // (a Profile embeds a Theme, so both import paths call this).
    private void ApplyThemeFromSettings()
    {
        DebugLog.Write("ThemeProfile", "ApplyThemeFromSettings starting");
        _scaleMultiplier = _settings.GetValue("sensorGadget.ScaleMultiplier", 1.0f);
        SetFontSize(_settings.GetValue("sensorGadget.FontSize", 7.5f));
        SyncCheckedByTag(_fontSizeMenu, _fontSize);
        SyncCheckedByTag(_scaleMenu, _scaleMultiplier);

        SetFontColor(_settings.GetValue("sensorGadget.FontColor", Color.White));
        SetBackgroundColor(_settings.GetValue("sensorGadget.BackgroundColor", Color.FromArgb(0)));

        Opacity = (byte)_settings.GetValue("sensorGadget.Opacity", 255);
        SyncCheckedByTag(_opacityMenu, Opacity);

        // HardwareNamesEnabled reads straight from _settings every call
        // (no cached value to push into, unlike a UserOption) - a Theme
        // import already updated the underlying key via _settings.SetValue
        // before this runs, so only the checkbox display needs resyncing.
        _hardwareNamesItem.Checked = HardwareNamesEnabled;
        _gradientBarBackground.Value = _settings.GetValue("sensorGadget.gradientBarBackground", false);

        _rowIconMode = GetRowIconMode(_settings);
        SyncCheckedByTag(_rowIconsMenu, _rowIconMode);

        Resize();
        Redraw();
    }

    // Fork addition (Theme/Profile export): re-sorts the sensors already
    // in each hardware block, and the hardware blocks themselves, by
    // whatever gadget.order/gadget.hardwareOrder values "Import Profile"
    // just wrote - mirrors Add()/InsertHardwareGroup's own order lookup,
    // but as a bulk re-sort of everything already on screen instead of
    // one newly-added item. Per-sensor color/gradient/display-name/mini
    // settings need no equivalent step: SensorGadgetItemSettings already
    // reads those live from _settings on every paint.
    private void ResortFromSettings()
    {
        foreach (IList<ISensor> list in _sensors.Values)
        {
            List<ISensor> sorted = list.OrderBy(s => _settings.GetValue(OrderKey(s), int.MaxValue)).ToList();
            list.Clear();
            foreach (ISensor sensor in sorted)
                list.Add(sensor);
        }

        List<IHardware> sortedHardware = _hardwareOrder.OrderBy(h => _settings.GetValue(HardwareOrderKey(h), int.MaxValue)).ToList();
        _hardwareOrder.Clear();
        _hardwareOrder.AddRange(sortedHardware);

        DebugLog.Write("ThemeProfile", $"ResortFromSettings done: {_hardwareOrder.Count} hardware groups, {_sensors.Values.Sum(l => l.Count)} sensors");
    }

    private void SetFontSize(float size)
    {
        _fontSize = size;

        // Fork fix: the font itself used to be created at the raw,
        // unscaled _fontSize while every other dimension below already
        // multiplied by _scale/_scaleMultiplier - so picking a bigger
        // gadget Scale grew the icons/margins/bar/window but left the
        // actual text pinned at its original size. Scale the font the
        // same way as everything else so "Scale" actually enlarges what
        // the user is trying to read.
        double scaledFontSize = _fontSize * _scale * _scaleMultiplier;
        _scaledFontSize = scaledFontSize;
        _largeFont = CreateFont((float)scaledFontSize, FontStyle.Bold);
        _smallFont = CreateFont((float)scaledFontSize, FontStyle.Regular);

        _iconSize = (int)Math.Round(1.5 * scaledFontSize);
        // Fork fix: upstream's 1.66x/1.33x ratios read as cramped now
        // that PerMonitorV2 DPI awareness renders text at its true size
        // instead of being silently corrected by DPI virtualization's
        // bitmap stretch (see the gadget-resize-clipping fix) - bumped
        // for more breathing room between rows.
        _hardwareLineHeight = (int)Math.Round(1.9 * scaledFontSize);
        _sensorLineHeight = (int)Math.Round(1.55 * scaledFontSize);
        _leftMargin = LeftBorder + (int)Math.Round(0.3 * scaledFontSize);
        _rightMargin = RightBorder + (int)Math.Round(0.3 * scaledFontSize);
        _topMargin = TopBorder;
        _bottomMargin = BottomBorder + (int)Math.Round(0.3 * scaledFontSize);
        _progressWidth = (int)Math.Round(5.3 * scaledFontSize);

        Resize(ComputeDefaultWidth(scaledFontSize));
    }

    // Fork fix: upstream's 17.3x-of-font-size ratio only ever needed to
    // fit a bar OR a number per row. "Both" display mode (the fork's
    // default for Load/Control/Level/Humidity sensors - see
    // ValueDisplayMode) layers a number on top of the bar in the same
    // spot, so the required width is whichever of the two is wider, not
    // upstream's original guess. Measure the actual worst case ("100 %")
    // instead of guessing, so the default/Scale-driven width is never
    // narrower than what either needs.
    //
    // Fork fix: the name side used to be a flat "6x font size" guess,
    // which is nowhere near long real hardware/sensor names ("Intel
    // Core i7-7700K", "CPU Package") - names kept truncating even after
    // the right-side fix above, because the window itself was simply
    // too narrow. Measure the actual longest name currently shown
    // instead of guessing - _hardwareOrder is empty the one time this
    // runs before any hardware is known (during the constructor, before
    // Add() has been called for the first sensor), where the flat
    // fallback is only ever a momentary placeholder: Add()/Resize()
    // recompute this for real the moment real hardware/sensor names are
    // known, unless the user has since pinned a width by dragging - see
    // _widthManuallySet.
    private int ComputeDefaultWidth(double scaledFontSize)
    {
        using (Bitmap b = new Bitmap(1, 1))
        using (Graphics g = Graphics.FromImage(b))
        {
            float numberWidth = g.MeasureString("100 %", _smallFont, int.MaxValue, StringFormat.GenericTypographic).Width;
            int rightSideWidth = Math.Max((int)Math.Ceiling(numberWidth), _progressWidth) + _rightMargin;

            float longestNameWidth = 6 * (float)scaledFontSize;
            foreach (IHardware hardware in _hardwareOrder)
            {
                IReadOnlyList<ISensor> list = GetDisplayedSensors(hardware);
                if (list.Count == 0)
                    continue;

                if (HardwareNamesEnabled)
                    longestNameWidth = Math.Max(longestNameWidth, g.MeasureString(hardware.Name, _largeFont, int.MaxValue, StringFormat.GenericTypographic).Width);

                foreach (ISensor sensor in list)
                    longestNameWidth = Math.Max(longestNameWidth, g.MeasureString(ResolveSensorDisplayName(_settings, sensor), _smallFont, int.MaxValue, StringFormat.GenericTypographic).Width);
            }

            // Fork addition (Row Icons): only the first sensor row of each
            // group gets a device icon, same as the hardware header row
            // already does - the single _iconSize allowance below already
            // covers that, no extra needed.
            int nameSideWidth = _leftMargin + _iconSize + (int)Math.Ceiling(longestNameWidth) + 4;
            return Math.Max((int)Math.Round(17.3 * scaledFontSize), rightSideWidth + nameSideWidth);
        }
    }

    private void SetFontColor(Color color)
    {
        _fontColor = color;
        _textBrush?.Dispose();
        _textBrush = new SolidBrush(color);
    }

    private void SetBackgroundColor(Color color)
    {
        _backgroundColor = color;
        _backTinted?.Dispose();
        _backTinted = null;

        // Transparent means "Default" and keeps the embedded/custom image as-is.
        if (_backgroundColor.A > 0)
        {
            _backTinted = CreateBackgroundTint(_back, _backgroundColor);
        }

        _backgroundDirty = true;
        Redraw();
    }

    private static Image CreateBackgroundTint(Image source, Color targetColor)
    {
        Bitmap sourceBitmap = new Bitmap(source);
        Rectangle rect = new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height);
        Bitmap workingSource = sourceBitmap.PixelFormat == PixelFormat.Format32bppArgb
            ? sourceBitmap
            : sourceBitmap.Clone(rect, PixelFormat.Format32bppArgb);
        Bitmap result = new Bitmap(workingSource.Width, workingSource.Height, PixelFormat.Format32bppPArgb);

        float targetHue = targetColor.GetHue() / 360f;
        float targetSaturation = targetColor.GetSaturation();
        float targetValue = GetColorValue(targetColor);
        bool isGrayscaleTarget = targetSaturation <= 0.001f;

        BitmapData srcData = null;
        BitmapData dstData = null;
        try
        {
            srcData = workingSource.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            dstData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);

            int srcLength = srcData.Stride * workingSource.Height;
            int dstLength = dstData.Stride * result.Height;
            byte[] srcBuffer = new byte[srcLength];
            byte[] dstBuffer = new byte[dstLength];
            Marshal.Copy(srcData.Scan0, srcBuffer, 0, srcLength);

            for (int y = 0; y < workingSource.Height; y++)
            {
                int srcRow = y * srcData.Stride;
                int dstRow = y * dstData.Stride;
                for (int x = 0; x < workingSource.Width; x++)
                {
                    int srcIndex = srcRow + (x * 4);
                    int dstIndex = dstRow + (x * 4);
                    byte b = srcBuffer[srcIndex + 0];
                    byte g = srcBuffer[srcIndex + 1];
                    byte r = srcBuffer[srcIndex + 2];
                    byte a = srcBuffer[srcIndex + 3];

                    if (a == 0)
                    {
                        // For PArgb targets, keep fully transparent pixels black.
                        dstBuffer[dstIndex + 0] = 0;
                        dstBuffer[dstIndex + 1] = 0;
                        dstBuffer[dstIndex + 2] = 0;
                        dstBuffer[dstIndex + 3] = 0;
                        continue;
                    }

                    Color c = Color.FromArgb(a, r, g, b);
                    float sourceSaturation = c.GetSaturation();
                    float sourceValue = c.GetBrightness();
                    float saturation = isGrayscaleTarget
                        ? 0f
                        : Math.Min(1f, Math.Max(sourceSaturation * 0.25f, targetSaturation * 0.85f));
                    float value = Math.Min(1f, (sourceValue * 0.55f) + (targetValue * 0.45f));
                    Color tinted = ColorFromHsv(targetHue, saturation, value, a);

                    // Destination format is 32bppPArgb, so RGB must be premultiplied by A.
                    dstBuffer[dstIndex + 0] = (byte)((tinted.B * tinted.A + 127) / 255);
                    dstBuffer[dstIndex + 1] = (byte)((tinted.G * tinted.A + 127) / 255);
                    dstBuffer[dstIndex + 2] = (byte)((tinted.R * tinted.A + 127) / 255);
                    dstBuffer[dstIndex + 3] = tinted.A;
                }
            }

            Marshal.Copy(dstBuffer, 0, dstData.Scan0, dstLength);
        }
        finally
        {
            if (srcData != null)
                workingSource.UnlockBits(srcData);
            if (dstData != null)
                result.UnlockBits(dstData);

            if (!ReferenceEquals(workingSource, sourceBitmap))
                workingSource.Dispose();

            sourceBitmap.Dispose();
        }

        return result;
    }

    // Fork addition (Phase 2/3): ported from GetPixel/SetPixel to LockBits
    // buffer access, matching CreateBackgroundTint. This used to run once
    // at startup for a single gadget-wide color; now it runs on demand
    // per resolved sensor color (still cached per color - see
    // GetTintedBarBack/GetTintedBarFore - so it's still "once per color",
    // just no longer only once total), so the cheaper access pattern
    // matters more than it used to.
    private static Image CreateBarTint(Image source, Color targetColor)
    {
        Bitmap sourceBitmap = new Bitmap(source);
        Rectangle rect = new Rectangle(0, 0, sourceBitmap.Width, sourceBitmap.Height);
        Bitmap workingSource = sourceBitmap.PixelFormat == PixelFormat.Format32bppArgb
            ? sourceBitmap
            : sourceBitmap.Clone(rect, PixelFormat.Format32bppArgb);
        Bitmap result = new Bitmap(workingSource.Width, workingSource.Height, PixelFormat.Format32bppPArgb);

        BitmapData srcData = null;
        BitmapData dstData = null;
        try
        {
            srcData = workingSource.LockBits(rect, ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            dstData = result.LockBits(rect, ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);

            int srcLength = srcData.Stride * workingSource.Height;
            int dstLength = dstData.Stride * result.Height;
            byte[] srcBuffer = new byte[srcLength];
            byte[] dstBuffer = new byte[dstLength];
            Marshal.Copy(srcData.Scan0, srcBuffer, 0, srcLength);

            for (int y = 0; y < workingSource.Height; y++)
            {
                int srcRow = y * srcData.Stride;
                int dstRow = y * dstData.Stride;
                for (int x = 0; x < workingSource.Width; x++)
                {
                    int srcIndex = srcRow + (x * 4);
                    int dstIndex = dstRow + (x * 4);

                    // Bars should follow the resolved color directly;
                    // preserve source alpha for antialiasing/edge
                    // transparency. Destination is 32bppPArgb, so RGB
                    // must be premultiplied by A.
                    byte a = srcBuffer[srcIndex + 3];
                    dstBuffer[dstIndex + 0] = (byte)((targetColor.B * a + 127) / 255);
                    dstBuffer[dstIndex + 1] = (byte)((targetColor.G * a + 127) / 255);
                    dstBuffer[dstIndex + 2] = (byte)((targetColor.R * a + 127) / 255);
                    dstBuffer[dstIndex + 3] = a;
                }
            }

            Marshal.Copy(dstBuffer, 0, dstData.Scan0, dstLength);
        }
        finally
        {
            if (srcData != null)
                workingSource.UnlockBits(srcData);
            if (dstData != null)
                result.UnlockBits(dstData);

            if (!ReferenceEquals(workingSource, sourceBitmap))
                workingSource.Dispose();

            sourceBitmap.Dispose();
        }

        return result;
    }

    private static Color ColorFromHsv(float hue, float saturation, float value, int alpha)
    {
        if (saturation <= 0)
        {
            int v = (int)Math.Round(value * 255);
            return Color.FromArgb(alpha, v, v, v);
        }

        float h = (hue % 1.0f + 1.0f) % 1.0f * 6.0f;
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

        return Color.FromArgb(alpha,
                              (int)Math.Round(r * 255),
                              (int)Math.Round(g * 255),
                              (int)Math.Round(b * 255));
    }

    private static bool TrySelectColor(Color initialColor, out Color selectedColor)
    {
        using Form form = new Form
        {
            Text = "Select Color",
            FormBorderStyle = FormBorderStyle.Sizable,
            StartPosition = FormStartPosition.CenterScreen,
            MinimizeBox = false,
            MaximizeBox = false,
            ClientSize = new Size(460, 340),
            MinimumSize = new Size(420, 320),
            AutoScaleMode = AutoScaleMode.Font
        };
        form.Icon = EmbeddedResources.GetIcon("icon.ico");

        Panel svBox = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Cross
        };
        Panel hueBox = new Panel
        {
            BorderStyle = BorderStyle.FixedSingle,
            Cursor = Cursors.Cross
        };
        SetDoubleBuffered(svBox);
        SetDoubleBuffered(hueBox);
        SetDoubleBuffered(form);

        float currentHue = initialColor.GetHue() / 360f;
        float currentSaturation = initialColor.GetSaturation();
        float currentValue = GetColorValue(initialColor);
        Color current = ColorFromHsv(currentHue, currentSaturation, currentValue, 255);

        int svSelectorX = 0;
        int svSelectorY = 0;
        int hueSelectorY = 0;

        Panel preview = new Panel
        {
            Size = new Size(34, 34),
            BorderStyle = BorderStyle.FixedSingle,
            BackColor = current
        };

        Label hexLabel = new Label
        {
            AutoSize = true,
            Location = new Point(0, 0),
            Text = "Hex:"
        };
        TextBox hexTextBox = new TextBox
        {
            Width = 110,
            Text = $"#{current.R:X2}{current.G:X2}{current.B:X2}"
        };

        Button okButton = new Button
        {
            Text = "OK",
            DialogResult = DialogResult.OK,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(75, 0),
            Padding = new Padding(6, 2, 6, 2)
        };
        Button cancelButton = new Button
        {
            Text = "Cancel",
            DialogResult = DialogResult.Cancel,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(75, 0),
            Padding = new Padding(6, 2, 6, 2)
        };
        bool updatingHexText = false;

        void LayoutControls()
        {
            const int padding = 12;
            const int hueBarWidth = 24;
            const int hueGap = 8;
            int bottomRowHeight = Math.Max(
                Math.Max(preview.Height, Math.Max(okButton.Height, cancelButton.Height)),
                Math.Max(hexTextBox.PreferredSize.Height, hexLabel.Height)) + 8;
            int rowTop = form.ClientSize.Height - padding - bottomRowHeight;

            svBox.Location = new Point(padding, padding);
            svBox.Size = new Size(
                Math.Max(200, form.ClientSize.Width - (2 * padding) - hueBarWidth - hueGap),
                Math.Max(120, form.ClientSize.Height - (3 * padding) - bottomRowHeight));
            hueBox.Location = new Point(svBox.Right + hueGap, padding);
            hueBox.Size = new Size(hueBarWidth, svBox.Height);

            preview.Location = new Point(padding, rowTop + (bottomRowHeight - preview.Height) / 2);
            hexLabel.Location = new Point(preview.Right + 10, rowTop + (bottomRowHeight - hexLabel.Height) / 2);
            hexTextBox.Location = new Point(hexLabel.Right + 6, rowTop + (bottomRowHeight - hexTextBox.Height) / 2);

            cancelButton.Location = new Point(form.ClientSize.Width - padding - cancelButton.Width, rowTop + (bottomRowHeight - cancelButton.Height) / 2);
            okButton.Location = new Point(cancelButton.Left - 8 - okButton.Width, rowTop + (bottomRowHeight - okButton.Height) / 2);
        }

        void SyncSelectorsFromCurrent()
        {
            svSelectorX = Math.Min(Math.Max(0, (int)Math.Round(currentSaturation * Math.Max(1, svBox.ClientSize.Width - 1))), Math.Max(0, svBox.ClientSize.Width - 1));
            svSelectorY = Math.Min(Math.Max(0, (int)Math.Round((1f - currentValue) * Math.Max(1, svBox.ClientSize.Height - 1))), Math.Max(0, svBox.ClientSize.Height - 1));
            hueSelectorY = Math.Min(Math.Max(0, (int)Math.Round(currentHue * Math.Max(1, hueBox.ClientSize.Height - 1))), Math.Max(0, hueBox.ClientSize.Height - 1));
        }

        void UpdateCurrentColor()
        {
            current = ColorFromHsv(currentHue, currentSaturation, currentValue, 255);
            preview.BackColor = current;
            updatingHexText = true;
            hexTextBox.Text = $"#{current.R:X2}{current.G:X2}{current.B:X2}";
            updatingHexText = false;
        }

        void UpdateSvFromPoint(Point p)
        {
            svSelectorX = Math.Min(Math.Max(0, p.X), Math.Max(0, svBox.ClientSize.Width - 1));
            svSelectorY = Math.Min(Math.Max(0, p.Y), Math.Max(0, svBox.ClientSize.Height - 1));
            currentSaturation = svSelectorX / (float)Math.Max(1, svBox.ClientSize.Width - 1);
            currentValue = 1f - svSelectorY / (float)Math.Max(1, svBox.ClientSize.Height - 1);
            UpdateCurrentColor();
            svBox.Invalidate();
        }

        void UpdateHueFromPoint(Point p)
        {
            int newHueSelectorY = Math.Min(Math.Max(0, p.Y), Math.Max(0, hueBox.ClientSize.Height - 1));
            if (newHueSelectorY == hueSelectorY)
                return;

            hueSelectorY = newHueSelectorY;
            currentHue = hueSelectorY / (float)Math.Max(1, hueBox.ClientSize.Height - 1);
            UpdateCurrentColor();
            svBox.Invalidate();
            hueBox.Invalidate();
        }

        bool draggingSv = false;
        bool draggingHue = false;
        svBox.MouseDown += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            draggingSv = true;
            UpdateSvFromPoint(e.Location);
        };
        svBox.MouseMove += delegate(object sender, MouseEventArgs e)
        {
            if (draggingSv)
                UpdateSvFromPoint(e.Location);
        };
        svBox.MouseUp += delegate { draggingSv = false; };

        hueBox.MouseDown += delegate(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left)
                return;
            draggingHue = true;
            UpdateHueFromPoint(e.Location);
        };
        hueBox.MouseMove += delegate(object sender, MouseEventArgs e)
        {
            if (draggingHue)
                UpdateHueFromPoint(e.Location);
        };
        hueBox.MouseUp += delegate { draggingHue = false; };

        svBox.Paint += delegate(object sender, PaintEventArgs e)
        {
            Rectangle rect = svBox.ClientRectangle;
            if (rect.Width <= 1 || rect.Height <= 1)
                return;

            rect.Width -= 1;
            rect.Height -= 1;
            e.Graphics.SmoothingMode = SmoothingMode.None;

            using (SolidBrush hueBrush = new SolidBrush(ColorFromHsv(currentHue, 1f, 1f, 255)))
                e.Graphics.FillRectangle(hueBrush, rect);

            using (LinearGradientBrush satBrush = new LinearGradientBrush(rect, Color.White, Color.Transparent, LinearGradientMode.Horizontal))
            {
                ColorBlend satBlend = new ColorBlend
                {
                    Positions = new[] { 0f, 1f },
                    Colors = new[] { Color.FromArgb(255, 255, 255, 255), Color.FromArgb(0, 255, 255, 255) }
                };
                satBrush.InterpolationColors = satBlend;
                e.Graphics.FillRectangle(satBrush, rect);
            }

            using (LinearGradientBrush valueBrush = new LinearGradientBrush(rect, Color.Transparent, Color.Black, LinearGradientMode.Vertical))
            {
                ColorBlend valueBlend = new ColorBlend
                {
                    Positions = new[] { 0f, 1f },
                    Colors = new[] { Color.FromArgb(0, 0, 0, 0), Color.FromArgb(255, 0, 0, 0) }
                };
                valueBrush.InterpolationColors = valueBlend;
                e.Graphics.FillRectangle(valueBrush, rect);
            }

            Rectangle marker = new Rectangle(svSelectorX - 4, svSelectorY - 4, 8, 8);
            using Pen outer = new Pen(Color.Black, 2f);
            using Pen inner = new Pen(Color.White, 1f);
            e.Graphics.DrawEllipse(outer, marker);
            e.Graphics.DrawEllipse(inner, marker);
        };
        hueBox.Paint += delegate(object sender, PaintEventArgs e)
        {
            Rectangle rect = hueBox.ClientRectangle;
            if (rect.Width <= 1 || rect.Height <= 1)
                return;

            rect.Width -= 1;
            rect.Height -= 1;

            using (LinearGradientBrush hueBrush = new LinearGradientBrush(rect, Color.Red, Color.Red, LinearGradientMode.Vertical))
            {
                hueBrush.InterpolationColors = new ColorBlend
                {
                    Positions = new[] { 0f, 1f / 6f, 2f / 6f, 3f / 6f, 4f / 6f, 5f / 6f, 1f },
                    Colors = new[] { Color.Red, Color.Yellow, Color.Lime, Color.Cyan, Color.Blue, Color.Magenta, Color.Red }
                };
                e.Graphics.FillRectangle(hueBrush, rect);
            }

            Rectangle marker = new Rectangle(0, hueSelectorY - 2, hueBox.ClientSize.Width - 1, 4);
            using Pen outer = new Pen(Color.Black, 2f);
            using Pen inner = new Pen(Color.White, 1f);
            e.Graphics.DrawRectangle(outer, marker);
            e.Graphics.DrawRectangle(inner, marker);
        };
        hexTextBox.TextChanged += delegate
        {
            if (updatingHexText)
                return;

            if (!TryParseHexColor(hexTextBox.Text, out Color parsedColor))
                return;

            float parsedHue = parsedColor.GetHue() / 360f;
            float parsedSaturation = parsedColor.GetSaturation();
            float parsedValue = GetColorValue(parsedColor);

            currentHue = parsedHue;
            currentSaturation = parsedSaturation;
            currentValue = parsedValue;
            SyncSelectorsFromCurrent();
            UpdateCurrentColor();
            svBox.Invalidate();
            hueBox.Invalidate();
        };

        form.Controls.Add(svBox);
        form.Controls.Add(hueBox);
        form.Controls.Add(preview);
        form.Controls.Add(hexLabel);
        form.Controls.Add(hexTextBox);
        form.Controls.Add(okButton);
        form.Controls.Add(cancelButton);
        form.AcceptButton = okButton;
        form.CancelButton = cancelButton;
        Theme.Current.Apply(form);
        form.Resize += delegate
        {
            LayoutControls();
            SyncSelectorsFromCurrent();
            UpdateCurrentColor();
            svBox.Invalidate();
            hueBox.Invalidate();
        };
        LayoutControls();
        DebugLog.Write("DialogLayout", $"Color picker: okButton.Height={okButton.Height} cancelButton.Height={cancelButton.Height} ClientSize={form.ClientSize}");
        SyncSelectorsFromCurrent();
        UpdateCurrentColor();

        if (form.ShowDialog() == DialogResult.OK)
        {
            selectedColor = current;
            return true;
        }

        selectedColor = initialColor;
        return false;
    }

    private static void SetDoubleBuffered(Control control)
    {
        typeof(Control).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.SetValue(control, true, null);
    }

    private static float GetColorValue(Color color)
    {
        return Math.Max(color.R, Math.Max(color.G, color.B)) / 255f;
    }

    private static bool TryParseHexColor(string input, out Color color)
    {
        color = Color.Empty;
        if (string.IsNullOrWhiteSpace(input))
            return false;

        string hex = input.Trim();
        if (hex.StartsWith("#"))
            hex = hex.Substring(1);

        if (hex.Length != 6)
            return false;

        if (!int.TryParse(hex.Substring(0, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int r))
            return false;
        if (!int.TryParse(hex.Substring(2, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int g))
            return false;
        if (!int.TryParse(hex.Substring(4, 2), System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out int b))
            return false;

        color = Color.FromArgb(r, g, b);
        return true;
    }

    // Fork fix: keep auto-fitting the width to whatever's actually shown
    // (a sensor added/removed, Mini/Full toggled, a longer display name
    // set) unless the user has pinned a width by dragging - see
    // _widthManuallySet. Every call site that used to just preserve the
    // current width now goes through this instead of Resize(Size.Width)
    // directly.
    private void Resize()
    {
        Resize(_widthManuallySet ? Size.Width : ComputeDefaultWidth(_scaledFontSize));
    }

    // Fork addition (Mini/Full gadget modes): Mini only counts/shows
    // sensors explicitly added to it - see AddToMini. A hardware group
    // with nothing to show in the current mode is skipped entirely
    // (no header either), rather than showing an empty block.
    private IReadOnlyList<ISensor> GetDisplayedSensors(IHardware hardware)
    {
        if (!_sensors.TryGetValue(hardware, out IList<ISensor> list))
            return Array.Empty<ISensor>();

        if (!_miniMode.Value)
            return (IReadOnlyList<ISensor>)list;

        return list.Where(s => SensorGadgetItemSettings.IsMini(_settings, s)).ToList();
    }

    private bool HasAnyDisplayedSensor()
    {
        return _hardwareOrder.Any(hw => GetDisplayedSensors(hw).Count > 0);
    }

    // Fork addition (vertical drag-to-resize): the total content height
    // as a function of extraPerStep, plus how many row-advance "steps"
    // (each hardware header and each sensor row) that content has -
    // shared by Resize (which applies whatever extra the user last
    // dragged to, or 0 until they ever have) and the UserResized handler
    // above (which calls this with extraPerStep=0 to get the "natural"
    // height, then solves for the extra needed to hit the height the
    // user just dragged to). OnPaint's row loop mirrors this exact
    // structure - keep the two in sync if either changes.
    private int ComputeContentHeight(int extraPerStep, out int stepCount)
    {
        // Fork fix (Gemini review Finding 6): clamp here too, not just
        // where extraPerStep is first computed/persisted - a caller
        // passing in a stale _lineSpacingExtra from before a font-size
        // or Scale change (smaller _sensorLineHeight now) must still get
        // a safe value; 0 (the "natural height" query every caller also
        // uses) is always above this floor, so clamping unconditionally
        // is a no-op for that case.
        extraPerStep = Math.Max(-(_sensorLineHeight - 2), extraPerStep);

        int y = _topMargin;
        int steps = 0;

        foreach (IHardware hardware in _hardwareOrder)
        {
            IReadOnlyList<ISensor> list = GetDisplayedSensors(hardware);
            if (list.Count == 0)
                continue;

            // Fork addition (Group Spacing): a fixed gap between groups,
            // independent of extraPerStep - not a "step" itself (doesn't
            // count toward stepCount), since it's not something the
            // vertical drag-to-resize feature stretches per-row.
            if (y > _topMargin)
                y += _groupSpacingExtra;

            if (HardwareNamesEnabled)
            {
                if (y > _topMargin)
                {
                    y += _hardwareLineHeight - _sensorLineHeight + extraPerStep;
                    steps++;
                }
                y += _hardwareLineHeight + extraPerStep;
                steps++;
            }
            y += list.Count * (_sensorLineHeight + extraPerStep);
            steps += list.Count;
        }

        // No rows yet - the placeholder message has its own fixed
        // height, unaffected by line spacing (nothing to space out).
        if (!HasAnyDisplayedSensor())
            y += 4 * _sensorLineHeight + _hardwareLineHeight;

        y += _bottomMargin;
        stepCount = steps;
        return y;
    }

    // Fork addition (vertical drag-to-resize, Gemini review Finding 2 -
    // see from_gemini_to_claude.md): wired to GadgetWindow.SnapHeight in
    // the constructor. Given a raw drag height, returns the nearest
    // height that corresponds to a whole-number extraPerStep, so
    // WM_WINDOWPOSCHANGING can snap wp.cy to it before ever delivering
    // it as Size/UserResized - the window edge then moves in discrete
    // row-step increments during the drag itself, instead of drifting
    // between an arbitrary cursor position and the quantized height
    // Resize() would later snap it to anyway (which was showing up as
    // clipping/blank gaps that got worse the longer the drag continued).
    private int SnapHeightToRowSpacing(int candidateHeight)
    {
        int naturalHeight = ComputeContentHeight(0, out int stepCount);
        if (stepCount <= 0)
            return candidateHeight;

        int extra = (int)Math.Round((candidateHeight - naturalHeight) / (double)stepCount, MidpointRounding.AwayFromZero);
        extra = Math.Max(-(_sensorLineHeight - 2), extra);
        return naturalHeight + stepCount * extra;
    }

    private void Resize(int width)
    {
        int extraPerStep = _lineSpacingManuallySet ? _lineSpacingExtra : 0;
        int y = ComputeContentHeight(extraPerStep, out _);
        DebugLog.Write("Resize", $"SensorGadget.Resize({width}): hardwareGroups={_hardwareOrder.Count} sensors={_sensors.Values.Sum(l => l.Count)} widthManuallySet={_widthManuallySet} lineSpacingManuallySet={_lineSpacingManuallySet} lineSpacingExtra={extraPerStep} computedHeight={y} currentSize={Size}");
        Size = new Size(width, y);
    }

    private void DrawImageWidthBorder(Graphics g, int width, int height, Image back, int t, int b, int l, int r)
    {
        GraphicsUnit u = GraphicsUnit.Pixel;

        g.DrawImage(back, new Rectangle(0, 0, l, t), new Rectangle(0, 0, l, t), u);
        g.DrawImage(back, new Rectangle(l, 0, width - l - r, t), new Rectangle(l, 0, back.Width - l - r, t), u);
        g.DrawImage(back, new Rectangle(width - r, 0, r, t), new Rectangle(back.Width - r, 0, r, t), u);

        g.DrawImage(back, new Rectangle(0, t, l, height - t - b), new Rectangle(0, t, l, back.Height - t - b), u);
        g.DrawImage(back, new Rectangle(l, t, width - l - r, height - t - b), new Rectangle(l, t, back.Width - l - r, back.Height - t - b), u);
        g.DrawImage(back, new Rectangle(width - r, t, r, height - t - b), new Rectangle(back.Width - r, t, r, back.Height - t - b), u);

        g.DrawImage(back, new Rectangle(0, height - b, l, b), new Rectangle(0, back.Height - b, l, b), u);
        g.DrawImage(back, new Rectangle(l, height - b, width - l - r, b), new Rectangle(l, back.Height - b, back.Width - l - r, b), u);
        g.DrawImage(back, new Rectangle(width - r, height - b, r, b), new Rectangle(back.Width - r, back.Height - b, r, b), u);
    }

    // Fork addition: like DrawImageWidthBorder, but for the background
    // layer specifically, replacing its large stretched middle-fill draw
    // with a freshly-computed LinearGradientBrush instead of stretching a
    // fixed ~117px source region to fill an arbitrary window height. The
    // skin's built-in "glass panel" shading (a subtle lighter-top/
    // darker-bottom two-tone gradient baked into gadget.png, which
    // CreateBackgroundTint's hue-only re-tint carries through unchanged)
    // looks fine stretched a little, but turns into an ugly hard-edged
    // band once the fork's vertical drag-to-resize lets a user make the
    // gadget far taller than the skin's native ~130px - something
    // upstream's skin was never designed to accommodate. A brush-drawn
    // gradient has no source "resolution" to run out of, so it stays
    // smooth at any height. Its two endpoint colors are sampled from the
    // actual current background image (so a custom Background Color tint
    // still comes through) rather than hardcoded. Only used for the
    // background (_backTinted ?? _back); the _fore overlay still uses
    // DrawImageWidthBorder unchanged since it isn't the source of the
    // banding.
    private void DrawBackgroundImage(Graphics g, int width, int height, Image back, int t, int b, int l, int r)
    {
        GraphicsUnit u = GraphicsUnit.Pixel;

        g.DrawImage(back, new Rectangle(0, 0, l, t), new Rectangle(0, 0, l, t), u);
        g.DrawImage(back, new Rectangle(l, 0, width - l - r, t), new Rectangle(l, 0, back.Width - l - r, t), u);
        g.DrawImage(back, new Rectangle(width - r, 0, r, t), new Rectangle(back.Width - r, 0, r, t), u);

        g.DrawImage(back, new Rectangle(0, t, l, height - t - b), new Rectangle(0, t, l, back.Height - t - b), u);

        Rectangle middleDest = new Rectangle(l, t, width - l - r, height - t - b);
        if (middleDest.Width > 0 && middleDest.Height > 0)
        {
            using (Bitmap sourceBitmap = new Bitmap(back))
            {
                int sampleX = Math.Min(sourceBitmap.Width - 1, sourceBitmap.Width / 2);
                int topY = Math.Min(sourceBitmap.Height - 1, t);
                int bottomY = Math.Max(0, sourceBitmap.Height - b - 1);
                Color topColor = sourceBitmap.GetPixel(sampleX, topY);
                Color bottomColor = sourceBitmap.GetPixel(sampleX, bottomY);

                using (LinearGradientBrush brush = new LinearGradientBrush(
                           new Rectangle(middleDest.X, middleDest.Y, middleDest.Width, Math.Max(1, middleDest.Height)),
                           topColor, bottomColor, LinearGradientMode.Vertical))
                {
                    g.FillRectangle(brush, middleDest);
                }
            }
        }

        g.DrawImage(back, new Rectangle(width - r, t, r, height - t - b), new Rectangle(back.Width - r, t, r, back.Height - t - b), u);

        g.DrawImage(back, new Rectangle(0, height - b, l, b), new Rectangle(0, back.Height - b, l, b), u);
        g.DrawImage(back, new Rectangle(l, height - b, width - l - r, b), new Rectangle(l, back.Height - b, back.Width - l - r, b), u);
        g.DrawImage(back, new Rectangle(width - r, height - b, r, b), new Rectangle(back.Width - r, back.Height - b, r, b), u);
    }

    private void DrawBackground(Graphics g)
    {
        int w = Size.Width;
        int h = Size.Height;

        bool needsRecreate = _backgroundDirty || w != _background.Width || h != _background.Height;
        DebugLog.Write("Resize", $"DrawBackground: w={w} h={h} _background.Width={_background.Width} _background.Height={_background.Height} _backgroundDirty={_backgroundDirty} needsRecreate={needsRecreate}");

        if (needsRecreate)
        {
            _background.Dispose();
            _background = new Bitmap(w, h, PixelFormat.Format32bppPArgb);

            using (Graphics graphics = Graphics.FromImage(_background))
            {
                DrawBackgroundImage(graphics, w, h, _backTinted ?? _back, TopBorder, BottomBorder, LeftBorder, RightBorder);

                if (_fore != null)
                    DrawImageWidthBorder(graphics, w, h, _fore, TopBorder, BottomBorder, LeftBorder, RightBorder);

                if (_image != null)
                {
                    int width = w - LeftBorder - RightBorder;
                    int height = h - TopBorder - BottomBorder;
                    float xRatio = width / (float)_image.Width;
                    float yRatio = height / (float)_image.Height;
                    float destWidth, destHeight;
                    float xOffset, yOffset;

                    if (xRatio < yRatio)
                    {
                        destWidth = width;
                        destHeight = _image.Height * xRatio;
                        xOffset = 0;
                        yOffset = 0.5f * (height - destHeight);
                    }
                    else
                    {
                        destWidth = _image.Width * yRatio;
                        destHeight = height;
                        xOffset = 0.5f * (width - destWidth);
                        yOffset = 0;
                    }

                    graphics.DrawImage(_image, new RectangleF(LeftBorder + xOffset, TopBorder + yOffset, destWidth, destHeight));
                }
            }

            _backgroundDirty = false;
        }

        g.DrawImageUnscaled(_background, 0, 0);
    }

    // Fork addition (Value Display): pulled out of OnPaint's row loop so
    // the "Percent" and "Both" branches can share it instead of
    // duplicating this switch - see IsBarCapableSensorType.
    private string FormatSensorValue(ISensor sensor)
    {
        string formatted;

        if (sensor.Value.HasValue)
        {
            string format = "";
            switch (sensor.SensorType)
            {
                case SensorType.Voltage:
                    format = "{0:F3} V";
                    break;
                case SensorType.Current:
                    format = "{0:F3} A";
                    break;
                case SensorType.Clock:
                    format = "{0:F0} MHz";
                    break;
                case SensorType.Frequency:
                    format = "{0:F0} Hz";
                    break;
                case SensorType.Temperature:
                    format = "{0:F1} °C";
                    break;
                case SensorType.Fan:
                    format = "{0:F0} RPM";
                    break;
                case SensorType.Flow:
                    format = "{0:F0} L/h";
                    break;
                case SensorType.Power:
                    format = "{0:F1} W";
                    break;
                case SensorType.Data:
                    format = "{0:F1} GB";
                    break;
                case SensorType.SmallData:
                    format = "{0:F0} MB";
                    break;
                case SensorType.Factor:
                    format = "{0:F3}";
                    break;
                case SensorType.TimeSpan:
                    format = "{0:g}";
                    break;
                case SensorType.Timing:
                    format = "{0:F3} ns";
                    break;
                case SensorType.Energy:
                    format = "{0:F0} mWh";
                    break;
                case SensorType.Noise:
                    format = "{0:F0} dBA";
                    break;
                case SensorType.Conductivity:
                    format = "{0:F1} µS/cm";
                    break;
                // Fork addition (Value Display "%"/"Both"): these types
                // used to only ever reach DrawProgress (a bar, no
                // number) - see IsBarCapableSensorType - so they never
                // needed a format string before now.
                case SensorType.Load:
                case SensorType.Control:
                case SensorType.Level:
                    format = "{0:F1} %";
                    break;
                case SensorType.Humidity:
                    format = "{0:F0} %";
                    break;
            }

            if (sensor.SensorType == SensorType.Temperature && _unitManager.TemperatureUnit == TemperatureUnit.Fahrenheit)
            {
                formatted = $"{UnitManager.CelsiusToFahrenheit(sensor.Value):F1} °F";
            }
            else if (sensor.SensorType == SensorType.Throughput)
            {
                string result;
                switch (sensor.Name)
                {
                    case "Connection Speed":
                        {
                            switch (sensor.Value)
                            {
                                case 100000000:
                                    result = "100Mbps";
                                    break;
                                case 1000000000:
                                    result = "1Gbps";
                                    break;
                                default:
                                    {
                                        if (sensor.Value < 1024)
                                            result = $"{sensor.Value:F0} bps";
                                        else if (sensor.Value < 1048576)
                                            result = $"{sensor.Value / 1024:F1} Kbps";
                                        else if (sensor.Value < 1073741824)
                                            result = $"{sensor.Value / 1048576:F1} Mbps";
                                        else
                                            result = $"{sensor.Value / 1073741824:F1} Gbps";
                                    }
                                    break;
                            }
                        }
                        break;
                    default:
                        {
                            if (sensor.Value < 1048576)
                                result = $"{sensor.Value / 1024:F1} KB/s";
                            else
                                result = $"{sensor.Value / 1048576:F1} MB/s";
                        }
                        break;
                }
                formatted = result;
            }
            else if (sensor.SensorType == SensorType.TimeSpan)
            {
                formatted = string.Format(format, TimeSpan.FromSeconds(sensor.Value.Value));
            }
            else
            {
                formatted = string.Format(format, sensor.Value);
            }
        }
        else
        {
            formatted = "-";
        }

        return formatted;
    }

    private void DrawProgress(Graphics g, float x, float y, float width, float height, float progress, Color color)
    {
        Image barBack = GetTintedBarBack(_gradientBarBackground.Value ? color : GetBarTrackColor());
        Image barFore = GetTintedBarFore(color);
        g.DrawImage(barBack,
                    new RectangleF(x + width * progress, y, width * (1 - progress), height),
                    new RectangleF(barBack.Width * progress, 0, (1 - progress) * barBack.Width, barBack.Height),
                    GraphicsUnit.Pixel);
        g.DrawImage(barFore,
                    new RectangleF(x, y, width * progress, height),
                    new RectangleF(0, 0, progress * barFore.Width, barFore.Height), GraphicsUnit.Pixel);
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        try
        {
            Graphics g = e.Graphics;
            int w = Size.Width;

            g.Clear(Color.Transparent);
            DrawBackground(g);

            _rowBounds.Clear();
            _hardwareHeaderBounds.Clear();

            int x;
            int y = _topMargin;

            // Fork addition (vertical drag-to-resize): mirrors
            // ComputeContentHeight's extraPerStep exactly, including its
            // live re-clamp against _sensorLineHeight (Gemini review
            // Finding 6) - keep the two in sync if either changes.
            int extraPerStep = _lineSpacingManuallySet ? Math.Max(-(_sensorLineHeight - 2), _lineSpacingExtra) : 0;

            if (!HasAnyDisplayedSensor())
            {
                x = LeftBorder + 1;
                g.DrawString(_miniMode.Value
                                 ? "No sensors in Mini yet. Double-click to switch to Full, or right-click " +
                                   "a sensor in the main window and choose \"Add to Mini Gadget\"."
                                 : "Right-click on a sensor in the main window and select " +
                                   "\"Show in Gadget\" to show the sensor here.",
                             _smallFont, _textBrush,
                             new Rectangle(x, y - 1, w - RightBorder - x, 0));
            }

            foreach (IHardware hardware in _hardwareOrder)
            {
                IReadOnlyList<ISensor> list = GetDisplayedSensors(hardware);
                if (list.Count == 0)
                    continue;

                bool isFirstSensorInGroup = true;

                // Fork addition (Group Spacing) - mirrors
                // ComputeContentHeight's identical addition exactly, see
                // its comment.
                if (y > _topMargin)
                    y += _groupSpacingExtra;

                if (HardwareNamesEnabled)
                {
                    if (y > _topMargin)
                        y += _hardwareLineHeight - _sensorLineHeight + extraPerStep;

                    int headerTop = y;
                    x = LeftBorder + 1;
                    g.DrawImage(HardwareTypeImage.Instance.GetImage(hardware.HardwareType), new Rectangle(x, y + 1, _iconSize, _iconSize));
                    x += _iconSize + 1;
                    // Fork fix (round-5 investigation): was _stringFormat,
                    // which has no Trimming set - a hardware name too long
                    // for the current width was hard-clipped mid-character
                    // with no "..." indicator, unlike the per-sensor display
                    // name below which already uses _trimStringFormat. User
                    // reported this as "text gets cropped" after a manual
                    // width drag left less room than the full hardware name
                    // needs.
                    g.DrawString(hardware.Name, _largeFont, _textBrush, new Rectangle(x, y - 1, w - RightBorder - x, 0), _trimStringFormat);
                    y += _hardwareLineHeight + extraPerStep;
                    _hardwareHeaderBounds.Add((hardware, headerTop, y));
                }

                foreach (ISensor sensor in list)
                {
                    _rowBounds.Add((sensor, y, y + _sensorLineHeight + extraPerStep));

                    int remainingWidth;

                    // Fork addition (Value Display): bar-capable sensor
                    // types used to always render as a bar with no
                    // number at all; "Value Display" on the sensor's
                    // context menu now lets each one show its number
                    // instead ("Percent"), or both together ("Both") -
                    // see IsBarCapableSensorType and
                    // SensorGadgetItemSettings.GetValueDisplayMode. Every
                    // other sensor type keeps rendering as a number only,
                    // same as always (forced to Percent below since
                    // GetValueDisplayMode is meaningless for them).
                    bool isBarCapable = IsBarCapableSensorType(sensor.SensorType) && sensor.Value.HasValue;
                    ValueDisplayMode displayMode = isBarCapable
                        ? SensorGadgetItemSettings.GetValueDisplayMode(_settings, sensor)
                        : ValueDisplayMode.Percent;

                    if (!isBarCapable || displayMode == ValueDisplayMode.Percent)
                    {
                        string formatted = FormatSensorValue(sensor);

                        // Fork addition (Phase 2/3 + Text Color): the value
                        // text follows ResolveSensorTextColor - an explicit
                        // per-sensor override if set, else the same
                        // resolved bar/gradient color as before - the
                        // numeric counterpart to DrawProgress below.
                        Brush valueBrush = sensor.Value.HasValue ? GetColorBrush(ResolveSensorTextColor(sensor)) : _textBrush;
                        g.DrawString(formatted, _smallFont, valueBrush, new RectangleF(-1, y - 1, w - _rightMargin + 3, 0), _alignRightStringFormat);

                        remainingWidth = w - (int)Math.Floor(g.MeasureString(formatted, _smallFont, w, StringFormat.GenericTypographic).Width) - _rightMargin;
                    }
                    else if (displayMode == ValueDisplayMode.Bar)
                    {
                        DrawProgress(g, w - _progressWidth - _rightMargin, y + 0.35f * _sensorLineHeight, _progressWidth, 0.6f * _sensorLineHeight, 0.01f * sensor.Value.Value, ResolveSensorColor(sensor));
                        remainingWidth = w - _progressWidth - _rightMargin;
                    }
                    else
                    {
                        // Both: the bar is drawn in its usual "Bar" mode
                        // spot, then the number is layered on top of it,
                        // right-aligned at the exact same column "Percent"
                        // mode uses - so every sensor's number lines up in
                        // the same column regardless of display mode, with
                        // the bar visible behind it. Text Color exists
                        // specifically so the number stays legible over
                        // the bar underneath it.
                        string formatted = FormatSensorValue(sensor);
                        int barX = w - _progressWidth - _rightMargin;

                        DrawProgress(g, barX, y + 0.35f * _sensorLineHeight, _progressWidth, 0.6f * _sensorLineHeight, 0.01f * sensor.Value.Value, ResolveSensorColor(sensor));
                        g.DrawString(formatted, _smallFont, GetColorBrush(ResolveSensorTextColor(sensor)), new RectangleF(-1, y - 1, w - _rightMargin + 3, 0), _alignRightStringFormat);

                        remainingWidth = barX;
                    }

                    int nameX = _leftMargin;

                    // Fork addition (Row Icons): the device icon marks
                    // only the first row of each hardware group - not
                    // every row - so it reads as a group marker rather
                    // than repeated clutter. "First" is positional
                    // (isFirstSensorInGroup, reset per hardware group
                    // below), not tied to a specific sensor identity, so
                    // it automatically follows whichever sensor Move Up/
                    // Move Down has sorted to the top of list via
                    // gadget.order - no separate tracking needed.
                    //
                    // The name column's X shift below is unconditional
                    // (applied whenever Row Icons is on at all, not just on
                    // the row that actually draws one) - user-reported: a
                    // shift only on the icon row left every other row's
                    // name flush left instead, misaligning the whole
                    // column. Reserving the same icon-width gap on every
                    // row keeps names lined up whether or not that
                    // particular row has an icon.
                    if (_rowIconMode != RowIconMode.Off)
                    {
                        if (isFirstSensorInGroup)
                        {
                            // Fork fix: centered against _sensorLineHeight
                            // alone, not _sensorLineHeight + extraPerStep.
                            // The row's text (DrawString below) is always
                            // top-anchored at y - 1 with an auto-grow
                            // height - extraPerStep only adds blank space
                            // *below* that text before the next row starts
                            // (see the vertical drag-to-resize feature), it
                            // never moves the text itself. Centering
                            // against the wider row+spacing slot pulled the
                            // icon down into that blank gap, away from the
                            // text it's meant to sit beside; centering
                            // against just _sensorLineHeight keeps it
                            // aligned with the actual text regardless of
                            // how much extra spacing the user has dragged
                            // in.
                            int iconY = y + (_sensorLineHeight - _iconSize) / 2;
                            g.DrawImage(HardwareTypeImage.Instance.GetImage(hardware.HardwareType), new Rectangle(nameX - 1, iconY, _iconSize, _iconSize));
                        }
                        nameX += _iconSize + 1;
                    }

                    // Icons Only: the device icon replaces the name on the
                    // one row that has it; every other row in the group is
                    // left with no name and no icon at all (user's explicit
                    // choice - rows after the first aren't meant to be
                    // individually identified in this mode).
                    remainingWidth -= nameX - _leftMargin + 2;
                    if (remainingWidth > 0 && _rowIconMode != RowIconMode.IconsOnly)
                    {
                        g.DrawString(ResolveSensorDisplayName(_settings, sensor), _smallFont, GetColorBrush(ResolveSensorNameColor(sensor)), new RectangleF(nameX - 1, y - 1, remainingWidth, 0), _trimStringFormat);
                    }
                    y += _sensorLineHeight + extraPerStep;
                    isFirstSensorInGroup = false;
                }
            }

            // Temporary diagnostic (vertical-drag-to-resize crop
            // investigation, round 3): the geometry layer (WM_WINDOWPOSCHANGING
            // -> SnapHeight -> UserResized -> Redraw -> UpdateLayeredWindow)
            // has been logged as internally consistent across multiple large
            // drag sessions with no exceptions or Win32 failures, yet the user
            // still reports a crop. This narrows the remaining unverified
            // layer down to paint content itself: does the row loop above
            // ever draw past the bottom of the buffer it's given? Logging the
            // final content bottom against Size.Height every paint (not just
            // on mismatch) so a divergence is visible in context, not just as
            // an isolated warning line.
            if (HasAnyDisplayedSensor())
                DebugLog.Write("Resize", $"SensorGadget.OnPaint: content bottom y={y} (+bottomMargin={_bottomMargin}={y + _bottomMargin}) vs Size.Height={Size.Height} extraPerStep={extraPerStep} sensorLineHeight={_sensorLineHeight} hardwareLineHeight={_hardwareLineHeight}");
        }
        catch (ArgumentException ex)
        {
            // #1425 - upstream's fix for some GDI+ draw calls throwing
            // ArgumentException on certain inputs (degenerate rectangles,
            // etc.). Silently skips the rest of this frame's drawing,
            // which - if it fires partway through the row loop - would
            // look exactly like the window being cropped, even though
            // the window itself is the right size. Temporary diagnostic
            // (vertical-drag-to-resize investigation): logging this was
            // previously silent, so there was no way to tell whether it
            // was ever actually firing.
            DebugLog.Write("Resize", $"OnPaint: caught ArgumentException (frame drawing stopped here) - {ex.Message}\n{ex.StackTrace}");
        }
    }

    private class HardwareComparer : IComparer<IHardware>
    {
        public int Compare(IHardware x, IHardware y)
        {
            switch (x)
            {
                case null when y == null:
                    return 0;
                case null:
                    return -1;
            }

            if (y == null)
                return 1;

            if (x.HardwareType != y.HardwareType)
                return x.HardwareType.CompareTo(y.HardwareType);

            return x.Identifier.CompareTo(y.Identifier);
        }
    }
}
