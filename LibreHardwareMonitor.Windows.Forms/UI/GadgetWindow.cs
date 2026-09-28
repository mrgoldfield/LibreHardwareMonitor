// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.
// If a copy of the MPL was not distributed with this file, You can obtain one at http://mozilla.org/MPL/2.0/.
// Copyright (C) LibreHardwareMonitor and Contributors.
// Partial Copyright (C) Michael M�ller <mmoeller@openhardwaremonitor.org> and Contributors.
// All Rights Reserved.

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using LibreHardwareMonitor.Windows.Forms.Utilities;

namespace LibreHardwareMonitor.Windows.Forms.UI;

public sealed class GadgetWindow : NativeWindow, IDisposable
{
    private bool _visible;
    private bool _alwaysOnTop;
    private byte _opacity = 255;
    private Point _location = new Point(100, 100);
    private Size _size = new Size(130, 84);
    private readonly MethodInfo _commandDispatch;
    private IntPtr _handleBitmapDC;
    private Size _bufferSize;
    private Graphics _graphics;

    // Temporary diagnostic (gadget-never-resizes investigation, see
    // CHANGELOG.md): set around our own UpdateLayeredWindow calls so
    // WM_WINDOWPOSCHANGING's unconditional entry log can show whether it
    // is being reentered as a side effect of a call we made ourselves,
    // as opposed to a genuine external (user-drag/OS) geometry change.
    private bool _inProgrammaticResize;

    public event EventHandler SizeChanged;
    public event EventHandler LocationChanged;
    public event HitTestEventHandler HitTest;
    public event MouseEventHandler MouseDoubleClick;

    // Fork addition: fires only when the size change came from a real
    // Win32 WM_WINDOWPOSCHANGING (the user dragging an edge, or the OS
    // moving/resizing the window) - never from this class's own Size
    // setter, which resizes via SetWindowPos directly and never goes
    // through WM_WINDOWPOSCHANGING. SizeChanged fires for both; this is
    // the subset a subclass can use to tell "the user chose this size"
    // apart from "something recomputed the auto-fit size" - see
    // SensorGadget's _widthManuallySet.
    //
    // Fork addition (vertical drag-to-resize): carries the size just
    // before this change, so a subclass can tell which dimension(s)
    // actually changed - a left/right-edge drag only changes Width, a
    // top/bottom-edge drag only changes Height, and comparing against
    // Size (the new value) alone can't distinguish them.
    public event EventHandler<Size> UserResized;

    // Fork addition: fires with the client-relative point right before the
    // context menu is shown, so a subclass (SensorGadget) can build the
    // menu around whatever was under the cursor - e.g. inserting per-row
    // "Move Up"/"Move Down" items when the click landed on a sensor row.
    public event EventHandler<Point> ContextMenuOpening;

    // Fork addition (vertical drag-to-resize, Gemini review Finding 2 -
    // see from_gemini_to_claude.md): an optional hook a subclass can set
    // to snap a raw drag height to some subclass-specific valid value
    // (e.g. SensorGadget snaps to a whole number of row-spacing steps)
    // before WM_WINDOWPOSCHANGING ever delivers it as Size/UserResized -
    // a Func<> rather than an event since we need a single answer back,
    // not a multicast notification. Null means no snapping (used as-is,
    // today's behavior for anyone who doesn't set it).
    public Func<int, int> SnapHeight;

    public GadgetWindow()
    {
        Type commandType = typeof(Form).Assembly.GetType("System.Windows.Forms.Command");
        _commandDispatch = commandType.GetMethod("DispatchID",
                                                 BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public,
                                                 null, new[] { typeof(int) }, null);

        CreateHandle(CreateParams);

        // Fork change: don't force the window to the bottom of the
        // Z-order on creation - that made the gadget start out hidden
        // behind whatever else was open. Leave it at its natural
        // (front-most, non-topmost) creation position; AlwaysOnTop
        // (default off) and the show-desktop handler still move it to
        // the bottom/top afterward as the user interacts with it.

        // prevent window from fading to a glass sheet when peek is invoked
        try
        {
            bool value = true;
            NativeMethods.DwmSetWindowAttribute(Handle, WindowAttribute.DWMWA_EXCLUDED_FROM_PEEK, ref value, Marshal.SizeOf(true));
        }
        catch (DllNotFoundException) { }
        catch (EntryPointNotFoundException) { }

        CreateBuffer();
    }

    private void ShowDesktopChanged(bool showDesktop)
    {
        DebugLog.Write("ZOrder", $"ShowDesktopChanged({showDesktop})");
        if (showDesktop)
            MoveToTopMost(Handle);
        else
            MoveToBottom(Handle);
    }

    private void MoveToBottom(IntPtr handle)
    {
        DebugLog.Write("ZOrder", "MoveToBottom");
        NativeMethods.SetWindowPos(handle, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
    }

    private void MoveToTopMost(IntPtr handle)
    {
        DebugLog.Write("ZOrder", "MoveToTopMost");
        NativeMethods.SetWindowPos(handle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
    }

    // Fork addition: a one-time "surface above whatever's currently in
    // front" nudge, distinct from AlwaysOnTop/MoveToTopMost above -
    // HWND_TOP (not HWND_TOPMOST) puts the window at the front of the
    // normal (non-topmost) Z-order band for just this call, it doesn't
    // stick there the way AlwaysOnTop does. Needed because the gadget is
    // made visible early in MainForm's constructor (naturally front-most
    // at that instant, nothing else from this app is on screen yet), but
    // MainForm.Show() and the modal StartupGuideDialog that follow it can
    // each become the active window afterward and end
    // up stacked in front of it - see MainForm's constructor, which calls
    // this last, after all of those have already run.
    public void BringToFront()
    {
        DebugLog.Write("ZOrder", "BringToFront");
        NativeMethods.SetWindowPos(Handle, HWND_TOP, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOSENDCHANGING);
    }

    private CreateParams CreateParams
    {
        get
        {
            CreateParams cp = new CreateParams
            {
                Width = 4096,
                Height = 4096,
                X = _location.X,
                Y = _location.Y,
                ExStyle = WS_EX_LAYERED | WS_EX_TOOLWINDOW
            };

            return cp;
        }
    }

    protected override void WndProc(ref Message message)
    {
        switch (message.Msg)
        {
            case WM_COMMAND:
                {
                    // need to dispatch the message for the context menu
                    if (message.LParam == IntPtr.Zero)
                        _commandDispatch.Invoke(null, new object[] {message.WParam.ToInt32() & 0xFFFF });
                }
                break;
            case WM_NCHITTEST:
                {
                    message.Result = (IntPtr)HitResult.Caption;
                    if (HitTest != null)
                    {
                        Point p = new Point(
                                            Macros.GET_X_LPARAM(message.LParam) - _location.X,
                                            Macros.GET_Y_LPARAM(message.LParam) - _location.Y
                                           );
                        HitTestEventArgs e = new HitTestEventArgs(p, HitResult.Caption);
                        HitTest(this, e);
                        message.Result = (IntPtr)e.HitResult;
                    }
                }
                break;
            case WM_NCLBUTTONDBLCLK:
                {
                    MouseDoubleClick?.Invoke(this, new MouseEventArgs(MouseButtons.Left, 2, Macros.GET_X_LPARAM(message.LParam) - _location.X, Macros.GET_Y_LPARAM(message.LParam) - _location.Y, 0));
                    message.Result = IntPtr.Zero;
                }
                break;
            case WM_NCRBUTTONDOWN:
                {
                    message.Result = IntPtr.Zero;
                }
                break;
            case WM_NCRBUTTONUP:
                {
                    Point screen = new Point(Macros.GET_X_LPARAM(message.LParam), Macros.GET_Y_LPARAM(message.LParam));
                    ContextMenuOpening?.Invoke(this, new Point(screen.X - _location.X, screen.Y - _location.Y));
                    ContextMenuStrip?.Show(screen);
                    message.Result = IntPtr.Zero;
                }
                break;
            case WM_GETMINMAXINFO:
                {
                    // Log what Windows proposed by default (see the struct's
                    // doc comment above for why this window - style-less,
                    // custom hit-test-driven resize - is exactly the kind of
                    // window where that default can be an unwanted, silent
                    // constraint), then override both track sizes generously
                    // so the interactive resize loop is never limited by
                    // anything but our own SnapHeight/screen-edge logic.
                    MINMAXINFO mmi = (MINMAXINFO)Marshal.PtrToStructure(message.LParam, typeof(MINMAXINFO));
                    DebugLog.Write("Resize",
                                   $"WM_GETMINMAXINFO: OS default ptMinTrackSize=({mmi.ptMinTrackSize.X},{mmi.ptMinTrackSize.Y}) "
                                   + $"ptMaxTrackSize=({mmi.ptMaxTrackSize.X},{mmi.ptMaxTrackSize.Y}) ptMaxSize=({mmi.ptMaxSize.X},{mmi.ptMaxSize.Y}) "
                                   + $"currentSize={_size}");

                    mmi.ptMinTrackSize = new POINT { X = 50, Y = 50 };
                    mmi.ptMaxTrackSize = new POINT { X = 10000, Y = 10000 };
                    Marshal.StructureToPtr(mmi, message.LParam, false);
                    message.Result = IntPtr.Zero;
                }
                break;
            case WM_WINDOWPOSCHANGING:
                {
                    // Fork fix (round-6, see from_gemini_to_claude.md's
                    // "Root Cause Identified" note): this handler now only
                    // computes geometry (clamping/snapping) and forwards it
                    // - it never touches _size/_location or fires events
                    // any more. WM_WINDOWPOSCHANGING is sent BEFORE the OS
                    // applies the change; base.WndProc (DefWindowProc) here
                    // only validates/queries constraints (e.g.
                    // WM_GETMINMAXINFO), it does NOT resize the window or
                    // let DWM reallocate its compositor backing store yet -
                    // that only happens once this message returns to the OS
                    // and it sends the separate WM_WINDOWPOSCHANGED message,
                    // which is now what updates state and fires events (see
                    // that case below). Round 5's fix (firing after
                    // base.WndProc but still inside WM_WINDOWPOSCHANGING)
                    // was still too early for exactly this reason.
                    WINDOWPOS wp = (WINDOWPOS)Marshal.PtrToStructure(message.LParam, typeof(WINDOWPOS));

                    DebugLog.Write("Resize", $"WM_WINDOWPOSCHANGING ENTRY: wp=({wp.x},{wp.y},{wp.cx}x{wp.cy}) flags={wp.flags:X} currentSize={_size} currentLocation={_location} inProgrammaticResize={_inProgrammaticResize} lockPositionAndSize={LockPositionAndSize}");

                    if (!_inProgrammaticResize)
                    {
                        if (LockPositionAndSize)
                        {
                            wp.flags |= SWP_NOSIZE | SWP_NOMOVE;
                        }
                        else
                        {
                            // prevent the window from leaving the screen
                            if ((wp.flags & SWP_NOMOVE) == 0)
                            {
                                Rectangle rect = Screen.GetWorkingArea(new Rectangle(wp.x, wp.y, wp.cx, wp.cy));
                                const int margin = 16;
                                wp.x = Math.Max(wp.x, rect.Left - wp.cx + margin);
                                wp.x = Math.Min(wp.x, rect.Right - margin);
                                wp.y = Math.Max(wp.y, rect.Top - wp.cy + margin);
                                wp.y = Math.Min(wp.y, rect.Bottom - margin);
                            }

                            // Fork addition (vertical drag-to-resize): snap the
                            // raw drag height to whatever SnapHeight approves
                            // (e.g. a whole number of row-spacing steps - see
                            // SensorGadget.SnapHeightToRowSpacing) before
                            // anything downstream (position re-anchoring, the
                            // mismatch check, event firing) sees it, so the
                            // delivered Height is always exact instead of
                            // drifting between drag ticks and a later
                            // content-driven Resize() call - Gemini code
                            // review finding, see from_gemini_to_claude.md.
                            if ((wp.flags & SWP_NOSIZE) == 0 && SnapHeight != null)
                            {
                                int snappedCy = SnapHeight(wp.cy);
                                if (snappedCy != wp.cy)
                                {
                                    // A top-edge drag keeps the bottom edge
                                    // fixed (wp.y moves, wp.cy grows/shrinks);
                                    // a bottom-edge drag keeps the top edge
                                    // fixed (wp.y stays at _location.Y). Only
                                    // one edge at a time is ever draggable
                                    // (HitTest never returns a corner), so
                                    // wp.y != _location.Y unambiguously means
                                    // this is a top-edge drag - re-anchor the
                                    // bottom edge to where this message's own
                                    // (un-snapped) geometry already implied it
                                    // should be, so snapping cy doesn't also
                                    // silently shift the window.
                                    if ((wp.flags & SWP_NOMOVE) == 0 && wp.y != _location.Y)
                                        wp.y = (wp.y + wp.cy) - snappedCy;
                                    wp.cy = snappedCy;
                                }
                            }

                        }
                    }

                    // Forward the message to the base implementation (DefWindowProc). This only
                    // lets the OS validate/query the proposed geometry at this stage (e.g.
                    // WM_GETMINMAXINFO) - it does NOT yet apply the change or let DWM reallocate
                    // its compositor backing store. _size/_location and event firing now happen
                    // in WM_WINDOWPOSCHANGED below, once the OS has actually applied this.
                    Marshal.StructureToPtr(wp, message.LParam, false);
                    base.WndProc(ref message);
                }
                break;
            case WM_WINDOWPOSCHANGED:
                {
                    // Fork fix (round-6, see from_gemini_to_claude.md's "Root
                    // Cause Identified" note): WM_WINDOWPOSCHANGED is sent
                    // AFTER the OS has applied the geometry change and DWM
                    // has resized its compositor backing store to match -
                    // unlike WM_WINDOWPOSCHANGING (above), which fires
                    // before any of that happens. Updating _size/_location
                    // and firing UserResized/SizeChanged/LocationChanged
                    // here (instead of in WM_WINDOWPOSCHANGING, even after
                    // its own base.WndProc call - round 5's fix, still too
                    // early) means SensorGadget's Redraw() -> UpdateLayeredWindow
                    // - triggered synchronously by those events - always
                    // paints into a backing store that's already the right
                    // size, live drag included.
                    base.WndProc(ref message);

                    WINDOWPOS wp = (WINDOWPOS)Marshal.PtrToStructure(message.LParam, typeof(WINDOWPOS));
                    DebugLog.Write("Resize", $"WM_WINDOWPOSCHANGED: wp=({wp.x},{wp.y},{wp.cx}x{wp.cy}) flags={wp.flags:X} currentSize={_size} currentLocation={_location} inProgrammaticResize={_inProgrammaticResize}");

                    // Skip entirely for a resize GadgetWindow.Size's own
                    // setter drove (SWP_NOSENDCHANGING there only suppresses
                    // WM_WINDOWPOSCHANGING, not this message) - _size is
                    // already set directly and its caller (e.g.
                    // SensorGadget.Resize()) already calls Redraw() itself,
                    // so reacting here too would double-fire/double-redraw
                    // exactly like the original Gemini Finding 1.
                    if (_inProgrammaticResize)
                        break;

                    if ((wp.flags & SWP_NOMOVE) == 0 && (_location.X != wp.x || _location.Y != wp.y))
                    {
                        _location = new Point(wp.x, wp.y);
                        LocationChanged?.Invoke(this, EventArgs.Empty);
                    }

                    if ((wp.flags & SWP_NOSIZE) == 0 && (_size.Width != wp.cx || _size.Height != wp.cy))
                    {
                        Size previousSize = _size;
                        _size = new Size(wp.cx, wp.cy);
                        DebugLog.Write("Resize", $"WM_WINDOWPOSCHANGED: size changed {previousSize} -> {_size}, firing UserResized/SizeChanged");
                        // Fork fix (Gemini review Finding 1): UserResized
                        // still fires before SizeChanged - SensorGadget's
                        // SizeChanged handler calls Redraw() immediately,
                        // and UserResized is what recomputes
                        // _lineSpacingExtra from the new Height.
                        UserResized?.Invoke(this, previousSize);
                        SizeChanged?.Invoke(this, EventArgs.Empty);
                    }
                }
                break;
            default:
                {
                    base.WndProc(ref message);
                }
                break;
        }
    }

    private BlendFunction CreateBlendFunction()
    {
        return new BlendFunction { BlendOp = AC_SRC_OVER, BlendFlags = 0, SourceConstantAlpha = _opacity, AlphaFormat = AC_SRC_ALPHA };
    }

    private void CreateBuffer()
    {
        IntPtr handleScreenDC = NativeMethods.GetDC(IntPtr.Zero);
        _handleBitmapDC = NativeMethods.CreateCompatibleDC(handleScreenDC);
        NativeMethods.ReleaseDC(IntPtr.Zero, handleScreenDC);
        _bufferSize = _size;

        BITMAPINFO info = new BITMAPINFO();
        info.Size = Marshal.SizeOf(info);
        info.Width = _size.Width;
        info.Height = -_size.Height;
        info.BitCount = 32;
        info.Planes = 1;

        IntPtr hBmp = NativeMethods.CreateDIBSection(_handleBitmapDC, ref info, 0, out IntPtr _, IntPtr.Zero, 0);
        if (hBmp == IntPtr.Zero)
            DebugLog.Write("Resize", $"CreateBuffer(): CreateDIBSection FAILED for size {_size}, Win32 error={Marshal.GetLastWin32Error()}");
        IntPtr hBmpOld = NativeMethods.SelectObject(_handleBitmapDC, hBmp);
        NativeMethods.DeleteObject(hBmpOld);

        _graphics = Graphics.FromHdc(_handleBitmapDC);

        if (Environment.OSVersion.Version.Major > 5)
        {
            _graphics.TextRenderingHint = TextRenderingHint.SystemDefault;
            _graphics.SmoothingMode = SmoothingMode.HighQuality;
        }
    }

    private void DisposeBuffer()
    {
        _graphics.Dispose();
        NativeMethods.DeleteDC(_handleBitmapDC);
    }

    public void Dispose()
    {
        DisposeBuffer();
    }

    public PaintEventHandler Paint;

    public void Redraw()
    {
        if (!_visible || Paint == null)
        {
            DebugLog.Write("Resize", $"Redraw() skipped: visible={_visible} paintHandlerSet={Paint != null}");
            return;
        }

        if (_size != _bufferSize)
        {
            DebugLog.Write("Resize", $"Redraw(): recreating buffer, _bufferSize {_bufferSize} -> {_size}");
            DisposeBuffer();
            CreateBuffer();
        }

        Paint(this, new PaintEventArgs(_graphics, new Rectangle(Point.Empty, _size)));
        Point pointSource = Point.Empty;
        BlendFunction blend = CreateBlendFunction();
        bool ok = NativeMethods.UpdateLayeredWindow(Handle, IntPtr.Zero, IntPtr.Zero, ref _size, _handleBitmapDC, ref pointSource, 0, ref blend, ULW_ALPHA);
        if (!ok)
            DebugLog.Write("Resize", $"Redraw(): UpdateLayeredWindow FAILED for size {_size}, Win32 error={Marshal.GetLastWin32Error()}");
        NativeMethods.GetWindowRect(Handle, out RECT actualRect);
        DebugLog.Write("Resize", $"Redraw(): after UpdateLayeredWindow ok={ok}, actual GetWindowRect size={actualRect.Right - actualRect.Left}x{actualRect.Bottom - actualRect.Top} at ({actualRect.Left},{actualRect.Top})");
        // make sure the window is at the right location
        NativeMethods.SetWindowPos(Handle, IntPtr.Zero, _location.X, _location.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);
    }

    public byte Opacity
    {
        get
        {
            return _opacity;
        }
        set
        {
            if (_opacity != value)
            {
                _opacity = value;
                BlendFunction blend = CreateBlendFunction();
                NativeMethods.UpdateLayeredWindow(Handle, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, 0, ref blend, ULW_ALPHA);
            }
        }
    }

    public bool Visible
    {
        get
        {
            return _visible;
        }
        set
        {
            if (_visible != value)
            {
                DebugLog.Write("ZOrder", $"Visible: {_visible} -> {value} (size={_size}, location={_location}, alwaysOnTop={_alwaysOnTop})");
                _visible = value;
                NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER | (value ? SWP_SHOWWINDOW : SWP_HIDEWINDOW));

                if (value)
                {
                    if (!_alwaysOnTop)
                        ShowDesktop.Instance.ShowDesktopChanged += ShowDesktopChanged;
                }
                else
                {
                    if (!_alwaysOnTop)
                        ShowDesktop.Instance.ShowDesktopChanged -= ShowDesktopChanged;
                }
            }
        }
    }

    // if locked, the window can not be moved or resized
    public bool LockPositionAndSize { get; set; }

    public bool AlwaysOnTop
    {
        get
        {
            return _alwaysOnTop;
        }
        set
        {
            if (value != _alwaysOnTop)
            {
                DebugLog.Write("ZOrder", $"AlwaysOnTop: {_alwaysOnTop} -> {value}");
                _alwaysOnTop = value;

                if (_alwaysOnTop)
                {
                    if (_visible)
                        ShowDesktop.Instance.ShowDesktopChanged -= ShowDesktopChanged;

                    MoveToTopMost(Handle);
                }
                else
                {
                    MoveToBottom(Handle);

                    if (_visible)
                        ShowDesktop.Instance.ShowDesktopChanged += ShowDesktopChanged;
                }
            }
        }
    }

    public Size Size
    {
        get
        {
            return _size;
        }
        set
        {
            if (_size != value)
            {
                DebugLog.Write("Resize", $"GadgetWindow.Size setter: {_size} -> {value}");
                _size = value;
                _inProgrammaticResize = true;
                bool ok;
                try
                {
                    // Use SetWindowPos to programmatically resize the native window's physical bounds.
                    // This informs the OS and DWM compositor of the new size, so they allocate
                    // a larger compositing surface and prevent clipping.
                    ok = NativeMethods.SetWindowPos(Handle, IntPtr.Zero, 0, 0, _size.Width, _size.Height, SWP_NOMOVE | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);
                }
                finally
                {
                    _inProgrammaticResize = false;
                }
                if (!ok)
                    DebugLog.Write("Resize", $"GadgetWindow.Size setter: SetWindowPos FAILED for size {_size}, Win32 error={Marshal.GetLastWin32Error()}");
                NativeMethods.GetWindowRect(Handle, out RECT actualRect);
                DebugLog.Write("Resize", $"GadgetWindow.Size setter: after SetWindowPos ok={ok}, actual GetWindowRect size={actualRect.Right - actualRect.Left}x{actualRect.Bottom - actualRect.Top} at ({actualRect.Left},{actualRect.Top})");
                SizeChanged?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                DebugLog.Write("Resize", $"GadgetWindow.Size setter: no-op, already {_size} (requested {value})");
            }
        }
    }

    public Point Location
    {
        get
        {
            return _location;
        }
        set
        {
            if (_location != value)
            {
                _location = value;
                NativeMethods.SetWindowPos(Handle, IntPtr.Zero, _location.X, _location.Y, 0, 0, SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOZORDER | SWP_NOSENDCHANGING);
                LocationChanged?.Invoke(this, EventArgs.Empty);
            }
        }
    }

    public ContextMenuStrip ContextMenuStrip { get; set; }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct BlendFunction
    {
        public byte BlendOp;
        public byte BlendFlags;
        public byte SourceConstantAlpha;
        public byte AlphaFormat;
    }

    // Temporary diagnostic (gadget-never-resizes investigation): lets us
    // compare what the OS actually thinks the window's bounds are
    // against our own _size field after a call we believe changed them.
    [StructLayout(LayoutKind.Sequential)]
    private struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    // Round-3 diagnostic/fix (vertical-drag-to-resize "beginning size
    // becomes an upper limit" report): this window is a plain
    // NativeWindow created with CreateParams.Style left at its default
    // (0) - no WS_THICKFRAME, no WS_CAPTION. All resizing is done
    // manually via the WM_NCHITTEST HitResult.Top/Bottom/Left/Right
    // override plus the OS's native interactive NC-drag loop, not a
    // standard sizable-window style. WM_GETMINMAXINFO was never
    // intercepted, meaning Windows' own default ptMinTrackSize/
    // ptMaxTrackSize computation - which can be considerably more
    // conservative for a style-less window like this one than for an
    // ordinary resizable window - has been silently constraining every
    // interactive drag this whole time with no visibility into it from
    // any of the WM_WINDOWPOSCHANGING logging added so far (that log
    // only sees whatever wp.cy the drag loop was already willing to
    // propose, post-clamp).
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MINMAXINFO
    {
        public POINT ptReserved;
        public POINT ptMaxSize;
        public POINT ptMaxPosition;
        public POINT ptMinTrackSize;
        public POINT ptMaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential, Pack = 1)]
    private struct WINDOWPOS
    {
        public readonly IntPtr hwnd;
        public readonly IntPtr hwndInsertAfter;
        public int x;
        public int y;
        // Fork fix (vertical drag-to-resize): cx/cy need to be writable
        // so the handler can snap a raw drag height via SnapHeight
        // before writing the struct back and forwarding it - widening
        // C# field access doesn't change the native memory layout this
        // marshals against, so it's safe.
        public int cx;
        public int cy;
        public uint flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct BITMAPINFO
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
        public int Colors;
    }

    public static readonly IntPtr HWND_TOP = (IntPtr)0;
    public static readonly IntPtr HWND_BOTTOM = (IntPtr)1;
    public static readonly IntPtr HWND_TOPMOST = (IntPtr)(-1);

    public const int WS_EX_LAYERED = 0x00080000;
    public const int WS_EX_TOOLWINDOW = 0x00000080;

    public const uint SWP_NOSIZE = 0x0001;
    public const uint SWP_NOMOVE = 0x0002;
    public const uint SWP_NOACTIVATE = 0x0010;
    public const uint SWP_FRAMECHANGED = 0x0020;
    public const uint SWP_HIDEWINDOW = 0x0080;
    public const uint SWP_SHOWWINDOW = 0x0040;
    public const uint SWP_NOZORDER = 0x0004;
    public const uint SWP_NOSENDCHANGING = 0x0400;

    public const int ULW_COLORKEY = 0x00000001;
    public const int ULW_ALPHA = 0x00000002;
    public const int ULW_OPAQUE = 0x00000004;

    public const byte AC_SRC_OVER = 0x00;
    public const byte AC_SRC_ALPHA = 0x01;

    public const int WM_GETMINMAXINFO = 0x0024;
    public const int WM_NCHITTEST = 0x0084;
    public const int WM_NCLBUTTONDBLCLK = 0x00A3;
    public const int WM_NCLBUTTONDOWN = 0x00A1;
    public const int WM_NCLBUTTONUP = 0x00A2;
    public const int WM_NCRBUTTONDOWN = 0x00A4;
    public const int WM_NCRBUTTONUP = 0x00A5;
    public const int WM_WINDOWPOSCHANGING = 0x0046;
    public const int WM_WINDOWPOSCHANGED = 0x0047;
    public const int WM_COMMAND = 0x0111;

    public const int TPM_RIGHTBUTTON = 0x0002;
    public const int TPM_VERTICAL = 0x0040;

    private enum WindowAttribute : int
    {
        DWMWA_NCRENDERING_ENABLED = 1,
        DWMWA_NCRENDERING_POLICY,
        DWMWA_TRANSITIONS_FORCEDISABLED,
        DWMWA_ALLOW_NCPAINT,
        DWMWA_CAPTION_BUTTON_BOUNDS,
        DWMWA_NONCLIENT_RTL_LAYOUT,
        DWMWA_FORCE_ICONIC_REPRESENTATION,
        DWMWA_FLIP3D_POLICY,
        DWMWA_EXTENDED_FRAME_BOUNDS,
        DWMWA_HAS_ICONIC_BITMAP,
        DWMWA_DISALLOW_PEEK,
        DWMWA_EXCLUDED_FROM_PEEK,
        DWMWA_LAST
    }

    /// <summary>
    /// Some macros imported and converted from the Windows SDK
    /// </summary>
    private static class Macros
    {
        public static ushort LOWORD(IntPtr l)
        {
            return (ushort)((ulong)l & 0xFFFF);
        }

        public static ushort HIWORD(IntPtr l)
        {
            return (ushort)(((ulong)l >> 16) & 0xFFFF);
        }

        public static int GET_X_LPARAM(IntPtr lp)
        {
            return (short)LOWORD(lp);
        }

        public static int GET_Y_LPARAM(IntPtr lp)
        {
            return (short)HIWORD(lp);
        }
    }

    /// <summary>
    /// Imported native methods
    /// </summary>
    private static class NativeMethods
    {
        private const string USER = "user32.dll";
        private const string GDI = "gdi32.dll";
        private const string DWMAPI = "dwmapi.dll";

        [DllImport(USER, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref Size psize, IntPtr hdcSrc, IntPtr pprSrc, int crKey, IntPtr pblend, int dwFlags);

        [DllImport(USER, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, ref Size psize, IntPtr hdcSrc, ref Point pprSrc, int crKey, ref BlendFunction pblend, int dwFlags);

        [DllImport(USER, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool UpdateLayeredWindow(IntPtr hwnd, IntPtr hdcDst, IntPtr pptDst, IntPtr psize, IntPtr hdcSrc, IntPtr pprSrc, int crKey, ref BlendFunction pblend, int dwFlags);

        [DllImport(USER, CallingConvention = CallingConvention.Winapi)]
        public static extern IntPtr GetDC(IntPtr hWnd);

        [DllImport(USER, CallingConvention = CallingConvention.Winapi)]
        public static extern int ReleaseDC(IntPtr hWnd, IntPtr hDC);

        [DllImport(USER, CallingConvention = CallingConvention.Winapi)]
        public static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport(USER, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport(USER, CallingConvention = CallingConvention.Winapi)]
        public static extern bool TrackPopupMenuEx(IntPtr hMenu, uint uFlags, int x, int y, IntPtr hWnd, IntPtr tpmParams);

        [DllImport(GDI, CallingConvention = CallingConvention.Winapi)]
        public static extern IntPtr CreateCompatibleDC(IntPtr hDC);

        [DllImport(GDI, CallingConvention = CallingConvention.Winapi, SetLastError = true)]
        public static extern IntPtr CreateDIBSection(IntPtr hdc, [In] ref BITMAPINFO pbmi, uint pila, out IntPtr ppvBits, IntPtr hSection, uint dwOffset);

        [DllImport(GDI, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteDC(IntPtr hdc);

        [DllImport(GDI, CallingConvention = CallingConvention.Winapi)]
        public static extern IntPtr SelectObject(IntPtr hDC, IntPtr hObject);

        [DllImport(GDI, CallingConvention = CallingConvention.Winapi)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool DeleteObject(IntPtr hObject);

        [DllImport(DWMAPI, CallingConvention = CallingConvention.Winapi)]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, WindowAttribute dwAttribute, ref bool pvAttribute, int cbAttribute);
    }
}

public delegate void HitTestEventHandler(object sender, HitTestEventArgs e);

public enum HitResult
{
    Transparent = -1,
    Nowhere = 0,
    Client = 1,
    Caption = 2,
    Left = 10,
    Right = 11,
    Top = 12,
    TopLeft = 13,
    TopRight = 14,
    Bottom = 15,
    BottomLeft = 16,
    BottomRight = 17,
    Border = 18
}

public class HitTestEventArgs : EventArgs
{
    public HitTestEventArgs(Point location, HitResult hitResult)
    {
        Location = location;
        HitResult = hitResult;
    }
    public Point Location { get; }
    public HitResult HitResult { get; set; }
}