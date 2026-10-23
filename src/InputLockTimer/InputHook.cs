using System.Runtime.InteropServices;
using static InputLockTimer.NativeMethods;

namespace InputLockTimer;

/// <summary>
/// 저수준 키보드·마우스 후크(WH_KEYBOARD_LL / WH_MOUSE_LL).
/// 필요한 동안(카운트다운·잠금·단축키 시험)에만 설치하며, 입력 내용은 저장하거나 외부로 보내지 않습니다.
/// 후크 콜백은 UI 스레드에서 실행되므로 콜백 안에서는 가벼운 처리만 하고 이벤트는 UI 큐로 넘깁니다.
/// </summary>
internal sealed class InputHook : IDisposable
{
    private readonly Control _sync;
    private readonly HookProc _keyboardProc;
    private readonly HookProc _mouseProc;
    private IntPtr _keyboardHook = IntPtr.Zero;
    private IntPtr _mouseHook = IntPtr.Zero;

    // 시스템에 전달된(차단되지 않은) 키 다운 상태. 잠금 중 키 업을 차단하면 키가 눌린 채로 남을 수 있어 추적합니다.
    private readonly HashSet<int> _keysDown = new();
    // 후크가 본 수식키의 물리 상태.
    private readonly HashSet<int> _modsDown = new();
    // 단축키로 소비한 키: 대응하는 키 업도 함께 삼킵니다.
    private readonly HashSet<int> _swallowUp = new();
    private readonly Dictionary<int, (ushort Scan, bool Extended)> _stuckKeys = new();
    private readonly HashSet<int> _mouseDown = new();
    private readonly HashSet<int> _stuckMouse = new();
    private bool _inputReported;
    private Point _lastMousePos;

    public InputHook(Control sync)
    {
        _sync = sync;
        _keyboardProc = KeyboardCallback;
        _mouseProc = MouseCallback;
    }

    /// <summary>true이면 일반 입력을 모두 차단합니다(잠금).</summary>
    public bool Blocking { get; set; }

    /// <summary>true이면 단축키가 아닌 입력이 처음 발생했을 때 <see cref="InputDetected"/>를 한 번 발생시킵니다.</summary>
    public bool ReportAnyInput { get; set; }

    public bool IncludeMouse { get; set; } = true;

    /// <summary>감시할 단축키. 일치하는 키 입력은 소비(차단)되고 <see cref="HotkeyPressed"/>가 발생합니다.</summary>
    public HotkeyCombo? Hotkey { get; set; }

    public bool IsRunning => _keyboardHook != IntPtr.Zero;

    public event Action? HotkeyPressed;
    public event Action? InputDetected;
    public event Action? Faulted;

    public bool Start()
    {
        if (IsRunning) return true;

        ResetTracking();
        SeedFromAsyncState();

        var module = GetModuleHandle(null);
        _keyboardHook = SetWindowsHookEx(WH_KEYBOARD_LL, _keyboardProc, module, 0);
        if (_keyboardHook == IntPtr.Zero) return false;

        if (IncludeMouse)
        {
            _mouseHook = SetWindowsHookEx(WH_MOUSE_LL, _mouseProc, module, 0);
            if (_mouseHook == IntPtr.Zero)
            {
                UnhookWindowsHookEx(_keyboardHook);
                _keyboardHook = IntPtr.Zero;
                return false;
            }
        }

        _lastMousePos = Cursor.Position;
        return true;
    }

    public void Stop()
    {
        if (_keyboardHook == IntPtr.Zero && _mouseHook == IntPtr.Zero) return;

        if (_keyboardHook != IntPtr.Zero) UnhookWindowsHookEx(_keyboardHook);
        if (_mouseHook != IntPtr.Zero) UnhookWindowsHookEx(_mouseHook);
        _keyboardHook = IntPtr.Zero;
        _mouseHook = IntPtr.Zero;

        // 후크를 먼저 제거한 뒤, 잠금 중 차단된 키 업/버튼 업을 시스템에 보내 "눌린 채로 남은" 입력을 풀어 줍니다.
        ReleaseStuckInputs();
        ResetTracking();
        Blocking = false;
    }

    public void Dispose() => Stop();

    // ---------------------------------------------------------------- 키보드

    private IntPtr KeyboardCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION)
        {
            try
            {
                var data = Marshal.PtrToStructure<KBDLLHOOKSTRUCT>(lParam);
                int msg = wParam.ToInt32();
                bool down = msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN;
                bool up = msg == WM_KEYUP || msg == WM_SYSKEYUP;
                if ((down || up) && HandleKey(data, down))
                    return (IntPtr)1;
            }
            catch
            {
                OnFault();
            }
        }

        return CallNextHookEx(_keyboardHook, nCode, wParam, lParam);
    }

    private bool HandleKey(KBDLLHOOKSTRUCT data, bool down)
    {
        int vk = (int)data.vkCode;
        bool isModifier = IsModifierVk(vk);

        if (isModifier)
        {
            if (down) _modsDown.Add(vk); else _modsDown.Remove(vk);
        }

        if (down && !isModifier)
        {
            if (_swallowUp.Contains(vk)) return true; // 자동 반복
            if (MatchesHotkey(vk))
            {
                _swallowUp.Add(vk);
                Raise(HotkeyPressed);
                return true;
            }
        }
        else if (!down && _swallowUp.Remove(vk))
        {
            return true;
        }

        if (down) ReportInput();

        if (Blocking)
        {
            if (!down && _keysDown.Contains(vk))
                _stuckKeys[vk] = ((ushort)data.scanCode, (data.flags & LLKHF_EXTENDED) != 0);
            return true;
        }

        if (down) _keysDown.Add(vk); else _keysDown.Remove(vk);
        return false;
    }

    private bool MatchesHotkey(int vk)
    {
        if (Hotkey is not { } hotkey) return false;
        if ((int)hotkey.Key != vk) return false;
        if (_modsDown.Contains(0x5B) || _modsDown.Contains(0x5C)) return false; // Win 키가 눌려 있으면 제외

        // Ctrl+Alt+Del 같은 보안 화면을 거치면 후크가 수식키 해제를 못 볼 수 있으므로
        // 후크가 추적한 값과 GetAsyncKeyState 값 중 하나라도 정확히 일치하면 인정합니다.
        return hotkey.Modifiers == TrackedModifiers() || hotkey.Modifiers == AsyncModifiers();
    }

    private HotkeyModifiers TrackedModifiers()
    {
        var mods = HotkeyModifiers.None;
        if (_modsDown.Contains(0xA2) || _modsDown.Contains(0xA3)) mods |= HotkeyModifiers.Ctrl;
        if (_modsDown.Contains(0xA4) || _modsDown.Contains(0xA5)) mods |= HotkeyModifiers.Alt;
        if (_modsDown.Contains(0xA0) || _modsDown.Contains(0xA1)) mods |= HotkeyModifiers.Shift;
        return mods;
    }

    private static HotkeyModifiers AsyncModifiers()
    {
        var mods = HotkeyModifiers.None;
        if ((GetAsyncKeyState(0x11) & 0x8000) != 0) mods |= HotkeyModifiers.Ctrl;
        if ((GetAsyncKeyState(0x12) & 0x8000) != 0) mods |= HotkeyModifiers.Alt;
        if ((GetAsyncKeyState(0x10) & 0x8000) != 0) mods |= HotkeyModifiers.Shift;
        return mods;
    }

    private static bool IsModifierVk(int vk) =>
        vk is 0x10 or 0x11 or 0x12 or (>= 0xA0 and <= 0xA5) or 0x5B or 0x5C;

    // ---------------------------------------------------------------- 마우스

    private IntPtr MouseCallback(int nCode, IntPtr wParam, IntPtr lParam)
    {
        if (nCode == HC_ACTION)
        {
            try
            {
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                if (HandleMouse(wParam.ToInt32(), data))
                    return (IntPtr)1;
            }
            catch
            {
                OnFault();
            }
        }

        return CallNextHookEx(_mouseHook, nCode, wParam, lParam);
    }

    private bool HandleMouse(int msg, MSLLHOOKSTRUCT data)
    {
        if (msg == WM_MOUSEMOVE)
        {
            var pos = new Point(data.pt.X, data.pt.Y);
            if (pos != _lastMousePos)
            {
                _lastMousePos = pos;
                ReportInput();
            }
            return Blocking;
        }

        int button = 0;
        bool? isDown = null;
        switch (msg)
        {
            case WM_LBUTTONDOWN: button = 1; isDown = true; break;
            case WM_LBUTTONUP: button = 1; isDown = false; break;
            case WM_RBUTTONDOWN: button = 2; isDown = true; break;
            case WM_RBUTTONUP: button = 2; isDown = false; break;
            case WM_MBUTTONDOWN: button = 4; isDown = true; break;
            case WM_MBUTTONUP: button = 4; isDown = false; break;
            case WM_XBUTTONDOWN: button = XButtonId(data.mouseData); isDown = true; break;
            case WM_XBUTTONUP: button = XButtonId(data.mouseData); isDown = false; break;
        }

        ReportInput(); // 클릭·휠 등

        if (isDown == true)
        {
            if (Blocking) return true;
            _mouseDown.Add(button);
            return false;
        }

        if (isDown == false)
        {
            if (Blocking)
            {
                if (_mouseDown.Contains(button)) _stuckMouse.Add(button);
                return true;
            }
            _mouseDown.Remove(button);
            return false;
        }

        return Blocking; // 휠 등
    }

    private static int XButtonId(uint mouseData) => (int)(mouseData >> 16) == 1 ? 5 : 6;

    // ---------------------------------------------------------------- 공통

    private void ReportInput()
    {
        if (!ReportAnyInput || _inputReported) return;
        _inputReported = true;
        Raise(InputDetected);
    }

    private void OnFault()
    {
        // 후크에서 오류가 나면 입력 차단을 즉시 풀고(fail-open) UI에 알립니다.
        Blocking = false;
        Raise(Faulted);
    }

    private void Raise(Action? handler)
    {
        if (handler == null) return;
        try
        {
            if (_sync.IsHandleCreated && !_sync.IsDisposed)
                _sync.BeginInvoke(handler);
        }
        catch (InvalidOperationException) { } // ObjectDisposedException 포함
    }

    private void ResetTracking()
    {
        _keysDown.Clear();
        _modsDown.Clear();
        _swallowUp.Clear();
        _stuckKeys.Clear();
        _mouseDown.Clear();
        _stuckMouse.Clear();
        _inputReported = false;
    }

    private void SeedFromAsyncState()
    {
        // 후크 설치 시점에 이미 눌려 있던 키를 기록합니다. 수식키는 좌/우 구분 코드만 사용합니다.
        for (int vk = 0x08; vk <= 0xFE; vk++)
        {
            if (vk is 0x10 or 0x11 or 0x12) continue;
            if ((GetAsyncKeyState(vk) & 0x8000) == 0) continue;
            _keysDown.Add(vk);
            if (IsModifierVk(vk)) _modsDown.Add(vk);
        }

        foreach (int button in new[] { 1, 2, 4, 5, 6 })
        {
            if ((GetAsyncKeyState(button) & 0x8000) != 0) _mouseDown.Add(button);
        }
    }

    private void ReleaseStuckInputs()
    {
        if (_stuckKeys.Count == 0 && _stuckMouse.Count == 0) return;

        var inputs = new List<INPUT>();

        foreach (var (vk, info) in _stuckKeys)
        {
            inputs.Add(new INPUT
            {
                type = INPUT_KEYBOARD,
                u = new INPUTUNION
                {
                    ki = new KEYBDINPUT
                    {
                        wVk = (ushort)vk,
                        wScan = info.Scan,
                        dwFlags = KEYEVENTF_KEYUP | (info.Extended ? KEYEVENTF_EXTENDEDKEY : 0),
                    },
                },
            });
        }

        foreach (int button in _stuckMouse)
        {
            var mi = new MOUSEINPUT();
            switch (button)
            {
                case 1: mi.dwFlags = MOUSEEVENTF_LEFTUP; break;
                case 2: mi.dwFlags = MOUSEEVENTF_RIGHTUP; break;
                case 4: mi.dwFlags = MOUSEEVENTF_MIDDLEUP; break;
                case 5: mi.dwFlags = MOUSEEVENTF_XUP; mi.mouseData = XBUTTON1; break;
                case 6: mi.dwFlags = MOUSEEVENTF_XUP; mi.mouseData = XBUTTON2; break;
                default: continue;
            }
            inputs.Add(new INPUT { type = INPUT_MOUSE, u = new INPUTUNION { mi = mi } });
        }

        if (inputs.Count > 0)
            SendInput((uint)inputs.Count, inputs.ToArray(), Marshal.SizeOf<INPUT>());
    }
}
