namespace InputLockTimer;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var mutex = new Mutex(true, @"Local\InputLockTimer.SingleInstance", out bool isFirstInstance);
        if (!isFirstInstance)
        {
            MessageBox.Show(
                "입력 잠금 타이머가 이미 실행 중입니다. 알림 영역(트레이)의 아이콘을 확인하세요.",
                "입력 잠금 타이머", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        try
        {
            Application.SetHighDpiMode(HighDpiMode.PerMonitorV2);
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);

            TrayApp? app = null;

            // 어떤 오류가 나더라도 입력 차단이 남지 않도록 먼저 해제합니다.
            Application.ThreadException += (_, e) =>
            {
                app?.EmergencyRelease();
                MessageBox.Show("예기치 않은 오류가 발생했습니다.\n\n" + e.Exception.Message,
                    "입력 잠금 타이머", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (_, _) => app?.EmergencyRelease();

            app = new TrayApp();
            Application.Run(app);
        }
        finally
        {
            mutex.ReleaseMutex();
        }
    }
}
