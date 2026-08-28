using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Platform;
using Avalonia.Threading;
using LibVLCSharp.Shared;

namespace BgIptvPlayer.Native;

/// <summary>
/// Hosts LibVLC in a real child HWND owned by the Avalonia window.
/// This avoids LibVLC creating its own Direct3D output window.
/// </summary>
public sealed class NativeVlcHost : NativeControlHost
{
    private const int GwlWndProc = -4;
    private const uint WmKeyDown = 0x0100;
    private const int VirtualKeyEscape = 0x1B;
    private const int VirtualKeyLeftButton = 0x01;
    private const uint WsChild = 0x40000000;
    private const uint WsVisible = 0x10000000;
    private const uint WsClipSiblings = 0x04000000;
    private const uint WsClipChildren = 0x02000000;

    private IntPtr _videoHandle;
    private IntPtr _originalWindowProc;
    private WindowProc? _windowProc;
    private MediaPlayer? _mediaPlayer;
    private DispatcherTimer? _mouseTimer;
    private bool _leftButtonWasDown;
    private uint _lastClickTime;
    private int _lastClickX;
    private int _lastClickY;
    private int _lastMouseX = int.MinValue;
    private int _lastMouseY = int.MinValue;
    private long _lastMoveNotification;

    public event EventHandler? VideoDoubleClicked;
    public event EventHandler? EscapePressed;
    public event EventHandler? VideoMouseMoved;

    public MediaPlayer? MediaPlayer
    {
        get => _mediaPlayer;
        set
        {
            if (ReferenceEquals(_mediaPlayer, value)) return;
            if (_mediaPlayer is not null && _videoHandle != IntPtr.Zero)
                _mediaPlayer.Hwnd = IntPtr.Zero;

            _mediaPlayer = value;
            AttachPlayer();
        }
    }

    protected override IPlatformHandle CreateNativeControlCore(IPlatformHandle parent)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException("Bu görüntü alanının macOS karşılığı henüz eklenmedi.");

        _videoHandle = CreateWindowEx(
            0,
            "STATIC",
            string.Empty,
            WsChild | WsVisible | WsClipSiblings | WsClipChildren,
            0, 0, 1, 1,
            parent.Handle,
            IntPtr.Zero,
            GetModuleHandle(null),
            IntPtr.Zero);

        if (_videoHandle == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "VLC görüntü alanı oluşturulamadı.");

        AttachPlayer();
        SubclassVideoWindow();
        StartMouseTracking();
        return new PlatformHandle(_videoHandle, "HWND");
    }

    protected override void DestroyNativeControlCore(IPlatformHandle control)
    {
        if (_mediaPlayer is not null)
            _mediaPlayer.Hwnd = IntPtr.Zero;

        _mouseTimer?.Stop();
        _mouseTimer = null;

        if (_videoHandle != IntPtr.Zero)
        {
            if (_originalWindowProc != IntPtr.Zero)
            {
                SetWindowProc(_videoHandle, _originalWindowProc);
                _originalWindowProc = IntPtr.Zero;
            }
            DestroyWindow(_videoHandle);
            _videoHandle = IntPtr.Zero;
        }
        _windowProc = null;
    }

    private void AttachPlayer()
    {
        if (_mediaPlayer is null || _videoHandle == IntPtr.Zero) return;
        _mediaPlayer.Hwnd = _videoHandle;
        _mediaPlayer.EnableMouseInput = false;
        _mediaPlayer.EnableKeyInput = false;
    }

    private void SubclassVideoWindow()
    {
        _windowProc = VideoWindowProc;
        _originalWindowProc = SetWindowProc(_videoHandle, Marshal.GetFunctionPointerForDelegate(_windowProc));
        if (_originalWindowProc == IntPtr.Zero)
            throw new Win32Exception(Marshal.GetLastWin32Error(), "Video tıklama alanı hazırlanamadı.");
    }

    private IntPtr VideoWindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam)
    {
        if (message == WmKeyDown && wParam.ToInt32() == VirtualKeyEscape)
        {
            Dispatcher.UIThread.Post(() => EscapePressed?.Invoke(this, EventArgs.Empty));
            return IntPtr.Zero;
        }

        return CallWindowProc(_originalWindowProc, window, message, wParam, lParam);
    }

    private void StartMouseTracking()
    {
        _mouseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(25) };
        _mouseTimer.Tick += (_, _) => PollVideoClicks();
        _mouseTimer.Start();
    }

    private void PollVideoClicks()
    {
        if (GetCursorPos(out var currentPoint) && GetWindowRect(_videoHandle, out var currentBounds) &&
            currentPoint.X >= currentBounds.Left && currentPoint.X < currentBounds.Right && currentPoint.Y >= currentBounds.Top && currentPoint.Y < currentBounds.Bottom &&
            (currentPoint.X != _lastMouseX || currentPoint.Y != _lastMouseY))
        {
            _lastMouseX = currentPoint.X;
            _lastMouseY = currentPoint.Y;
            var nowTicks = Environment.TickCount64;
            if (nowTicks - _lastMoveNotification >= 80)
            {
                _lastMoveNotification = nowTicks;
                VideoMouseMoved?.Invoke(this, EventArgs.Empty);
            }
        }

        var isDown = (GetAsyncKeyState(VirtualKeyLeftButton) & 0x8000) != 0;
        if (isDown && !_leftButtonWasDown && GetCursorPos(out var point) && GetWindowRect(_videoHandle, out var bounds) &&
            point.X >= bounds.Left && point.X < bounds.Right && point.Y >= bounds.Top && point.Y < bounds.Bottom)
        {
            var now = unchecked((uint)Environment.TickCount);
            var closeEnough = Math.Abs(point.X - _lastClickX) <= GetSystemMetrics(36) / 2 && Math.Abs(point.Y - _lastClickY) <= GetSystemMetrics(37) / 2;
            if (_lastClickTime != 0 && unchecked(now - _lastClickTime) <= GetDoubleClickTime() && closeEnough)
            {
                _lastClickTime = 0;
                VideoDoubleClicked?.Invoke(this, EventArgs.Empty);
            }
            else
            {
                _lastClickTime = now;
                _lastClickX = point.X;
                _lastClickY = point.Y;
            }
        }
        _leftButtonWasDown = isDown;
    }

    private static IntPtr SetWindowProc(IntPtr window, IntPtr procedure) =>
        IntPtr.Size == 8 ? SetWindowLongPtr(window, GwlWndProc, procedure) : new IntPtr(SetWindowLong(window, GwlWndProc, procedure.ToInt32()));

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowEx(
        uint extendedStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr parameter);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(IntPtr window);

    private delegate IntPtr WindowProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr(IntPtr window, int index, IntPtr newValue);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong(IntPtr window, int index, int newValue);

    [DllImport("user32.dll")]
    private static extern IntPtr CallWindowProc(IntPtr previousProcedure, IntPtr window, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    private static extern uint GetDoubleClickTime();

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int virtualKey);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(IntPtr window, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint { public int X; public int Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect { public int Left; public int Top; public int Right; public int Bottom; }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string? moduleName);
}
