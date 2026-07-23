using Avalonia.Controls;
using Avalonia.Threading;
using ClientAvalonia.Services;

namespace ClientAvalonia.Views;

public partial class CnCNetTopBar : UserControl
{
    private readonly DispatcherTimer _clockTimer;
    private Action<string>? _navigate;
    private Action? _logout;
    private Action? _openPrivateMessages;

    public CnCNetTopBar()
    {
        InitializeComponent();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        BtnMainMenu.Click += (_, _) => _navigate?.Invoke("MainMenu");
        BtnCnCNetLobby.Click += (_, _) => _navigate?.Invoke("CnCNetLobby");
        BtnPrivateMessages.Click += (_, _) => _openPrivateMessages?.Invoke();
        BtnSettings.Click += (_, _) => _navigate?.Invoke("OptionsWindow");
        BtnLogout.Click += (_, _) => _logout?.Invoke();
    }

    public void BindNavigation(Action<string> navigate, Action? logout = null, Action? openPrivateMessages = null)
    {
        _navigate = navigate;
        _logout = logout;
        _openPrivateMessages = openPrivateMessages;
    }

    public void UpdateState(string connectionStatus, int onlineCount, bool isConnected = false, int unreadPrivateMessages = 0)
    {
        LblConnectionStatus.Text = connectionStatus;
        LblOnlineCount.Text = onlineCount >= 0 ? onlineCount.ToString() : "N/A";

        BtnPrivateMessages.IsEnabled = isConnected;
        BtnPrivateMessages.Content = unreadPrivateMessages > 0
            ? $"私信 (F4) · {unreadPrivateMessages}"
            : "私信 (F4)";
    }

    private void UpdateClock()
        => LblClock.Text = DateTime.Now.ToString("H:mm:ss yyyy/M/d");
}
