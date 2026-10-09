using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.Windows.ApplicationModel.Resources;
using System.Runtime.InteropServices;

// To learn more about WinUI, the WinUI project structure,
// and more about our project templates, see: http://aka.ms/winui-project-info.

namespace SDeleteGUI;

/// <summary>
/// The application window. This hosts a Frame that displays pages. Add your
/// UI and logic to MainPage.xaml / MainPage.xaml.cs instead of here so you
/// can use Page features such as navigation events and the Loaded lifecycle.
/// </summary>
public sealed partial class MainWindow : Window
{
	private bool _closeConfirmed;

    public MainWindow()
    {
        InitializeComponent();

        var resources = new ResourceLoader();
        var title = resources.GetString("AppWindowTitle");

        Title = title;
        AppTitleBar.Title = title;

        ExtendsContentIntoTitleBar = true;
        SetTitleBar(AppTitleBar);

        AppWindow.SetIcon("Assets/AppIcon.ico");

        if (AppWindow.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
        {
            ApplyWindowSize();

            presenter.IsResizable = false;

            presenter.IsMaximizable = false;
            presenter.IsMinimizable = true;
        }

        // Navigate the root frame to the main page on startup.
        RootFrame.Navigate(typeof(MainPage));

		AppWindow.Closing += AppWindow_Closing;
        Closed += MainWindow_Closed;
    }

    private async void AppWindow_Closing(AppWindow sender, AppWindowClosingEventArgs args)
    {
        if (_closeConfirmed)
            return;

        if (RootFrame.Content is not MainPage page)
            return;

        if (!page.IsRunning)
            return;

        args.Cancel = true;

        var confirmed = await page.ConfirmCloseWhileRunningAsync();
        if (!confirmed)
            return;

        _closeConfirmed = true;
        page.RequestStopForClose();
        Close();
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        if (RootFrame.Content is MainPage page)
        {
            page.SaveSettingsOnClose();
        }
    }


	[DllImport("user32.dll")]
	private static extern uint GetDpiForWindow(IntPtr hWnd);

	private void ApplyWindowSize()
	{
		const int designWidth = 800;
		const int designHeight = 900;

		var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(this);
		var scale = GetDpiForWindow(hwnd) / 96.0;
		var workArea = DisplayArea.GetFromWindowId(AppWindow.Id, DisplayAreaFallback.Primary).WorkArea;

		var width = Math.Min((int)(designWidth * scale), workArea.Width);
		var height = Math.Min((int)(designHeight * scale), workArea.Height);

		AppWindow.Resize(new Windows.Graphics.SizeInt32(width, height));
		AppWindow.Move(new Windows.Graphics.PointInt32(
			workArea.X + (workArea.Width - width) / 2,
			workArea.Y + (workArea.Height - height) / 2));
	}
}
