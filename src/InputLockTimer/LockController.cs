namespace InputLockTimer;

/// <summary>
/// 상태 기계(<see cref="LockStateMachine"/>), 입력 후크, OSD를 연결합니다.
/// 후크는 카운트다운이 시작될 때 설치되고, 대기 상태로 돌아가면 제거됩니다.
/// </summary>
internal sealed class LockController : IDisposable
{
    private readonly Control _ui;
    private readonly LockStateMachine _machine = new();
    private readonly InputHook _hook;
    private readonly OverlayManager _overlay = new();
    private readonly System.Windows.Forms.Timer _timer = new() { Interval = 250 };
    private AppSettings _settings = new();
    private bool _disposed;

    public LockController(Control ui, AppSettings settings)
    {
        _ui = ui;
        _hook = new InputHook(ui);
        _hook.HotkeyPressed += OnHotkey;
        _hook.InputDetected += OnInputDetected;
        _hook.Faulted += OnFaulted;
        _machine.StateChanged += OnStateChanged;
        _timer.Tick += (_, _) => OnTick();

        ApplySettings(settings);
        _timer.Start();
    }

    public event EventHandler? StatusChanged;
    public event Action<string>? Notice;

    public LockState State => _machine.State;
    public string StatusText { get; private set; } = "";

    private static long Now => Environment.TickCount64;

    public void ApplySettings(AppSettings settings)
    {
        _settings = settings.Clone();
        ForceRelease();
        _machine.Configure(
            TimeSpan.FromMilliseconds(_settings.InactivityTimeoutMs),
            _settings.CountdownSeconds,
            _settings.Enabled,
            Now);
        UpdateStatus(force: true);
    }

    /// <summary>입력 차단·후크·OSD를 모두 해제하고 비활성 타이머를 초기화합니다.</summary>
    public void ForceRelease()
    {
        _machine.Reset(Now);
        _hook.Stop();
        _overlay.HideOverlay();
        UpdateStatus(force: true);
    }

    private void OnTick()
    {
        long now = Now;
        _machine.Tick(now, NativeMethods.GetSystemIdleMs());

        switch (_machine.State)
        {
            case LockState.Countdown:
                _overlay.SetSeconds(_machine.RemainingCountdownSeconds(now));
                _overlay.EnsureTopMost();
                break;
            case LockState.Locked:
                _overlay.EnsureTopMost();
                break;
        }

        UpdateStatus(force: false);
    }

    private void OnStateChanged(LockState previous, LockState next)
    {
        switch (next)
        {
            case LockState.Countdown:
            {
                if (!_settings.TryGetCancelHotkey(out var cancel) || !_settings.TryGetUnlockHotkey(out _))
                {
                    Abort("단축키 설정이 올바르지 않아 잠금을 취소했습니다.");
                    return;
                }

                _hook.Hotkey = cancel;
                _hook.Blocking = false;
                _hook.ReportAnyInput = _settings.CancelOnInput;
                _hook.IncludeMouse = true;
                if (!_hook.Start())
                {
                    Abort("입력 후크를 설치하지 못해 잠금을 취소했습니다.");
                    return;
                }

                _overlay.ShowCountdown(_machine.RemainingCountdownSeconds(Now), cancel.ToString());
                break;
            }

            case LockState.Locked:
            {
                if (!_settings.TryGetUnlockHotkey(out var unlock) || !_hook.IsRunning)
                {
                    Abort("입력 후크 상태가 올바르지 않아 잠금을 취소했습니다.");
                    return;
                }

                _hook.Hotkey = unlock;
                _hook.ReportAnyInput = false;
                _hook.Blocking = true;
                _overlay.ShowLocked(unlock.ToString());
                break;
            }

            default:
                _hook.Stop();
                _overlay.HideOverlay();
                break;
        }

        UpdateStatus(force: true);
    }

    private void OnHotkey()
    {
        long now = Now;
        if (State == LockState.Countdown) _machine.CancelCountdown(now);
        else if (State == LockState.Locked) _machine.Unlock(now);
    }

    private void OnInputDetected()
    {
        if (State == LockState.Countdown && _settings.CancelOnInput)
            _machine.CancelCountdown(Now);
    }

    private void OnFaulted()
    {
        ForceRelease();
        Notice?.Invoke("입력 후크에서 오류가 발생해 잠금을 해제했습니다.");
    }

    private void Abort(string message)
    {
        // 상태 변경 처리 중 다시 상태를 바꾸지 않도록 UI 큐로 미룹니다.
        if (_ui.IsHandleCreated && !_ui.IsDisposed)
        {
            _ui.BeginInvoke(new Action(() =>
            {
                ForceRelease();
                Notice?.Invoke(message);
            }));
        }
        else
        {
            ForceRelease();
        }
    }

    private void UpdateStatus(bool force)
    {
        string text = BuildStatus();
        if (!force && text == StatusText) return;
        StatusText = text;
        StatusChanged?.Invoke(this, EventArgs.Empty);
    }

    private string BuildStatus()
    {
        switch (_machine.State)
        {
            case LockState.Disabled:
                return "잠금 기능이 꺼져 있습니다";
            case LockState.Idle:
                return $"대기 중 · 카운트다운까지 {FormatDuration(_machine.MillisUntilCountdown)}";
            case LockState.Countdown:
                return $"카운트다운 중 · {_machine.RemainingCountdownSeconds(Now)}초 후 입력 잠금";
            default:
                return _settings.TryGetUnlockHotkey(out var unlock)
                    ? $"입력 잠김 · {unlock} 로 해제"
                    : "입력 잠김";
        }
    }

    internal static string FormatDuration(long milliseconds)
    {
        long totalSeconds = (Math.Max(0, milliseconds) + 999) / 1000;
        long h = totalSeconds / 3600;
        long m = totalSeconds % 3600 / 60;
        long s = totalSeconds % 60;
        return h > 0 ? $"{h}:{m:00}:{s:00}" : $"{m:00}:{s:00}";
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _timer.Stop();
        _timer.Dispose();
        _hook.Dispose();
        _overlay.Dispose();
    }
}
