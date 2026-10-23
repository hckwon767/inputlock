using System.Drawing.Drawing2D;
using Microsoft.Win32;

namespace InputLockTimer;

internal static class TrayIcons
{
    /// <summary>색 원 안에 자물쇠 모양을 그린 트레이 아이콘을 코드로 생성합니다.</summary>
    public static Icon Create(Color background, bool locked)
    {
        using var bitmap = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bitmap))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(Color.Transparent);

            using var fill = new SolidBrush(background);
            g.FillEllipse(fill, 1, 1, 30, 30);

            using var pen = new Pen(Color.White, 3f);
            using var white = new SolidBrush(Color.White);
            if (locked)
            {
                g.DrawArc(pen, 10, 5, 12, 14, 180, 180);
                g.DrawLine(pen, 10, 12, 10, 16);
                g.DrawLine(pen, 22, 12, 22, 16);
            }
            else
            {
                g.DrawArc(pen, 10, 5, 12, 14, 180, 180);
                g.DrawLine(pen, 10, 12, 10, 16);
            }
            g.FillRectangle(white, 8, 16, 16, 11);
        }

        IntPtr handle = bitmap.GetHicon();
        try
        {
            using var temp = Icon.FromHandle(handle);
            return (Icon)temp.Clone();
        }
        finally
        {
            NativeMethods.DestroyIcon(handle);
        }
    }
}

/// <summary>트레이 아이콘, 메뉴, 설정 창을 관리하는 애플리케이션 컨텍스트.</summary>
internal sealed class TrayApp : ApplicationContext
{
    private readonly Control _ui = new();
    private readonly NotifyIcon _tray;
    private readonly ContextMenuStrip _menu = new();
    private readonly ToolStripMenuItem _statusItem = new("") { Enabled = false };
    private readonly ToolStripMenuItem _enableItem = new("잠금 기능 사용(&E)");
    private readonly Dictionary<LockState, Icon> _icons;
    private readonly LockController _controller;
    private AppSettings _settings;
    private SettingsForm? _settingsForm;
    private LockState? _shownIconState;

    public TrayApp()
    {
        _ = _ui.Handle; // 후크·이벤트를 UI 스레드로 넘길 때 쓸 핸들을 미리 만듭니다.

        _settings = SettingsStore.Load(out bool fileExisted, out string? loadWarning);

        _icons = new Dictionary<LockState, Icon>
        {
            [LockState.Disabled] = TrayIcons.Create(Color.FromArgb(117, 117, 117), false),
            [LockState.Idle] = TrayIcons.Create(Color.FromArgb(46, 125, 50), false),
            [LockState.Countdown] = TrayIcons.Create(Color.FromArgb(245, 124, 0), false),
            [LockState.Locked] = TrayIcons.Create(Color.FromArgb(198, 40, 40), true),
        };

        _controller = new LockController(_ui, _settings);

        var settingsItem = new ToolStripMenuItem("설정(&S)...");
        settingsItem.Click += (_, _) => ShowSettings(false);
        _enableItem.Click += (_, _) => ToggleEnabled();
        var exitItem = new ToolStripMenuItem("종료(&X)");
        exitItem.Click += (_, _) => ExitThread();
        _menu.Items.AddRange(new ToolStripItem[]
        {
            _statusItem,
            new ToolStripSeparator(),
            settingsItem,
            _enableItem,
            new ToolStripSeparator(),
            exitItem,
        });

        _tray = new NotifyIcon
        {
            ContextMenuStrip = _menu,
            Icon = _icons[LockState.Disabled],
            Text = "입력 잠금 타이머",
            Visible = true,
        };
        _tray.DoubleClick += (_, _) => ShowSettings(false);

        _controller.StatusChanged += (_, _) => RefreshUi();
        _controller.Notice += ShowNotice;

        SystemEvents.SessionSwitch += OnSessionSwitch;
        SystemEvents.PowerModeChanged += OnPowerModeChanged;

        RefreshUi();

        if (!fileExisted || loadWarning != null)
        {
            _ui.BeginInvoke(new Action(() =>
            {
                if (loadWarning != null) ShowNotice(loadWarning);
                ShowSettings(true);
            }));
        }
    }

    /// <summary>예기치 않은 오류 시 입력 차단이 남지 않도록 즉시 해제합니다.</summary>
    public void EmergencyRelease() => _controller.ForceRelease();

    private void RefreshUi()
    {
        var state = _controller.State;
        if (_shownIconState != state)
        {
            _tray.Icon = _icons[state];
            _shownIconState = state;
        }

        string status = _controller.StatusText;
        _statusItem.Text = status;
        _enableItem.Checked = _settings.Enabled;

        string text = "입력 잠금 타이머 · " + status;
        _tray.Text = text.Length > 120 ? text[..120] : text;
    }

    private void ShowNotice(string message) =>
        _tray.ShowBalloonTip(5000, "입력 잠금 타이머", message, ToolTipIcon.Warning);

    private void ToggleEnabled()
    {
        var next = _settings.Clone();
        next.Enabled = !next.Enabled;

        if (next.Enabled)
        {
            var error = next.Validate();
            if (error != null)
            {
                ShowNotice(error);
                ShowSettings(false);
                return;
            }
        }

        try
        {
            ApplySettings(next);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            ShowNotice("설정을 파일에 저장하지 못했습니다: " + ex.Message);
        }
    }

    /// <summary>설정을 적용하고 파일에 저장합니다. 저장 실패 시 예외를 던지지만 적용은 이미 끝난 상태입니다.</summary>
    private void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        _controller.ApplySettings(_settings);
        RefreshUi();
        SettingsStore.Save(_settings);
    }

    private void ShowSettings(bool firstRun)
    {
        if (_settingsForm != null)
        {
            _settingsForm.Activate();
            return;
        }

        using var form = new SettingsForm(_controller, _settings, ApplySettings, firstRun);
        _settingsForm = form;
        try
        {
            form.ShowDialog();
        }
        finally
        {
            _settingsForm = null;
        }
    }

    // 로그아웃·화면 잠금(Win+L)·원격 연결 해제·절전 때는 입력 잠금을 풀고 타이머를 초기화합니다.
    // (Windows 보안 화면은 가로채지 않습니다.)
    private void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        if (e.Reason is SessionSwitchReason.SessionLock
            or SessionSwitchReason.SessionLogoff
            or SessionSwitchReason.ConsoleDisconnect
            or SessionSwitchReason.RemoteDisconnect)
        {
            PostRelease();
        }
    }

    private void OnPowerModeChanged(object? sender, PowerModeChangedEventArgs e)
    {
        if (e.Mode is PowerModes.Suspend or PowerModes.Resume)
            PostRelease();
    }

    private void PostRelease()
    {
        try
        {
            if (_ui.IsHandleCreated && !_ui.IsDisposed)
                _ui.BeginInvoke(new Action(_controller.ForceRelease));
        }
        catch (InvalidOperationException) { }
    }

    protected override void ExitThreadCore()
    {
        SystemEvents.SessionSwitch -= OnSessionSwitch;
        SystemEvents.PowerModeChanged -= OnPowerModeChanged;

        _controller.Dispose(); // 후크 제거 및 눌린 채 남은 입력 해제

        _tray.Visible = false;
        _tray.Dispose();
        _menu.Dispose();
        foreach (var icon in _icons.Values) icon.Dispose();
        _ui.Dispose();

        base.ExitThreadCore();
    }
}
