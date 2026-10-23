using System.Text.Json.Serialization;

namespace InputLockTimer;

/// <summary>사용자 설정. %APPDATA%\InputLockTimer\settings.json 에 저장됩니다.</summary>
internal sealed class AppSettings
{
    public const string DefaultHotkey = "Ctrl+Alt+K";
    public const int MinCountdownSeconds = 1;
    public const int MaxCountdownSeconds = 30;

    /// <summary>잠금 기능 사용 여부. 첫 실행에서는 해제 단축키를 시험할 수 있도록 꺼진 상태로 시작합니다.</summary>
    public bool Enabled { get; set; }

    public int InactivityHours { get; set; }
    public int InactivityMinutes { get; set; } = 5;
    public int InactivitySeconds { get; set; }

    public int CountdownSeconds { get; set; } = 5;

    public string UnlockHotkey { get; set; } = DefaultHotkey;
    public bool CancelHotkeySameAsUnlock { get; set; } = true;
    public string CancelHotkey { get; set; } = DefaultHotkey;

    /// <summary>카운트다운 중 일반 입력이 감지되면 카운트다운을 취소할지 여부.</summary>
    public bool CancelOnInput { get; set; } = true;

    [JsonIgnore]
    public long InactivityTimeoutMs =>
        (((long)InactivityHours * 60 + InactivityMinutes) * 60 + InactivitySeconds) * 1000;

    public bool TryGetUnlockHotkey(out HotkeyCombo combo) =>
        HotkeyCombo.TryParse(UnlockHotkey, out combo);

    public bool TryGetCancelHotkey(out HotkeyCombo combo) =>
        CancelHotkeySameAsUnlock
            ? TryGetUnlockHotkey(out combo)
            : HotkeyCombo.TryParse(CancelHotkey, out combo);

    /// <summary>저장 가능한 설정이면 null, 아니면 사용자에게 보여 줄 오류 문구.</summary>
    public string? Validate()
    {
        if (InactivityHours is < 0 or > 99
            || InactivityMinutes is < 0 or > 59
            || InactivitySeconds is < 0 or > 59)
            return "비활성 시간 범위가 올바르지 않습니다(시 0~99, 분 0~59, 초 0~59).";

        if (InactivityTimeoutMs <= 0)
            return "비활성 시간은 0초로 설정할 수 없습니다.";

        if (CountdownSeconds is < MinCountdownSeconds or > MaxCountdownSeconds)
            return $"카운트다운은 {MinCountdownSeconds}~{MaxCountdownSeconds}초 사이여야 합니다.";

        if (!HotkeyCombo.TryParse(UnlockHotkey, out var unlock))
            return "해제 단축키가 설정되어 있지 않거나 올바르지 않습니다.";
        var unlockError = unlock.Validate();
        if (unlockError != null)
            return "해제 단축키: " + unlockError;

        if (!CancelHotkeySameAsUnlock)
        {
            if (!HotkeyCombo.TryParse(CancelHotkey, out var cancel))
                return "카운트다운 취소 단축키가 설정되어 있지 않거나 올바르지 않습니다.";
            var cancelError = cancel.Validate();
            if (cancelError != null)
                return "카운트다운 취소 단축키: " + cancelError;
        }

        return null;
    }

    /// <summary>파일에서 읽은 값을 안전한 범위로 보정합니다. 해제 단축키가 잘못되면 기본값으로 되돌리고 기능을 끕니다.</summary>
    public void Normalize()
    {
        InactivityHours = Math.Clamp(InactivityHours, 0, 99);
        InactivityMinutes = Math.Clamp(InactivityMinutes, 0, 59);
        InactivitySeconds = Math.Clamp(InactivitySeconds, 0, 59);
        if (InactivityTimeoutMs <= 0)
        {
            InactivityHours = 0;
            InactivityMinutes = 5;
            InactivitySeconds = 0;
        }

        CountdownSeconds = Math.Clamp(CountdownSeconds, MinCountdownSeconds, MaxCountdownSeconds);

        if (HotkeyCombo.TryParse(UnlockHotkey, out var unlock) && unlock.Validate() == null)
        {
            UnlockHotkey = unlock.ToString();
        }
        else
        {
            UnlockHotkey = DefaultHotkey;
            Enabled = false;
        }

        if (!CancelHotkeySameAsUnlock)
        {
            if (HotkeyCombo.TryParse(CancelHotkey, out var cancel) && cancel.Validate() == null)
            {
                CancelHotkey = cancel.ToString();
            }
            else
            {
                CancelHotkeySameAsUnlock = true;
                CancelHotkey = UnlockHotkey;
            }
        }
    }

    public AppSettings Clone() => (AppSettings)MemberwiseClone();
}
