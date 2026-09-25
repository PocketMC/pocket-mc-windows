using System;
using System.Windows;

namespace PocketMC.Desktop.Features.Shell;

public partial class SplashWindow : Window
{
    private static SplashWindow? _instance;

    public SplashWindow()
    {
        InitializeComponent();
        Title = PocketMC.Infrastructure.Configuration.AppConfig.AppName;
        SourceInitialized += OnSourceInitialized;
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        CenterOnPrimaryScreen();
    }

    private void CenterOnPrimaryScreen()
    {
        double screenWidth = SystemParameters.PrimaryScreenWidth;
        double screenHeight = SystemParameters.PrimaryScreenHeight;
        Left = (screenWidth - Width) / 2;
        Top = (screenHeight - Height) / 2;
    }

    public static void ShowSplash()
    {
        if (_instance != null) return;

        _instance = new SplashWindow();
        _instance.Show();
    }

    public static void CloseSplash()
    {
        if (_instance == null) return;

        try
        {
            var win = _instance;
            _instance = null;
            win.Close();
        }
        catch
        {
            // Ignore errors if window is already closed or closing
        }
    }
}
