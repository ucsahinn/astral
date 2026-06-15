using System.Diagnostics;
using System.Windows;

namespace Discorder.App;

public partial class ReleaseNotesWindow : Window
{
    private readonly Uri _releaseUri;

    public ReleaseNotesWindow(Uri releaseUri)
    {
        _releaseUri = releaseUri ?? throw new ArgumentNullException(nameof(releaseUri));
        InitializeComponent();
    }

    private void OpenReleasePage_Click(object sender, RoutedEventArgs e)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = _releaseUri.AbsoluteUri,
            UseShellExecute = true
        });
    }

    private void Close_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
