using System.Windows;
using PocketMC.Infrastructure.News;

namespace PocketMC.Desktop.Features.News;

public partial class NewsPopupWindow : Wpf.Ui.Controls.FluentWindow
{
    public bool WasAcknowledged { get; private set; }

    public NewsPopupWindow(NewsItem item)
    {
        InitializeComponent();
        ArticleView.Display(item);
        Microsoft.Extensions.DependencyInjection.ServiceProviderServiceExtensions
            .GetService<PocketMC.Desktop.Features.Shell.Interfaces.IShellVisualService>(((App)System.Windows.Application.Current).Services)
            ?.ApplyThemeToDialog(this);
    }

    private void GotIt_Click(object sender, RoutedEventArgs e)
    {
        WasAcknowledged = true;
        Close();
    }
}