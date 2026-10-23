namespace InputLockTimer;

internal enum LockState
{
    /// <summary>기능이 꺼져 있음.</summary>
    Disabled,
    /// <summary>입력을 감시하며 비활성 시간을 재는 중.</summary>
    Idle,
    /// <summary>잠금 직전 OSD 카운트다운 중.</summary>
    Countdown,
    /// <summary>입력이 차단된 상태.</summary>
    Locked,
}

/// <summary>
/// UI·후크와 분리된 순수 상태 기계. 시간은 호출자가 ms 단위로 넘겨 주므로 테스트하기 쉽습니다.
/// </summary>
internal sealed class LockStateMachine
{
    private long _timeoutMs = 300_000;
    private int _countdownSeconds = 5;
    private bool _enabled;
    private long _resetAtMs;
    private long _countdownEndMs;

    public LockState State { get; private set; } = LockState.Disabled;

    /// <summary>마지막 Tick 기준 유효 비활성 시간(ms). 초기화(해제·취소) 이후부터 다시 셉니다.</summary>
    public long LastIdleMs { get; private set; }

    public long MillisUntilCountdown => Math.Max(0, _timeoutMs - LastIdleMs);

    public event Action<LockState, LockState>? StateChanged;

    public void Configure(TimeSpan timeout, int countdownSeconds, bool enabled, long nowMs)
    {
        if (timeout <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(timeout), "비활성 시간은 0보다 커야 합니다.");
        if (countdownSeconds < 1)
            throw new ArgumentOutOfRangeException(nameof(countdownSeconds));

        _timeoutMs = (long)timeout.TotalMilliseconds;
        _countdownSeconds = countdownSeconds;
        _enabled = enabled;
        Reset(nowMs);
    }

    /// <summary>어떤 상태에서든 대기(또는 꺼짐) 상태로 되돌리고 비활성 타이머를 초기화합니다.</summary>
    public void Reset(long nowMs)
    {
        _resetAtMs = nowMs;
        LastIdleMs = 0;
        SetState(_enabled ? LockState.Idle : LockState.Disabled);
    }

    /// <param name="systemIdleMs">운영체제가 보고하는 마지막 입력 이후 경과 시간.</param>
    public void Tick(long nowMs, long systemIdleMs)
    {
        switch (State)
        {
            case LockState.Idle:
                // 해제·취소 시점 이전의 입력은 무시하기 위해 reset 이후 경과 시간과 비교해 작은 값을 사용합니다.
                LastIdleMs = Math.Max(0, Math.Min(systemIdleMs, nowMs - _resetAtMs));
                if (LastIdleMs >= _timeoutMs)
                {
                    _countdownEndMs = nowMs + _countdownSeconds * 1000L;
                    SetState(LockState.Countdown);
                }
                break;

            case LockState.Countdown:
                if (nowMs >= _countdownEndMs)
                    SetState(LockState.Locked);
                break;
        }
    }

    public void CancelCountdown(long nowMs)
    {
        if (State == LockState.Countdown) Reset(nowMs);
    }

    public void Unlock(long nowMs)
    {
        if (State == LockState.Locked) Reset(nowMs);
    }

    public int RemainingCountdownSeconds(long nowMs)
    {
        if (State != LockState.Countdown) return 0;
        long remainingMs = _countdownEndMs - nowMs;
        return (int)Math.Clamp((remainingMs + 999) / 1000, 0, _countdownSeconds);
    }

    private void SetState(LockState next)
    {
        if (State == next) return;
        var previous = State;
        State = next;
        StateChanged?.Invoke(previous, next);
    }
}
