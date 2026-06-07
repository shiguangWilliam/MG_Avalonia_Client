using Avalonia.Controls;
using Avalonia.Threading;
using ClientAvalonia.Services;

namespace ClientAvalonia.Views;

public partial class CnCNetTopBar : UserControl
{
    private readonly DispatcherTimer _clockTimer;
    private Action<string>? _navigate;
    private Action? _logout;

    public CnCNetTopBar()
    {
        InitializeComponent();
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
        _clockTimer.Tick += (_, _) => UpdateClock();
        _clockTimer.Start();
        UpdateClock();

        BtnMainMenu.Click += (_, _) => _navigate?.Invoke("MainMenu");
        BtnCnCNetLobby.Click += (_, _) => _navigate?.Invoke("CnCNetLobby");
        BtnSettings.Click += (_, _) => _navigate?.Invoke("OptionsWindow");
        BtnLogout.Click += (_, _) => _logout?.Invoke();
    }

    public void BindNavigation(Action<string> navigate, Action? logout = null)
    {
        _navigate = navigate;
        _logout = logout;
    }

    public void UpdateState(string connectionStatus, int onlineCount)
    {
        LblConnectionStatus.Text = connectionStatus;
        LblOnlineCount.Text = onlineCount >= 0 ? onlineCount.ToString() : "N/A";
    }

    private void UpdateClock()
        => LblClock.Text = DateTime.Now.ToString("H:mm:ss yyyy/M/d");
}
