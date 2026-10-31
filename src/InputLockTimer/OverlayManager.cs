namespace InputLockTimer;

/// <summary>
/// 연결된 모든 모니터에 OSD(<see cref="OverlayForm"/>)를 하나씩 표시합니다.
/// 모니터 구성이 바뀌면(연결·해제·해상도·배치 변경) 표시 중에도 자동으로 다시 구성합니다.
/// </summary>
internal sealed class OverlayManager : IDisposable
{
    private enum Mode { None, Countdown, Locked }

    private readonly List<OverlayForm> _forms = new();
    private Mode _mode = Mode.None;
    private int _seconds;
    private string _hotkey = "";
    private string _layout = "";
    private bool _disposed;

    public void ShowCountdown(int seconds, string cancelHotkey)
    {
        _mode = Mode.Countdown;
        _seconds = seconds;
        _hotkey = cancelHotkey;
        Rebuild();
    }

    public void ShowLocked(string unlockHotkey)
    {
        _mode = Mode.Locked;
        _hotkey = unlockHotkey;
        Rebuild();
    }

    public void SetSeconds(int seconds)
    {
        _seconds = seconds;
        foreach (var form in _forms) form.SetSeconds(seconds);
    }

    public void HideOverlay()
    {
        _mode = Mode.None;
        foreach (var form in _forms) form.HideOverlay();
    }

    /// <summary>모니터 구성 변경을 반영하고, 모든 OSD의 최상위 순서를 다시 지정합니다.</summary>
    public void EnsureTopMost()
    {
        if (_disposed || _mode == Mode.None) return;

        if (_layout != CurrentLayout()) Rebuild();
        else foreach (var form in _forms) form.EnsureTopMost();
    }

    private static string CurrentLayout() =>
        string.Join("|", Screen.AllScreens.Select(s => s.Bounds.ToString()));

    private void Rebuild()
    {
        if (_disposed) return;

        DisposeForms();
        _layout = CurrentLayout();

        foreach (var screen in Screen.AllScreens)
        {
            var form = new OverlayForm(screen);
            _forms.Add(form);

            if (_mode == Mode.Countdown) form.ShowCountdown(_seconds, _hotkey);
            else if (_mode == Mode.Locked) form.ShowLocked(_hotkey);
        }
    }

    private void DisposeForms()
    {
        foreach (var form in _forms)
        {
            form.Close();
            form.Dispose();
        }
        _forms.Clear();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        DisposeForms();
    }
}
