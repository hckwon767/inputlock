using Xunit;

namespace InputLockTimer.Tests;

public class LockStateMachineTests
{
    private static LockStateMachine Create(bool enabled = true)
    {
        var machine = new LockStateMachine();
        machine.Configure(TimeSpan.FromSeconds(10), 5, enabled, nowMs: 0);
        return machine;
    }

    [Fact]
    public void Configure_RejectsZeroTimeout()
    {
        var machine = new LockStateMachine();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            machine.Configure(TimeSpan.Zero, 5, true, 0));
    }

    [Fact]
    public void Configure_SetsIdleWhenEnabledAndDisabledOtherwise()
    {
        Assert.Equal(LockState.Idle, Create(true).State);
        Assert.Equal(LockState.Disabled, Create(false).State);
    }

    [Fact]
    public void Tick_StaysIdleBeforeTimeout()
    {
        var machine = Create();
        machine.Tick(9_999, systemIdleMs: 9_999);
        Assert.Equal(LockState.Idle, machine.State);
    }

    [Fact]
    public void Tick_StartsCountdownAtTimeout_ThenLocksAfterCountdown()
    {
        var machine = Create();
        var changes = new List<(LockState, LockState)>();
        machine.StateChanged += (oldState, newState) => changes.Add((oldState, newState));

        machine.Tick(10_000, 10_000);
        Assert.Equal(LockState.Countdown, machine.State);
        Assert.Equal(5, machine.RemainingCountdownSeconds(10_000));
        Assert.Equal(3, machine.RemainingCountdownSeconds(12_001));

        machine.Tick(14_999, 14_999);
        Assert.Equal(LockState.Countdown, machine.State);

        machine.Tick(15_000, 15_000);
        Assert.Equal(LockState.Locked, machine.State);

        Assert.Equal(
            new[] { (LockState.Idle, LockState.Countdown), (LockState.Countdown, LockState.Locked) },
            changes);
    }

    [Fact]
    public void Tick_InputResetsIdleTime()
    {
        var machine = Create();
        // 시스템이 최근 입력이 있었다고 보고하면 경과 시간이 커져도 카운트다운이 시작되지 않는다.
        machine.Tick(60_000, systemIdleMs: 1_000);
        Assert.Equal(LockState.Idle, machine.State);
    }

    [Fact]
    public void CancelCountdown_ReturnsToIdleAndRestartsTimer()
    {
        var machine = Create();
        machine.Tick(10_000, 10_000);
        Assert.Equal(LockState.Countdown, machine.State);

        machine.CancelCountdown(12_000);
        Assert.Equal(LockState.Idle, machine.State);

        // 취소 이전의 입력 공백은 무시: 취소 직후 시스템 비활성 시간이 길어도 즉시 다시 시작하지 않는다.
        machine.Tick(12_100, systemIdleMs: 12_100);
        Assert.Equal(LockState.Idle, machine.State);

        // 취소로부터 10초가 지나야 다시 카운트다운.
        machine.Tick(22_000, 22_000);
        Assert.Equal(LockState.Countdown, machine.State);
    }

    [Fact]
    public void Unlock_ReturnsToIdleAndResetsTimer()
    {
        var machine = Create();
        machine.Tick(10_000, 10_000);
        machine.Tick(15_000, 15_000);
        Assert.Equal(LockState.Locked, machine.State);

        machine.Unlock(20_000);
        Assert.Equal(LockState.Idle, machine.State);

        machine.Tick(20_500, systemIdleMs: 20_500);
        Assert.Equal(LockState.Idle, machine.State);
    }

    [Fact]
    public void CancelCountdown_AndUnlock_AreIgnoredInOtherStates()
    {
        var machine = Create();
        machine.CancelCountdown(1);
        machine.Unlock(1);
        Assert.Equal(LockState.Idle, machine.State);
    }

    [Fact]
    public void Disabled_NeverStartsCountdown()
    {
        var machine = Create(enabled: false);
        machine.Tick(1_000_000, 1_000_000);
        Assert.Equal(LockState.Disabled, machine.State);
    }

    [Fact]
    public void Reset_FromLocked_ReleasesLock()
    {
        var machine = Create();
        machine.Tick(10_000, 10_000);
        machine.Tick(15_000, 15_000);
        machine.Reset(16_000);
        Assert.Equal(LockState.Idle, machine.State);
    }

    [Theory]
    [InlineData(0L, "00:00")]
    [InlineData(1L, "00:01")]
    [InlineData(59_000L, "00:59")]
    [InlineData(61_000L, "01:01")]
    [InlineData(3_661_000L, "1:01:01")]
    public void FormatDuration_RoundsUpToSeconds(long milliseconds, string expected)
    {
        Assert.Equal(expected, LockController.FormatDuration(milliseconds));
    }
}
