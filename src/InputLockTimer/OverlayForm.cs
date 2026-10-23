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

    private OverlayMode _mode = OverlayMode.Countdown;
    private int _seconds;
    private string _hint = "";

    public OverlayForm()
    {
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
        float scale = DeviceDpi / 96f;
        var screen = Screen.FromPoint(Cursor.Position);
        var size = new Size((int)(logicalSize.Width * scale), (int)(logicalSize.Height * scale));

        Size = size;
        int x = screen.Bounds.Left + (screen.Bounds.Width - size.Width) / 2;
        int y = atTop
            ? screen.Bounds.Top + (int)(32 * scale)
            : screen.Bounds.Top + (screen.Bounds.Height - size.Height) / 2;
        Location = new Point(x, y);

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
