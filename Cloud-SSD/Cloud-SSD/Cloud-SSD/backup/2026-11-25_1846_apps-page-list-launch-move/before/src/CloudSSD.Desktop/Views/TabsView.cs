using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using CloudSSD.Desktop.ViewModels;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// A section of pages side by side (<see cref="TabsViewModel"/>), like the Files page: a chip per page at the top,
/// the chosen page below (its own view, found by the window's templates).
/// </summary>
public sealed class TabsView : UserControl
{
    private readonly WrapPanel _chips = new() { Margin = new Thickness(0, 0, 14, 12) };
    private readonly ContentControl _body = new() { Focusable = false };
    private readonly Dictionary<PageViewModel, RadioButton> _byPage = new();
    private TabsViewModel? _tabs;
    private bool _syncing;

    public TabsView()
    {
        var root = new DockPanel();
        DockPanel.SetDock(_chips, Dock.Top);
        root.Children.Add(_chips);
        _body.SetBinding(ContentProperty, new Binding(nameof(TabsViewModel.Current)));
        root.Children.Add(_body);
        Content = root;
        DataContextChanged += (_, _) => Build();
        Unloaded += (_, _) => { if (_tabs != null) _tabs.PropertyChanged -= OnTabsChanged; };
        Loaded += (_, _) => { if (_tabs != null) { _tabs.PropertyChanged -= OnTabsChanged; _tabs.PropertyChanged += OnTabsChanged; Sync(); } };
        Build();
    }

    private void Build()
    {
        if (_tabs != null) _tabs.PropertyChanged -= OnTabsChanged;
        _tabs = DataContext as TabsViewModel;
        _chips.Children.Clear();
        _byPage.Clear();
        if (_tabs is null) return;
        _tabs.PropertyChanged += OnTabsChanged;
        // Each view of this kind has its own group, so two sections do not switch each other's chips.
        var group = "cssd-tabs-" + _tabs.Title;
        foreach (var tab in _tabs.Tabs)
        {
            var text = LocalizationManager.T(tab.Label);
            var chip = new RadioButton
            {
                Content = text, GroupName = group, Height = 34, Padding = new Thickness(16, 0, 16, 0), Margin = new Thickness(0, 0, 8, 0),
                HorizontalContentAlignment = HorizontalAlignment.Center, VerticalContentAlignment = VerticalAlignment.Center,
            };
            if (Application.Current?.TryFindResource("PillRadio") is Style ps) chip.Style = ps;
            System.Windows.Automation.AutomationProperties.SetName(chip, text);
            var page = tab.Page;
            chip.Checked += (_, _) => { if (!_syncing) _tabs?.Select(page); };
            _byPage[page] = chip;
            _chips.Children.Add(chip);
        }
        Sync();
    }

    private void OnTabsChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(TabsViewModel.Current)) Dispatcher.BeginInvoke(Sync);
    }

    /// <summary>The chip of the page shown is the chosen one (the page can also be chosen from elsewhere).</summary>
    private void Sync()
    {
        if (_tabs is null || !_byPage.TryGetValue(_tabs.Current, out var chip) || chip.IsChecked == true) return;
        _syncing = true;
        try { chip.IsChecked = true; }
        finally { _syncing = false; }
    }
}
