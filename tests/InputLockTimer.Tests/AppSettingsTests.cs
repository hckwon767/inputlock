using Xunit;

namespace InputLockTimer.Tests;

public class AppSettingsTests
{
    [Fact]
    public void Defaults_MatchSpecification()
    {
        var s = new AppSettings();
        Assert.Equal(0, s.InactivityHours);
        Assert.Equal(5, s.InactivityMinutes);
        Assert.Equal(0, s.InactivitySeconds);
        Assert.Equal(300_000, s.InactivityTimeoutMs);
        Assert.Equal(5, s.CountdownSeconds);
        Assert.Equal("Ctrl+Alt+K", s.UnlockHotkey);
        Assert.True(s.CancelHotkeySameAsUnlock);
        Assert.True(s.CancelOnInput);
        Assert.False(s.Enabled);
        Assert.Null(s.Validate());
    }

    [Fact]
    public void Validate_RejectsZeroInactivityTime()
    {
        var s = new AppSettings { InactivityHours = 0, InactivityMinutes = 0, InactivitySeconds = 0 };
        Assert.NotNull(s.Validate());
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(1, 0, 0)]
    [InlineData(0, 59, 59)]
    public void Validate_AcceptsPositiveTimes(int h, int m, int s)
    {
        var settings = new AppSettings { InactivityHours = h, InactivityMinutes = m, InactivitySeconds = s };
        Assert.Null(settings.Validate());
    }

    [Fact]
    public void Validate_RejectsMissingOrInvalidUnlockHotkey()
    {
        Assert.NotNull(new AppSettings { UnlockHotkey = "" }.Validate());
        Assert.NotNull(new AppSettings { UnlockHotkey = "Ctrl+Shift" }.Validate());
        Assert.NotNull(new AppSettings { UnlockHotkey = "Ctrl+Alt+Delete" }.Validate());
    }

    [Fact]
    public void Validate_ChecksSeparateCancelHotkeyOnlyWhenUsed()
    {
        Assert.Null(new AppSettings { CancelHotkeySameAsUnlock = true, CancelHotkey = "garbage" }.Validate());
        Assert.NotNull(new AppSettings { CancelHotkeySameAsUnlock = false, CancelHotkey = "garbage" }.Validate());
    }

    [Fact]
    public void Json_RoundTripKeepsValues()
    {
        var original = new AppSettings
        {
            Enabled = true,
            InactivityHours = 1,
            InactivityMinutes = 2,
            InactivitySeconds = 3,
            CountdownSeconds = 7,
            UnlockHotkey = "Shift+F12",
            CancelHotkeySameAsUnlock = false,
            CancelHotkey = "Ctrl+Alt+C",
            CancelOnInput = false,
        };

        var loaded = SettingsStore.Deserialize(SettingsStore.Serialize(original));

        Assert.NotNull(loaded);
        Assert.True(loaded!.Enabled);
        Assert.Equal(3723_000, loaded.InactivityTimeoutMs);
        Assert.Equal(7, loaded.CountdownSeconds);
        Assert.Equal("Shift+F12", loaded.UnlockHotkey);
        Assert.False(loaded.CancelHotkeySameAsUnlock);
        Assert.Equal("Ctrl+Alt+C", loaded.CancelHotkey);
        Assert.False(loaded.CancelOnInput);
    }

    [Fact]
    public void Deserialize_ReturnsNullForBrokenJson()
    {
        Assert.Null(SettingsStore.Deserialize("{ not json"));
    }

    [Fact]
    public void Normalize_FixesOutOfRangeValuesAndDisablesWhenHotkeyIsInvalid()
    {
        var loaded = SettingsStore.Deserialize(
            """{ "Enabled": true, "InactivityHours": 0, "InactivityMinutes": 0, "InactivitySeconds": 0, "CountdownSeconds": 999, "UnlockHotkey": "nonsense" }""");

        Assert.NotNull(loaded);
        Assert.False(loaded!.Enabled);                       // 해제 단축키가 잘못됐으므로 기능을 끈다
        Assert.Equal(AppSettings.DefaultHotkey, loaded.UnlockHotkey);
        Assert.Equal(300_000, loaded.InactivityTimeoutMs);   // 0초는 기본값으로 보정
        Assert.Equal(AppSettings.MaxCountdownSeconds, loaded.CountdownSeconds);
        Assert.Null(loaded.Validate());
    }
}
