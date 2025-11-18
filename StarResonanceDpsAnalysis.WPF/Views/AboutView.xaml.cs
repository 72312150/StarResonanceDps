using System.Diagnostics;
using System.Reflection;
using System.Windows;
using System.Windows.Navigation;

namespace StarResonanceDpsAnalysis.WPF.Views;

/// <summary>
/// Interaction logic for AboutView.xaml
/// </summary>
public partial class AboutView : Window
{
    public AboutView()
    {
        InitializeComponent();
    }

    public static string Version
    {
        get
        {
            var v = Assembly
                .GetEntryAssembly()
                ?.GetCustomAttribute<AssemblyFileVersionAttribute>()?
                .Version ?? "-.-.-";
            return $"v{v.Split('+')[0]}";
        }
    }

    private void Footer_ConfirmClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Footer_CancelClick(object sender, RoutedEventArgs e)
    {
        Close();
    }

    private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
    {
        Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true });
        e.Handled = true;
    }
}