using System.Windows;
using Chattyn.ViewModels;

namespace Chattyn;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private readonly Action _onSaved;

    public SettingsWindow(SettingsViewModel vm, Action onSaved)
    {
        InitializeComponent();
        _vm = vm;
        _onSaved = onSaved;
        DataContext = vm;
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => Close();

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (!_vm.TryApply()) return;
        _onSaved();
        Close();
    }
}
