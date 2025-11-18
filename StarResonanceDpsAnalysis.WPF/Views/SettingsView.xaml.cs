using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using StarResonanceDpsAnalysis.WPF.ViewModels;

namespace StarResonanceDpsAnalysis.WPF.Views;

/// <summary>
/// Interaction logic for SettingsView.xaml
/// </summary>
public partial class SettingsView : Window
{
    private readonly SettingsViewModel _vm;

    public SettingsView(SettingsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;

        _vm = vm;
        _vm.RequestClose += Vm_RequestClose;
    }

    private void Vm_RequestClose()
    {
        Close();
    }

    private void Footer_ConfirmClick(object sender, RoutedEventArgs e) { /* handled by VM command */ }

    private void Footer_CancelClick(object sender, RoutedEventArgs e) { /* handled by VM command */ }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            Close();
        }
    }

    private void ScrollToSection(FrameworkElement? target)
    {
        if (target == null) return;

        if (ContentScrollViewer?.Content is FrameworkElement content)
        {
            var p = target.TransformToVisual(content).Transform(new Point(0, 0));
            var y = Math.Max(p.Y, 0);
            ContentScrollViewer.ScrollToVerticalOffset(y);
        }
        else
        {
            target.BringIntoView();
        }
    }

    private void Nav_Language_Click(object sender, RoutedEventArgs e) => ScrollToSection(SectionLanguage);
    private void Nav_Basic_Click(object sender, RoutedEventArgs e) => ScrollToSection(SectionBasic);
    private void Nav_Shortcut_Click(object sender, RoutedEventArgs e) => ScrollToSection(SectionShortcut);
    private void Nav_Combat_Click(object sender, RoutedEventArgs e) => ScrollToSection(SectionCombat);
    private void Nav_Theme_Click(object sender, RoutedEventArgs e) => ScrollToSection(SectionTheme);
}
