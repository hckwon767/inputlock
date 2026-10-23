namespace InputLockTimer;

[Flags]
internal enum HotkeyModifiers
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
}

/// <summary>Ctrl/Alt/Shift 조합 + 일반 키 1개로 이루어진 단축키.</summary>
internal readonly record struct HotkeyCombo(HotkeyModifiers Modifiers, Keys Key)
{
    private static readonly Dictionary<Keys, string> KeyNames = new()
    {
        [Keys.Return] = "Enter",
        [Keys.Escape] = "Esc",
        [Keys.Prior] = "PageUp",
        [Keys.Next] = "PageDown",
        [Keys.Back] = "Backspace",
        [Keys.Capital] = "CapsLock",
        [Keys.HangulMode] = "Hangul",
        [Keys.HanjaMode] = "Hanja",
    };

    private static readonly Dictionary<string, Keys> NameToKey = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Enter"] = Keys.Return,
        ["Return"] = Keys.Return,
        ["Esc"] = Keys.Escape,
        ["Escape"] = Keys.Escape,
        ["PageUp"] = Keys.Prior,
        ["PgUp"] = Keys.Prior,
        ["PageDown"] = Keys.Next,
        ["PgDn"] = Keys.Next,
        ["Backspace"] = Keys.Back,
        ["CapsLock"] = Keys.Capital,
        ["Hangul"] = Keys.HangulMode,
        ["Hanja"] = Keys.HanjaMode,
    };

    public static bool IsModifierKey(Keys key) =>
        key is Keys.ControlKey or Keys.ShiftKey or Keys.Menu
            or Keys.LControlKey or Keys.RControlKey
            or Keys.LShiftKey or Keys.RShiftKey
            or Keys.LMenu or Keys.RMenu
            or Keys.LWin or Keys.RWin;

    /// <summary>사용할 수 없는 조합이면 사유를, 사용 가능하면 null을 반환합니다.</summary>
    public string? Validate()
    {
        int vk = (int)Key;
        if (vk <= 0 || vk > 254 || IsModifierKey(Key))
            return "Ctrl, Alt, Shift만으로는 단축키를 만들 수 없습니다. 일반 키를 함께 지정하세요.";

        if (Key is Keys.LButton or Keys.RButton or Keys.MButton or Keys.XButton1 or Keys.XButton2
            or Keys.Cancel or Keys.ProcessKey or Keys.Packet)
            return "이 키는 단축키로 사용할 수 없습니다.";

        if (Key == Keys.Delete
            && Modifiers.HasFlag(HotkeyModifiers.Ctrl)
            && Modifiers.HasFlag(HotkeyModifiers.Alt))
            return "Ctrl+Alt+Delete는 Windows 보안 화면 단축키이므로 사용할 수 없습니다.";

        return null;
    }

    /// <summary>사용은 가능하지만 충돌 가능성이 있는 경우의 안내 문구 목록.</summary>
    public IReadOnlyList<string> GetWarnings()
    {
        var list = new List<string>();
        bool printable = IsPrintableKey(Key);
        bool ctrl = Modifiers.HasFlag(HotkeyModifiers.Ctrl);
        bool alt = Modifiers.HasFlag(HotkeyModifiers.Alt);

        if (Modifiers == HotkeyModifiers.None)
        {
            list.Add(printable
                ? "수식키 없이 문자·숫자 키를 쓰면 평소 타이핑 중 의도치 않게 눌릴 수 있습니다."
                : "수식키 없는 단일 키는 다른 프로그램의 단축키와 겹칠 수 있습니다.");
        }
        else if (Modifiers == HotkeyModifiers.Shift && printable)
        {
            list.Add("Shift + 문자·숫자 키는 대문자 입력과 겹쳐 타이핑 중 눌릴 수 있습니다.");
        }
        else if (Modifiers == HotkeyModifiers.Ctrl || Modifiers == HotkeyModifiers.Alt)
        {
            list.Add("Ctrl 또는 Alt 하나만 쓰는 조합은 복사·저장 등 많은 프로그램의 단축키와 겹칠 수 있습니다.");
        }

        bool altSystem = alt && Key is Keys.F4 or Keys.Tab or Keys.Escape or Keys.Space;
        bool ctrlSystem = ctrl && Key == Keys.Escape;
        if (altSystem || ctrlSystem)
            list.Add("Windows 시스템 단축키와 충돌합니다(Alt+F4, Alt+Tab, Ctrl+Esc 등).");

        if (Key is Keys.HangulMode or Keys.HanjaMode)
            list.Add("한/영·한자 키는 입력기(IME)가 먼저 처리해 기대대로 동작하지 않을 수 있습니다.");

        return list;
    }

    private static bool IsPrintableKey(Keys key)
    {
        int vk = (int)key;
        return key is >= Keys.A and <= Keys.Z
            || key is >= Keys.D0 and <= Keys.D9
            || key is >= Keys.NumPad0 and <= Keys.NumPad9
            || key == Keys.Space
            || vk is >= 0xBA and <= 0xC0
            || vk is >= 0xDB and <= 0xDF
            || vk == 0xE2;
    }

    public static string KeyName(Keys key)
    {
        if (key >= Keys.D0 && key <= Keys.D9) return ((int)key - (int)Keys.D0).ToString();
        return KeyNames.TryGetValue(key, out var name) ? name : key.ToString();
    }

    public override string ToString()
    {
        var parts = new List<string>(4);
        if (Modifiers.HasFlag(HotkeyModifiers.Ctrl)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        parts.Add(KeyName(Key));
        return string.Join("+", parts);
    }

    public static bool TryParse(string? text, out HotkeyCombo combo)
    {
        combo = default;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var parts = text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return false;

        var mods = HotkeyModifiers.None;
        for (int i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    mods |= HotkeyModifiers.Ctrl;
                    break;
                case "alt":
                    mods |= HotkeyModifiers.Alt;
                    break;
                case "shift":
                    mods |= HotkeyModifiers.Shift;
                    break;
                default:
                    return false;
            }
        }

        if (!TryParseKey(parts[^1], out var key)) return false;
        combo = new HotkeyCombo(mods, key);
        return true;
    }

    private static bool TryParseKey(string name, out Keys key)
    {
        key = Keys.None;

        if (name.Length == 1)
        {
            char c = char.ToUpperInvariant(name[0]);
            if (c is >= 'A' and <= 'Z') { key = Keys.A + (c - 'A'); return true; }
            if (c is >= '0' and <= '9') { key = Keys.D0 + (c - '0'); return true; }
            return false;
        }

        if (NameToKey.TryGetValue(name, out var alias)) { key = alias; return true; }

        // "Control, Shift" 같은 플래그 문자열이나 순수 숫자 문자열은 거부
        if (name.Contains(',') || name.All(char.IsDigit)) return false;

        if (Enum.TryParse<Keys>(name, true, out var parsed)
            && Enum.IsDefined(parsed)
            && parsed != Keys.None
            && parsed == (parsed & Keys.KeyCode))
        {
            key = parsed;
            return true;
        }

        return false;
    }
}
