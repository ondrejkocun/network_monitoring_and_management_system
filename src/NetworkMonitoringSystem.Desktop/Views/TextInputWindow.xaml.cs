using System.Windows;

namespace NetworkMonitoringSystem.Desktop.Views;

/// <summary>
/// Interaction logic for TextInputWindow.xaml: one line of text with Save and Cancel. Empty text cannot be saved.
/// </summary>
public partial class TextInputWindow : Window
{
    public TextInputWindow(string title, string prompt, string initialText, int maxLength)
    {
        InitializeComponent();

        Title = title;
        PromptLabel.Content = prompt;
        InputBox.MaxLength = maxLength;
        InputBox.Text = initialText;
        InputBox.SelectAll();
    }

    public string Text => InputBox.Text.Trim();

    private void OnTextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        ConfirmButton.IsEnabled = InputBox.Text.Trim().Length > 0;
    }

    private void OnConfirmClick(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
    }
}
