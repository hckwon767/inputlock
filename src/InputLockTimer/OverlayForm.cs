using System.Drawing.Drawing2D;
using static InputLockTimer.NativeMethods;

namespace InputLockTimer;

/// <summary>
/// 카운트다운(화면 중앙)과 잠금 표시(화면 상단 배너)에 쓰는 최상위 OSD 창.
/// 포커스를 가져가지 않고 마우스 클릭을 통과시킵니다.
/// </summary>
internal sealed class OverlayForm : Form
{
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;
    private const int WS_EX_TRANSPARENT = 0x00000020;
    private const int WS_EX_LAYERED = 0x00080000;

    private enum OverlayMode { Countdown, Locked }

    private readonly Screen _screen;
    private OverlayMode _mode = OverlayMode.Countdown;
    private int _seconds;
    private string _hint = "";

    public OverlayForm(Screen screen)
    {
        _screen = screen;
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        TopMost = true;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.FromArgb(28, 28, 30);
        Opacity = 0.92;
        DoubleBuffered = true;
        SetStyle(ControlStyles.ResizeRedraw, true);
    }

    protected override bool ShowWithoutActivation => true;

    /// <summary>모니터 간 DPI가 달라도 Place에서 정한 크기를 그대로 유지합니다.</summary>
    protected override void OnDpiChanged(DpiChangedEventArgs e)
    {
        e.Cancel = true;
        base.OnDpiChanged(e);
    }

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE | WS_EX_TRANSPARENT | WS_EX_LAYERED;
            return cp;
        }
    }

    public void ShowCountdown(int seconds, string cancelHotkey)
    {
        _mode = OverlayMode.Countdown;
        _seconds = seconds;
        _hint = $"취소: {cancelHotkey}";
        Place(new Size(360, 220), atTop: false);
        Present();
    }

    public void ShowLocked(string unlockHotkey)
    {
        _mode = OverlayMode.Locked;
        _hint = $"{unlockHotkey} 로 해제";
        Place(new Size(520, 64), atTop: true);
        Present();
    }

    public void SetSeconds(int seconds)
    {
        if (_seconds == seconds) return;
        _seconds = seconds;
        Invalidate();
    }

    public void HideOverlay()
    {
        if (Visible) Hide();
    }

    /// <summary>전체 화면 앱이 뒤늦게 올라와도 가려지지 않도록 최상위 순서를 다시 지정합니다.</summary>
    public void EnsureTopMost()
    {
        if (IsHandleCreated && Visible)
        {
            SetWindowPos(Handle, HWND_TOPMOST, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_SHOWWINDOW);
        }
    }

    private void Present()
    {
        if (!Visible) Show();
        EnsureTopMost();
        Invalidate();
    }

    private void Place(Size logicalSize, bool atTop)
    {
        // 이 OSD가 속한 모니터 기준으로 배치·배율을 계산합니다(모니터마다 DPI가 다를 수 있음).
        var bounds = _screen.Bounds;
        float scale = NativeMethods.GetMonitorDpi(
            new Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2)) / 96f;
        var size = new Size((int)(logicalSize.Width * scale), (int)(logicalSize.Height * scale));

        int x = bounds.Left + (bounds.Width - size.Width) / 2;
        int y = atTop
            ? bounds.Top + (int)(32 * scale)
            : bounds.Top + (bounds.Height - size.Height) / 2;
        SetBounds(x, y, size.Width, size.Height);

        using var path = RoundedRect(new Rectangle(Point.Empty, size), (int)(18 * scale));
        var oldRegion = Region;
        Region = new Region(path);
        oldRegion?.Dispose();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        var g = e.Graphics;
        g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAliasGridFit;

        var rect = ClientRectangle;
        using var format = new StringFormat
        {
            Alignment = StringAlignment.Center,
            LineAlignment = StringAlignment.Center,
        };
        using var white = new SolidBrush(Color.White);
        using var gray = new SolidBrush(Color.FromArgb(205, 205, 205));

        if (_mode == OverlayMode.Countdown)
        {
            using var titleFont = new Font("Segoe UI", 14f);
            using var numberFont = new Font("Segoe UI", 66f, FontStyle.Bold);
            using var hintFont = new Font("Segoe UI", 12f);

            g.DrawString("입력이 곧 잠깁니다", titleFont, white,
                new RectangleF(0, rect.Height * 0.06f, rect.Width, rect.Height * 0.20f), format);
            g.DrawString(_seconds.ToString(), numberFont, white,
                new RectangleF(0, rect.Height * 0.24f, rect.Width, rect.Height * 0.50f), format);
            g.DrawString(_hint, hintFont, gray,
                new RectangleF(0, rect.Height * 0.76f, rect.Width, rect.Height * 0.20f), format);
        }
        else
        {
            using var font = new Font("Segoe UI", 13f, FontStyle.Bold);
            g.DrawString("입력 잠김  ·  " + _hint, font, white, rect, format);
        }
    }

    private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
    {
        int d = Math.Max(2, radius * 2);
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, d, d, 180, 90);
        path.AddArc(bounds.Right - d, bounds.Top, d, d, 270, 90);
        path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - d, d, d, 90, 90);
        path.CloseFigure();
        return path;
    }
}
