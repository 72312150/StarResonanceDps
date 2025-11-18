using CommunityToolkit.Mvvm.ComponentModel;
using StarResonanceDpsAnalysis.Core.Models;

namespace StarResonanceDpsAnalysis.WPF.ViewModels;

/// <summary>
/// Data carrier used for DataTemplate bindings (attached to ProgressBarData.Data)
/// </summary>
public partial class PlayerSlotViewModel : OrderingDataViewModel
{
    [ObservableProperty] private Classes _class = Classes.Unknown;
    [ObservableProperty] private string _name = string.Empty;

    [ObservableProperty] private string _nickname = string.Empty;

    [ObservableProperty] private ulong _value;
}