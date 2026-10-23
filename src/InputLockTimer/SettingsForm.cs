namespace InputLockTimer;

/// <summary>클릭 후 키 조합을 누르면 단축키로 기록하는 입력 상자.</summary>
internal sealed class HotkeyBox : TextBox
{
    private HotkeyCombo? _value;

    public HotkeyBox()
    {
        ReadOnly = true;
        ShortcutsEnabled = false;
        ImeMode = ImeMode.Disable;
        BackColor = SystemColors.Window;
        Width = 220;
    }

    public event EventHandler? ValueChanged;

    public HotkeyCombo? Value
    {
        get => _value;
        set
        {
            _value = value;
            Text = value?.ToString() ?? "";
        }
    }

    protected override bool IsInputKey(Keys keyData) => true;

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if (!Focused) return base.ProcessCmdKey(ref msg, keyData);

        var key = keyData & Keys.KeyCode;
        if (key == Keys.None || HotkeyCombo.IsModifierKey(key)) return true;

        var mods = HotkeyModifiers.None;
        if ((keyData & Keys.Control) != 0) mods |= HotkeyModifiers.Ctrl;
        if ((keyData & Keys.Alt) != 0) mods |= HotkeyModifiers.Alt;
        if ((keyData & Keys.Shift) != 0) mods |= HotkeyModifiers.Shift;

        Value = new HotkeyCombo(mods, key);
        ValueChanged?.Invoke(this, EventArgs.Empty);
        return true;
    }
}

/// <summary>
/// 저장 전에 단축키가 실제 잠금 중과 같은 방식(저수준 후크)으로 감지되는지 시험하는 대화 상자.
/// 시험 중에는 입력을 차단하지 않고, 일치한 단축키만 소비합니다.
/// </summary>
internal sealed class HotkeyTestForm : Form
{
    private readonly InputHook _hook;
    private readonly Label _result = new()
    {
        AutoSize = true,
        MaximumSize = new Size(400, 0),
        Margin = new Padding(0, 12, 0, 12),
        Text = "아직 감지되지 않았습니다.",
    };

    public HotkeyTestForm(HotkeyCombo combo)
    {
        Text = "단축키 시험";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.CenterParent;
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;
        Padding = new Padding(16);

        var intro = new Label
        {
            AutoSize = true,
            MaximumSize = new Size(400, 0),
            Text = $"키보드에서 {combo} 를 눌러 보세요.\n\n" +
                   "실제 잠금 중 해제와 같은 방식(저수준 입력 후크)으로 감지합니다. " +
                   "감지되지 않으면 Windows나 다른 프로그램이 이 조합을 먼저 처리하는 것일 수 있으니 다른 조합을 선택하세요.",
        };
        var close = new Button { Text = "닫기", DialogResult = DialogResult.OK, AutoSize = true };

        var flow = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
        };
        flow.Controls.Add(intro);
        flow.Controls.Add(_result);
        flow.Controls.Add(close);
        Controls.Add(flow);
        AcceptButton = close;
        CancelButton = close;

        _hook = new InputHook(this) { Hotkey = combo, IncludeMouse = false };
        _hook.HotkeyPressed += () =>
        {
            Detected = true;
            _result.ForeColor = Color.ForestGreen;
            _result.Text = "✔ 감지되었습니다. 잠금 중에도 이 조합으로 해제할 수 있습니다.";
        };
    }

    public bool Detected { get; private set; }

    protected override void OnShown(EventArgs e)
    {
        base.OnShown(e);
        if (!_hook.Start())
        {
            _result.ForeColor = Color.Firebrick;
            _result.Text = "입력 후크를 시작하지 못했습니다. 시험할 수 없습니다.";
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _hook.Dispose();
        base.OnFormClosed(e);
    }
}

internal sealed class SettingsForm : Form
{
    private readonly LockController _controller;
    private readonly AppSettings _original;
    private readonly Action<AppSettings> _apply;
    private readonly HashSet<string> _tested = new();
    private readonly System.Windows.Forms.Timer _statusTimer = new() { Interval = 500 };

    private readonly Label _lblStatus = new() { AutoSize = true, Font = new Font(SystemFonts.DefaultFont, FontStyle.Bold) };
    private readonly CheckBox _chkEnabled = new() { Text = "잠금 기능 사용", AutoSize = true };
    private readonly NumericUpDown _numHours = new() { Minimum = 0, Maximum = 99, Width = 56 };
    private readonly NumericUpDown _numMinutes = new() { Minimum = 0, Maximum = 59, Width = 56 };
    private readonly NumericUpDown _numSeconds = new() { Minimum = 0, Maximum = 59, Width = 56 };
    private readonly NumericUpDown _numCountdown = new()
    {
        Minimum = AppSettings.MinCountdownSeconds,
        Maximum = AppSettings.MaxCountdownSeconds,
        Width = 56,
    };
    private readonly HotkeyBox _hkUnlock = new();
    private readonly Button _btnTestUnlock = new() { Text = "시험…", AutoSize = true };
    private readonly Label _warnUnlock = NewWarningLabel();
    private readonly CheckBox _chkSame = new() { Text = "해제 단축키와 동일", AutoSize = true };
    private readonly HotkeyBox _hkCancel = new();
    private readonly Button _btnTestCancel = new() { Text = "시험…", AutoSize = true };
    private readonly Label _warnCancel = NewWarningLabel();
    private readonly CheckBox _chkCancelOnInput = new() { Text = "카운트다운 중 일반 입력이 있으면 취소", AutoSize = true };
    private readonly Button _btnSave = new() { Text = "저장", AutoSize = true, MinimumSize = new Size(80, 0) };
    private readonly Button _btnClose = new() { Text = "닫기", AutoSize = true, MinimumSize = new Size(80, 0), DialogResult = DialogResult.Cancel };

    public SettingsForm(LockController controller, AppSettings current, Action<AppSettings> apply, bool firstRun)
    {
        _controller = controller;
        _original = current.Clone();
        _apply = apply;

        Text = "입력 잠금 타이머 설정";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        AutoSize = true;
        AutoSizeMode = AutoSizeMode.GrowAndShrink;

        BuildLayout(firstRun);
        LoadValues(current);

        _hkUnlock.ValueChanged += (_, _) => RefreshWarnings();
        _hkCancel.ValueChanged += (_, _) => RefreshWarnings();
        _chkSame.CheckedChanged += (_, _) => RefreshWarnings();
        _btnTestUnlock.Click += (_, _) => TestHotkey(_hkUnlock.Value);
        _btnTestCancel.Click += (_, _) => TestHotkey(_hkCancel.Value);
        _btnSave.Click += (_, _) => OnSave();

        AcceptButton = _btnSave;
        CancelButton = _btnClose;

        _statusTimer.Tick += (_, _) => RefreshStatus();
        _statusTimer.Start();
        RefreshStatus();
        RefreshWarnings();
    }

    private static Label NewWarningLabel() => new()
    {
        AutoSize = true,
        MaximumSize = new Size(420, 0),
        ForeColor = Color.FromArgb(176, 84, 0),
        Margin = new Padding(0, 2, 0, 2),
    };

    private static Label Caption(string text) => new()
    {
        Text = text,
        AutoSize = true,
        Margin = new Padding(0, 6, 8, 0),
    };

    private static FlowLayoutPanel Row(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            WrapContents = false,
            FlowDirection = FlowDirection.LeftToRight,
            Margin = new Padding(0, 2, 0, 2),
        };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private static GroupBox Group(string title, params Control[] rows)
    {
        var flow = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Dock = DockStyle.Top,
        };
        flow.Controls.AddRange(rows);

        var box = new GroupBox
        {
            Text = title,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(10, 6, 10, 8),
            Margin = new Padding(0, 6, 0, 6),
            MinimumSize = new Size(450, 0),
        };
        box.Controls.Add(flow);
        return box;
    }

    private void BuildLayout(bool firstRun)
    {
        var root = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(14),
        };

        if (firstRun)
        {
            root.Controls.Add(new Label
            {
                AutoSize = true,
                MaximumSize = new Size(450, 0),
                Margin = new Padding(0, 0, 0, 8),
                Text = "처음 실행하셨습니다. 해제 단축키를 [시험…]으로 확인한 뒤 '잠금 기능 사용'을 켜고 저장하세요. " +
                       "기능을 켜기 전까지는 입력이 차단되지 않습니다.",
            });
        }

        root.Controls.Add(_lblStatus);
        root.Controls.Add(_chkEnabled);

        root.Controls.Add(Group("비활성 시간",
            Row(Caption("키보드·마우스 입력이 없으면"),
                _numHours, Caption("시간"),
                _numMinutes, Caption("분"),
                _numSeconds, Caption("초")),
            Row(Caption("잠금 전 카운트다운"), _numCountdown, Caption("초"))));

        root.Controls.Add(Group("해제 단축키",
            Row(_hkUnlock, _btnTestUnlock),
            Caption("상자를 클릭한 뒤 원하는 키 조합을 누르세요. 예: Ctrl+Alt+K, Shift+F12"),
            _warnUnlock));

        root.Controls.Add(Group("카운트다운 취소",
            _chkSame,
            Row(_hkCancel, _btnTestCancel),
            _warnCancel,
            _chkCancelOnInput));

        root.Controls.Add(new Label
        {
            AutoSize = true,
            MaximumSize = new Size(450, 0),
            ForeColor = SystemColors.GrayText,
            Margin = new Padding(0, 4, 0, 4),
            Text = "입력 내용은 기록하거나 외부로 전송하지 않습니다. " +
                   "설정은 사용자 프로필(%APPDATA%\\InputLockTimer\\settings.json)에만 저장됩니다.",
        });

        var buttons = new FlowLayoutPanel
        {
            FlowDirection = FlowDirection.RightToLeft,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Dock = DockStyle.Top,
            Margin = new Padding(0, 8, 0, 0),
        };
        buttons.Controls.Add(_btnClose);
        buttons.Controls.Add(_btnSave);
        root.Controls.Add(buttons);

        Controls.Add(root);
    }

    private void LoadValues(AppSettings s)
    {
        _chkEnabled.Checked = s.Enabled;
        _numHours.Value = s.InactivityHours;
        _numMinutes.Value = s.InactivityMinutes;
        _numSeconds.Value = s.InactivitySeconds;
        _numCountdown.Value = s.CountdownSeconds;
        _hkUnlock.Value = HotkeyCombo.TryParse(s.UnlockHotkey, out var unlock) ? unlock : null;
        _chkSame.Checked = s.CancelHotkeySameAsUnlock;
        _hkCancel.Value = HotkeyCombo.TryParse(s.CancelHotkey, out var cancel) ? cancel : _hkUnlock.Value;
        _chkCancelOnInput.Checked = s.CancelOnInput;
    }

    private void RefreshStatus() => _lblStatus.Text = "현재 상태: " + _controller.StatusText;

    private void RefreshWarnings()
    {
        _warnUnlock.Text = FormatWarnings(_hkUnlock.Value);
        _warnCancel.Text = _chkSame.Checked ? "" : FormatWarnings(_hkCancel.Value);
        _hkCancel.Enabled = !_chkSame.Checked;
        _btnTestCancel.Enabled = !_chkSame.Checked;
    }

    private static string FormatWarnings(HotkeyCombo? combo)
    {
        if (combo is not { } value) return "";
        var error = value.Validate();
        if (error != null) return "⚠ " + error;
        return string.Join(Environment.NewLine, value.GetWarnings().Select(w => "⚠ " + w));
    }

    private void TestHotkey(HotkeyCombo? combo)
    {
        if (combo is not { } value) return;
        var error = value.Validate();
        if (error != null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        using var dialog = new HotkeyTestForm(value);
        dialog.ShowDialog(this);
        if (dialog.Detected) _tested.Add(value.ToString());
    }

    private AppSettings? Collect()
    {
        var settings = new AppSettings
        {
            Enabled = _chkEnabled.Checked,
            InactivityHours = (int)_numHours.Value,
            InactivityMinutes = (int)_numMinutes.Value,
            InactivitySeconds = (int)_numSeconds.Value,
            CountdownSeconds = (int)_numCountdown.Value,
            UnlockHotkey = _hkUnlock.Value?.ToString() ?? "",
            CancelHotkeySameAsUnlock = _chkSame.Checked,
            CancelHotkey = _hkCancel.Value?.ToString() ?? "",
            CancelOnInput = _chkCancelOnInput.Checked,
        };

        var error = settings.Validate();
        if (error != null)
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return null;
        }

        return settings;
    }

    private void OnSave()
    {
        var settings = Collect();
        if (settings == null) return;

        settings.TryGetUnlockHotkey(out var unlock);
        bool unlockChanged = settings.UnlockHotkey != _original.UnlockHotkey;
        bool cancelChanged = !settings.CancelHotkeySameAsUnlock
                             && (_original.CancelHotkeySameAsUnlock || settings.CancelHotkey != _original.CancelHotkey);

        // 1) 충돌 가능성 안내
        if (unlockChanged || cancelChanged)
        {
            var combos = new List<HotkeyCombo> { unlock };
            if (!settings.CancelHotkeySameAsUnlock && settings.TryGetCancelHotkey(out var cancel))
                combos.Add(cancel);

            var warnings = combos.Distinct()
                .SelectMany(c => c.GetWarnings().Select(w => $"[{c}] {w}"))
                .ToList();

            if (warnings.Count > 0)
            {
                var answer = MessageBox.Show(this,
                    "단축키 충돌 가능성이 있습니다.\n\n" + string.Join("\n", warnings) + "\n\n그래도 저장하시겠습니까?",
                    Text, MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
                if (answer != DialogResult.Yes) return;
            }
        }

        // 2) 아직 시험하지 않은 해제 단축키로 기능을 켜려는 경우
        bool newlyEnabling = settings.Enabled && (!_original.Enabled || unlockChanged);
        if (newlyEnabling && !_tested.Contains(unlock.ToString()))
        {
            var answer = MessageBox.Show(this,
                $"해제 단축키 {unlock} 를 아직 시험하지 않았습니다.\n" +
                "해제 단축키가 감지되지 않으면 잠금을 풀 수 없으니 먼저 시험하는 것을 권장합니다.\n\n" +
                "예: 지금 시험  /  아니요: 그대로 저장  /  취소: 돌아가기",
                Text, MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
            if (answer == DialogResult.Yes) { TestHotkey(unlock); return; }
            if (answer == DialogResult.Cancel) return;
        }

        try
        {
            _apply(settings);
            DialogResult = DialogResult.OK;
            Close();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this,
                "설정을 파일에 저장하지 못했습니다. 이번 실행에는 적용되었지만 다음 실행에는 유지되지 않을 수 있습니다.\n\n" + ex.Message,
                Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    protected override void OnFormClosed(FormClosedEventArgs e)
    {
        _statusTimer.Stop();
        _statusTimer.Dispose();
        base.OnFormClosed(e);
    }
}
