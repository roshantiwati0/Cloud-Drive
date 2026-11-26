using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CloudSSD.App;
using CloudSSD.Core;

namespace CloudSSD.Desktop.Views;

/// <summary>
/// Modal dialog providing Gemini AI-powered multimodal file summarization,
/// interactive question answering ("Chat with File"), and semantic auto-tagging.
/// </summary>
public sealed class GeminiInsightsWindow : Window
{
    private readonly AppHost _host;
    private readonly string _filePath;
    private readonly FileInfo _fileInfo;

    private readonly Border _apiKeyBanner;
    private readonly PasswordBox _apiKeyBox;
    private readonly Button _saveKeyBtn;

    // Summary tab controls
    private readonly TextBlock _summaryStatus;
    private readonly TextBox _summaryBox;
    private readonly Button _refreshSummaryBtn;
    private readonly Button _copySummaryBtn;

    // Q&A tab controls
    private readonly TextBox _chatHistoryBox;
    private readonly TextBox _questionInput;
    private readonly Button _askBtn;

    // Tags tab controls
    private readonly WrapPanel _tagsPanel;
    private readonly Button _refreshTagsBtn;
    private readonly Button _copyTagsBtn;

    private static string T(string s) => LocalizationManager.T(s);

    public GeminiInsightsWindow(AppHost host, string filePath)
    {
        _host = host;
        _filePath = filePath;
        _fileInfo = new FileInfo(filePath);

        Title = $"Gemini AI Insights — {_fileInfo.Name}";
        Width = 660;
        Height = 620;
        MinWidth = 500;
        MinHeight = 450;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Owner = Application.Current?.MainWindow;

        if (Application.Current?.TryFindResource("DialogWindow") is Style st) Style = st;

        var root = new Grid { Margin = new Thickness(20) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Header
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // API Key Banner
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Tabs
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Footer

        // 1. Header
        var header = new DockPanel { Margin = new Thickness(0, 0, 0, 14) };
        var icon = new TextBlock
        {
            Text = "✨",
            FontSize = 24,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 12, 0)
        };
        DockPanel.SetDock(icon, Dock.Left);
        header.Children.Add(icon);

        var titleStack = new StackPanel();
        var titleText = new TextBlock
        {
            Text = _fileInfo.Name,
            FontSize = 17,
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        titleText.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        titleStack.Children.Add(titleText);

        var subText = new TextBlock
        {
            Text = $"{FormatSize(_fileInfo.Length)}  •  {_filePath}",
            FontSize = 12,
            Margin = new Thickness(0, 2, 0, 0),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        subText.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        titleStack.Children.Add(subText);
        header.Children.Add(titleStack);

        Grid.SetRow(header, 0);
        root.Children.Add(header);

        // 2. API Key Banner (if not configured)
        _apiKeyBanner = new Border
        {
            Margin = new Thickness(0, 0, 0, 14),
            Padding = new Thickness(12),
            CornerRadius = new CornerRadius(6),
            Visibility = _host.Gemini.IsConfigured ? Visibility.Collapsed : Visibility.Visible
        };
        _apiKeyBanner.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
        _apiKeyBanner.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
        _apiKeyBanner.BorderThickness = new Thickness(1);

        var bannerStack = new StackPanel();
        var bannerLabel = new TextBlock
        {
            Text = "Gemini API key is required. (Get a free API key at aistudio.google.com/apikey)",
            FontSize = 12.5,
            Margin = new Thickness(0, 0, 0, 8),
            TextWrapping = TextWrapping.Wrap
        };
        bannerLabel.SetResourceReference(TextBlock.ForegroundProperty, "Text");
        bannerStack.Children.Add(bannerLabel);

        var keyRow = new DockPanel();
        _saveKeyBtn = new Button { Content = "Save Key", Padding = new Thickness(14, 4, 14, 4) };
        if (Application.Current?.TryFindResource("Primary") is Style pStyle) _saveKeyBtn.Style = pStyle;
        _saveKeyBtn.Click += SaveApiKey;
        DockPanel.SetDock(_saveKeyBtn, Dock.Right);
        keyRow.Children.Add(_saveKeyBtn);

        _apiKeyBox = new PasswordBox { Margin = new Thickness(0, 0, 8, 0) };
        keyRow.Children.Add(_apiKeyBox);
        bannerStack.Children.Add(keyRow);

        _apiKeyBanner.Child = bannerStack;
        Grid.SetRow(_apiKeyBanner, 1);
        root.Children.Add(_apiKeyBanner);

        // 3. TabControl
        var tabs = new TabControl { Margin = new Thickness(0, 0, 0, 12) };

        // --- TAB 1: Summary ---
        var summaryTab = new TabItem { Header = "Summary" };
        var summaryGrid = new Grid { Margin = new Thickness(8) };
        summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        summaryGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        summaryGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var summaryBar = new DockPanel { Margin = new Thickness(0, 0, 0, 8) };
        _refreshSummaryBtn = new Button { Content = "Refresh Summary", Padding = new Thickness(12, 4, 12, 4) };
        _refreshSummaryBtn.Click += async (_, _) => await LoadSummaryAsync();
        DockPanel.SetDock(_refreshSummaryBtn, Dock.Right);
        summaryBar.Children.Add(_refreshSummaryBtn);

        _summaryStatus = new TextBlock { Text = "Ready", VerticalAlignment = VerticalAlignment.Center, FontSize = 12 };
        _summaryStatus.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        summaryBar.Children.Add(_summaryStatus);
        Grid.SetRow(summaryBar, 0);
        summaryGrid.Children.Add(summaryBar);

        _summaryBox = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AcceptsReturn = true,
            Padding = new Thickness(10),
            FontSize = 13
        };
        _summaryBox.SetResourceReference(TextBox.BackgroundProperty, "Surface");
        _summaryBox.SetResourceReference(TextBox.ForegroundProperty, "Text");
        _summaryBox.SetResourceReference(TextBox.BorderBrushProperty, "BorderBrush");
        Grid.SetRow(_summaryBox, 1);
        summaryGrid.Children.Add(_summaryBox);

        var summaryBottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        _copySummaryBtn = new Button { Content = "Copy Summary", Padding = new Thickness(12, 4, 12, 4) };
        _copySummaryBtn.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_summaryBox.Text))
            {
                Clipboard.SetText(_summaryBox.Text);
                _summaryStatus.Text = "Summary copied to clipboard!";
            }
        };
        summaryBottom.Children.Add(_copySummaryBtn);
        Grid.SetRow(summaryBottom, 2);
        summaryGrid.Children.Add(summaryBottom);

        summaryTab.Content = summaryGrid;
        tabs.Items.Add(summaryTab);

        // --- TAB 2: Ask & Chat ---
        var qaTab = new TabItem { Header = "Ask Question" };
        var qaGrid = new Grid { Margin = new Thickness(8) };
        qaGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        qaGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        _chatHistoryBox = new TextBox
        {
            IsReadOnly = true,
            TextWrapping = TextWrapping.Wrap,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            AcceptsReturn = true,
            Padding = new Thickness(10),
            FontSize = 13,
            Margin = new Thickness(0, 0, 0, 8)
        };
        _chatHistoryBox.SetResourceReference(TextBox.BackgroundProperty, "Surface");
        _chatHistoryBox.SetResourceReference(TextBox.ForegroundProperty, "Text");
        _chatHistoryBox.SetResourceReference(TextBox.BorderBrushProperty, "BorderBrush");
        Grid.SetRow(_chatHistoryBox, 0);
        qaGrid.Children.Add(_chatHistoryBox);

        var qaInputRow = new DockPanel();
        _askBtn = new Button { Content = "Ask", Padding = new Thickness(16, 6, 16, 6) };
        if (Application.Current?.TryFindResource("Primary") is Style askStyle) _askBtn.Style = askStyle;
        _askBtn.Click += async (_, _) => await AskQuestionAsync();
        DockPanel.SetDock(_askBtn, Dock.Right);
        qaInputRow.Children.Add(_askBtn);

        _questionInput = new TextBox
        {
            Margin = new Thickness(0, 0, 8, 0),
            Padding = new Thickness(8, 6, 8, 6),
            FontSize = 13
        };
        _questionInput.KeyDown += async (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                e.Handled = true;
                await AskQuestionAsync();
            }
        };
        qaInputRow.Children.Add(_questionInput);
        Grid.SetRow(qaInputRow, 1);
        qaGrid.Children.Add(qaInputRow);

        qaTab.Content = qaGrid;
        tabs.Items.Add(qaTab);

        // --- TAB 3: Semantic Tags ---
        var tagsTab = new TabItem { Header = "Smart Tags" };
        var tagsGrid = new Grid { Margin = new Thickness(8) };
        tagsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        tagsGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        tagsGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var tagsTop = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 10) };
        _refreshTagsBtn = new Button { Content = "Suggest Tags", Padding = new Thickness(12, 4, 12, 4) };
        _refreshTagsBtn.Click += async (_, _) => await LoadTagsAsync();
        tagsTop.Children.Add(_refreshTagsBtn);
        Grid.SetRow(tagsTop, 0);
        tagsGrid.Children.Add(tagsTop);

        _tagsPanel = new WrapPanel { Margin = new Thickness(4) };
        var scrollTags = new ScrollViewer { Content = _tagsPanel, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };
        Grid.SetRow(scrollTags, 1);
        tagsGrid.Children.Add(scrollTags);

        var tagsBottom = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
        _copyTagsBtn = new Button { Content = "Copy Tags", Padding = new Thickness(12, 4, 12, 4) };
        _copyTagsBtn.Click += (_, _) => CopyTags();
        tagsBottom.Children.Add(_copyTagsBtn);
        Grid.SetRow(tagsBottom, 2);
        tagsGrid.Children.Add(tagsBottom);

        tagsTab.Content = tagsGrid;
        tabs.Items.Add(tagsTab);

        Grid.SetRow(tabs, 2);
        root.Children.Add(tabs);

        // 4. Footer
        var footer = new DockPanel();
        var closeBtn = new Button
        {
            Content = T("Close"),
            IsCancel = true,
            Padding = new Thickness(16, 6, 16, 6)
        };
        DockPanel.SetDock(closeBtn, Dock.Right);
        footer.Children.Add(closeBtn);

        var modelInfo = new TextBlock
        {
            Text = $"Powered by Google Gemini ({_host.Gemini.Model})",
            VerticalAlignment = VerticalAlignment.Center,
            FontSize = 11.5
        };
        modelInfo.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        footer.Children.Add(modelInfo);

        Grid.SetRow(footer, 3);
        root.Children.Add(footer);

        Content = root;

        Loaded += async (_, _) =>
        {
            if (_host.Gemini.IsConfigured)
            {
                await LoadSummaryAsync();
            }
            else
            {
                _summaryBox.Text = "Please enter and save your Gemini API key above to analyze this file.";
            }
        };
    }

    private void SaveApiKey(object? sender, RoutedEventArgs? e)
    {
        var key = _apiKeyBox.Password.Trim();
        if (string.IsNullOrWhiteSpace(key)) return;

        _host.Settings.Gemini.ApiKey = key;
        _host.SettingsStore.Save(_host.Settings);
        _apiKeyBanner.Visibility = Visibility.Collapsed;
        _summaryStatus.Text = "API key saved successfully.";

        _ = LoadSummaryAsync();
    }

    private async Task LoadSummaryAsync()
    {
        if (!_host.Gemini.IsConfigured)
        {
            _apiKeyBanner.Visibility = Visibility.Visible;
            return;
        }

        _summaryStatus.Text = "Generating summary with Gemini...";
        _refreshSummaryBtn.IsEnabled = false;
        try
        {
            var summary = await _host.Gemini.SummarizeFileAsync(_filePath);
            _summaryBox.Text = summary;
            _summaryStatus.Text = "Summary ready.";
        }
        catch (Exception ex)
        {
            _summaryBox.Text = $"Error generating summary:\n{ex.Message}";
            _summaryStatus.Text = "Failed.";
        }
        finally
        {
            _refreshSummaryBtn.IsEnabled = true;
        }
    }

    private async Task AskQuestionAsync()
    {
        var q = _questionInput.Text.Trim();
        if (string.IsNullOrWhiteSpace(q)) return;

        if (!_host.Gemini.IsConfigured)
        {
            _apiKeyBanner.Visibility = Visibility.Visible;
            return;
        }

        _askBtn.IsEnabled = false;
        _questionInput.IsEnabled = false;
        _chatHistoryBox.AppendText($"\n\nQ: {q}\nThinking...");
        _chatHistoryBox.ScrollToEnd();

        try
        {
            var answer = await _host.Gemini.AskFileAsync(_filePath, q);
            // Replace "Thinking..." with answer
            var current = _chatHistoryBox.Text;
            int idx = current.LastIndexOf("Thinking...", StringComparison.Ordinal);
            if (idx >= 0)
                _chatHistoryBox.Text = current[..idx] + $"A: {answer}";
            else
                _chatHistoryBox.AppendText($"\nA: {answer}");

            _questionInput.Clear();
            _chatHistoryBox.ScrollToEnd();
        }
        catch (Exception ex)
        {
            _chatHistoryBox.AppendText($"\nError: {ex.Message}");
        }
        finally
        {
            _askBtn.IsEnabled = true;
            _questionInput.IsEnabled = true;
            _questionInput.Focus();
        }
    }

    private async Task LoadTagsAsync()
    {
        if (!_host.Gemini.IsConfigured)
        {
            _apiKeyBanner.Visibility = Visibility.Visible;
            return;
        }

        _refreshTagsBtn.IsEnabled = false;
        _tagsPanel.Children.Clear();
        var loading = new TextBlock { Text = "Analyzing file for tags...", Margin = new Thickness(6) };
        loading.SetResourceReference(TextBlock.ForegroundProperty, "Muted");
        _tagsPanel.Children.Add(loading);

        try
        {
            var tags = await _host.Gemini.SuggestTagsAsync(_filePath);
            _tagsPanel.Children.Clear();
            foreach (var tag in tags)
            {
                var pill = new Border
                {
                    CornerRadius = new CornerRadius(14),
                    Padding = new Thickness(12, 6, 12, 6),
                    Margin = new Thickness(4)
                };
                pill.SetResourceReference(Border.BackgroundProperty, "SurfaceAlt");
                pill.SetResourceReference(Border.BorderBrushProperty, "BorderBrush");
                pill.BorderThickness = new Thickness(1);

                var txt = new TextBlock { Text = $"#{tag}", FontWeight = FontWeights.Medium, FontSize = 12.5 };
                txt.SetResourceReference(TextBlock.ForegroundProperty, "Accent");
                pill.Child = txt;
                _tagsPanel.Children.Add(pill);
            }
        }
        catch (Exception ex)
        {
            _tagsPanel.Children.Clear();
            var err = new TextBlock { Text = $"Error: {ex.Message}", Margin = new Thickness(6) };
            _tagsPanel.Children.Add(err);
        }
        finally
        {
            _refreshTagsBtn.IsEnabled = true;
        }
    }

    private void CopyTags()
    {
        var tags = _tagsPanel.Children.OfType<Border>()
            .Select(b => (b.Child as TextBlock)?.Text)
            .Where(t => !string.IsNullOrEmpty(t));
        var joined = string.Join(" ", tags);
        if (!string.IsNullOrWhiteSpace(joined))
        {
            Clipboard.SetText(joined);
        }
    }

    private static string FormatSize(long bytes)
    {
        if (bytes < 1024) return $"{bytes} B";
        if (bytes < 1024 * 1024) return $"{bytes / 1024.0:F1} KB";
        return $"{bytes / (1024.0 * 1024.0):F1} MB";
    }
}
