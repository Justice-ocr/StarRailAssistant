using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace SRAFrontend.Server.Utils;

/// <summary>
/// 不调用 SRA-cli，直接截取崩坏：星穹铁道客户区。
/// </summary>
[SupportedOSPlatform("windows")]
public static class GameScreenshot
{
    private const string GameWindowTitle = "崩坏：星穹铁道";
    private const uint PwClientOnly = 1;
    private const uint PwRenderFullContent = 2;
    private static readonly IntPtr PerMonitorAwareV2 = new(-4);

    public static byte[]? CaptureGameWindowPng(out string? error)
    {
        var previousDpiContext = SetThreadDpiAwarenessContext(PerMonitorAwareV2);
        try
        {
            return CaptureCore(out error);
        }
        finally
        {
            if (previousDpiContext != IntPtr.Zero)
                SetThreadDpiAwarenessContext(previousDpiContext);
        }
    }

    private static byte[]? CaptureCore(out string? error)
    {
        error = null;
        var window = FindGameWindow();
        if (window == IntPtr.Zero)
        {
            error = "未找到 StarRail 游戏窗口";
            return null;
        }

        if (!TryGetClientSize(window, out var width, out var height))
        {
            error = "游戏窗口尚未准备好";
            return null;
        }

        var windowDc = GetWindowDC(window);
        if (windowDc == IntPtr.Zero)
        {
            error = "无法访问游戏窗口画面";
            return null;
        }

        var memoryDc = IntPtr.Zero;
        var bitmap = IntPtr.Zero;
        var previousBitmap = IntPtr.Zero;
        try
        {
            memoryDc = CreateCompatibleDC(windowDc);
            if (memoryDc == IntPtr.Zero)
            {
                error = "无法创建截图设备上下文";
                return null;
            }

            bitmap = CreateCompatibleBitmap(windowDc, width, height);
            if (bitmap == IntPtr.Zero)
            {
                error = "无法创建截图位图";
                return null;
            }

            previousBitmap = SelectObject(memoryDc, bitmap);
            var captured = PrintWindow(
                window,
                memoryDc,
                PwClientOnly | PwRenderFullContent);
            if (captured)
                return EncodePng(bitmap);
        }
        finally
        {
            if (previousBitmap != IntPtr.Zero && memoryDc != IntPtr.Zero)
                SelectObject(memoryDc, previousBitmap);
            if (bitmap != IntPtr.Zero)
                DeleteObject(bitmap);
            if (memoryDc != IntPtr.Zero)
                DeleteDC(memoryDc);
            ReleaseDC(window, windowDc);
        }

        if (IsIconic(window))
        {
            error = "游戏窗口已最小化，无法截图";
            return null;
        }

        if (!TryGetClientSize(window, out width, out height))
        {
            error = "无法读取游戏窗口的实际尺寸";
            return null;
        }

        return CaptureVisibleClientArea(window, width, height, out error);
    }

    private static byte[] EncodePng(IntPtr bitmap)
    {
        using var image = System.Drawing.Image.FromHbitmap(bitmap);
        using var output = new MemoryStream();
        image.Save(output, System.Drawing.Imaging.ImageFormat.Png);
        return output.ToArray();
    }

    private static byte[]? CaptureVisibleClientArea(
        IntPtr window,
        int width,
        int height,
        out string? error)
    {
        error = null;
        var previousForeground = GetForegroundWindow();
        var switchedForeground = previousForeground != window;
        if (switchedForeground && !TryActivateWindow(window))
        {
            error = "无法激活游戏窗口以完成截图";
            return null;
        }

        try
        {
            if (switchedForeground)
                Thread.Sleep(180);

            var origin = new NativePoint();
            if (!ClientToScreen(window, ref origin))
            {
                error = "无法读取游戏窗口屏幕坐标";
                return null;
            }

            using var bitmap = new System.Drawing.Bitmap(
                width,
                height,
                System.Drawing.Imaging.PixelFormat.Format32bppArgb);
            using var graphics = System.Drawing.Graphics.FromImage(bitmap);
            graphics.CopyFromScreen(
                origin.X,
                origin.Y,
                0,
                0,
                new System.Drawing.Size(width, height),
                System.Drawing.CopyPixelOperation.SourceCopy);
            using var output = new MemoryStream();
            bitmap.Save(output, System.Drawing.Imaging.ImageFormat.Png);
            return output.ToArray();
        }
        catch (Exception)
        {
            error = "屏幕捕获不可用，请确认游戏窗口未最小化";
            return null;
        }
        finally
        {
            if (switchedForeground)
            {
                SetWindowPos(
                    window,
                    new IntPtr(-2), // 取消窗口置顶
                    0,
                    0,
                    0,
                    0,
                    SwpNoMove | SwpNoSize | SwpNoActivate);
                if (previousForeground != IntPtr.Zero)
                {
                    SetWindowPos(
                        previousForeground,
                        IntPtr.Zero, // 恢复到顶层
                        0,
                        0,
                        0,
                        0,
                        SwpNoMove | SwpNoSize | SwpShowWindow);
                    SetForegroundWindow(previousForeground);
                }
            }
        }
    }

    private static bool TryActivateWindow(IntPtr window)
    {
        if (window == IntPtr.Zero)
            return false;

        var currentThread = GetCurrentThreadId();
        var targetThread = GetWindowThreadProcessId(window, out _);
        var foregroundWindow = GetForegroundWindow();
        var foregroundThread = foregroundWindow == IntPtr.Zero
            ? 0
            : GetWindowThreadProcessId(foregroundWindow, out _);
        var attachedTarget = targetThread != 0 &&
                             targetThread != currentThread &&
                             AttachThreadInput(currentThread, targetThread, true);
        var attachedForeground = foregroundThread != 0 &&
                                 foregroundThread != currentThread &&
                                 foregroundThread != targetThread &&
                                 AttachThreadInput(currentThread, foregroundThread, true);

        try
        {
            ShowWindow(window, 9); // 恢复窗口
            var raised = SetWindowPos(
                window,
                new IntPtr(-1), // 临时置顶
                0,
                0,
                0,
                0,
                SwpNoMove | SwpNoSize | SwpNoActivate | SwpShowWindow);
            SetForegroundWindow(window);
            SetFocus(window);
            return raised && IsWindowVisible(window);
        }
        finally
        {
            if (attachedForeground)
                AttachThreadInput(currentThread, foregroundThread, false);
            if (attachedTarget)
                AttachThreadInput(currentThread, targetThread, false);
        }
    }

    private static IntPtr FindGameWindow()
    {
        foreach (var process in Process.GetProcessesByName("StarRail"))
        {
            using (process)
            {
                var window = process.MainWindowHandle;
                if (window != IntPtr.Zero && TryGetClientSize(window, out _, out _))
                    return window;
            }
        }

        var titledWindow = FindWindow(null, GameWindowTitle);
        return titledWindow != IntPtr.Zero &&
               TryGetClientSize(titledWindow, out _, out _)
            ? titledWindow
            : IntPtr.Zero;
    }

    private static bool TryGetClientSize(IntPtr window, out int width, out int height)
    {
        width = 0;
        height = 0;
        if (!GetClientRect(window, out var rect))
            return false;

        width = rect.Right - rect.Left;
        height = rect.Bottom - rect.Top;
        return width > 0 && height > 0;
    }

    private const uint SwpNoSize = 0x0001;
    private const uint SwpNoMove = 0x0002;
    private const uint SwpNoActivate = 0x0010;
    private const uint SwpShowWindow = 0x0040;

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr FindWindow(string? className, string windowName);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(IntPtr window, out Rect rect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(IntPtr window, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr window);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(IntPtr window);

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(
        IntPtr window,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr window, int command);

    [DllImport("user32.dll")]
    private static extern IntPtr SetFocus(IntPtr window);

    [DllImport("kernel32.dll")]
    private static extern uint GetCurrentThreadId();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool AttachThreadInput(uint sourceThread, uint targetThread, bool attach);

    [DllImport("user32.dll")]
    private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern IntPtr GetWindowDC(IntPtr window);

    [DllImport("user32.dll")]
    private static extern int ReleaseDC(IntPtr window, IntPtr dc);

    [DllImport("user32.dll")]
    private static extern bool PrintWindow(IntPtr window, IntPtr dc, uint flags);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleDC(IntPtr dc);

    [DllImport("gdi32.dll")]
    private static extern IntPtr CreateCompatibleBitmap(
        IntPtr dc,
        int width,
        int height);

    [DllImport("gdi32.dll")]
    private static extern IntPtr SelectObject(IntPtr dc, IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteObject(IntPtr value);

    [DllImport("gdi32.dll")]
    private static extern bool DeleteDC(IntPtr dc);
}
