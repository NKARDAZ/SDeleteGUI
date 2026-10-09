using System.Diagnostics;
using Microsoft.UI.Dispatching;
using System.Collections.ObjectModel;
using System.IO;
using System.Text;
using System.Reflection;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.UI;
using WinRT.Interop;

using SDeleteGUI.Models;
using SDeleteGUI.Services;

namespace SDeleteGUI;

public sealed partial class MainPage : Page
{
	private readonly SDeleteOptions _options = new();
	private readonly List<PathEntry> _pathEntries = new();
	private readonly Dictionary<PathEntry, PathRowControls> _pathRowControls = new();

	private sealed class PathRowControls
	{
		public required Grid Row { get; init; }
		public required TextBox PathTextBox { get; init; }
		public required CheckBox ContentsOnlyCheckBox { get; init; }
		public required Button ChangeButton { get; init; }
		public required Button IconButton { get; init; }
	}

	private readonly List<string> _blockedPaths = new();
	private readonly ObservableCollection<DriveItem> _drives = new();
	private readonly ObservableCollection<DriveItem> _visibleDrives = new();

	private bool _isReady;
	private bool _isRunning;

	private DispatcherTimer? _statusTimer;
	private DateTime _processStartTime;
	private bool _stopRequestedByUser;

	private Brush? _originalButtonPointerOverBackground;
	private Brush? _originalButtonPressedBackground;

	private readonly StringBuilder _outputBuffer = new();
	private readonly DispatcherTimer _outputFlushTimer;
	private bool _outputDirty;
	private const int MaxOutputBufferChars = 2_000_000;

	private ScrollViewer? _outputScrollViewer;

	public bool IsRunning => _isRunning;

	public MainPage()
	{
		InitializeComponent();

		_outputFlushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(150) };
		_outputFlushTimer.Tick += (_, _) => FlushOutputToUi();
		_outputFlushTimer.Start();

		Loaded += async (_, _) =>
		{
			await ShowBlockedPathsWarningAsync();

			var flash = DrivesGridView.SelectedItems.OfType<DriveItem>().Where(d => d.IsFlashBased).ToList();
			if (flash.Count > 0)
				await ShowFlashWarningAsync(flash);
		};

		InitializeStatusBar();

		RunButton.Content = Loc.Get("RunButton");

		DrivesGridView.ItemsSource = _visibleDrives;

		LoadDrives();

		var settings = SettingsService.Load();
		ApplySettingsToUI(settings);
		ApplyDriveVisibilityFilter();
		UpdateSdeletePresenceWarning();

		ApplyStartupArguments(App.StartupArguments);

		_isReady = true;

		RefreshCommandPreview();
	}

	public async Task<bool> ConfirmCloseWhileRunningAsync()
	{
		var dialog = new ContentDialog
		{
			XamlRoot = this.XamlRoot,
			Title = Loc.Get("CloseWhileRunningTitle"),
			Content = Loc.Get("CloseWhileRunningText"),
			PrimaryButtonText = Loc.Get("CloseWhileRunningYes"),
			CloseButtonText = Loc.Get("CloseWhileRunningNo"),
			DefaultButton = ContentDialogButton.Close
		};

		var result = await dialog.ShowAsync();
		return result == ContentDialogResult.Primary;
	}

	public void RequestStopForClose()
	{
		PseudoConsoleProcessRunner.RequestStop();
	}

	private void InitializeStatusBar()
	{
		var informationalVersion = Assembly.GetExecutingAssembly()
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
			.InformationalVersion;

		var version = informationalVersion?.Split('+')[0] ?? "1.0.0";

		StatusVersionText.Text = version;
		StatusAuthorLink.Content = Loc.Get("AuthorName");

		SetStatusIdle();
	}

	private void SetStatusIdle()
	{
		StatusIcon.Glyph = "\uE945";
		StatusIcon.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
		StatusIcon.Visibility = Visibility.Visible;
		StatusProgressRing.IsActive = false;
		StatusProgressRing.Visibility = Visibility.Collapsed;
	}

	private void SetStatusRunning()
	{
		StatusIcon.Visibility = Visibility.Collapsed;
		StatusProgressRing.IsActive = true;
		StatusProgressRing.Visibility = Visibility.Visible;
		_processStartTime = DateTime.Now;
		StatusTimerText.Text = "00:00:00";
		StartStatusTimer();
	}

	private void SetStatusCompleted(int exitCode)
	{
		StopStatusTimer();
		StatusProgressRing.IsActive = false;
		StatusProgressRing.Visibility = Visibility.Collapsed;
		StatusIcon.Visibility = Visibility.Visible;

		if (exitCode == 0)
		{
			StatusIcon.Glyph = "\uE73E";
			StatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorSuccessBrush"];
		}
		else
		{
			StatusIcon.Glyph = "\uEA39";
			StatusIcon.Foreground = (Brush)Application.Current.Resources["SystemFillColorCriticalBrush"];
		}
	}

	private void SetStatusStopped()
	{
		StopStatusTimer();
		StatusProgressRing.IsActive = false;
		StatusProgressRing.Visibility = Visibility.Collapsed;
		StatusIcon.Visibility = Visibility.Visible;
		StatusIcon.Glyph = "\uE711";
		StatusIcon.Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"];
	}

	private void StartStatusTimer()
	{
		_statusTimer ??= new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		_statusTimer.Tick -= OnStatusTimerTick;
		_statusTimer.Tick += OnStatusTimerTick;
		_statusTimer.Start();
	}

	private void StopStatusTimer()
	{
		_statusTimer?.Stop();
	}

	private void OnStatusTimerTick(object? sender, object e)
	{
		var elapsed = DateTime.Now - _processStartTime;
		StatusTimerText.Text = elapsed.ToString(@"hh\:mm\:ss");
	}

	private static readonly System.Text.RegularExpressions.Regex DriveRootRegex =
		new(@"^[A-Za-z]:\\?$", System.Text.RegularExpressions.RegexOptions.Compiled);

	private void ApplyStartupArguments(string[] args)
	{
		if (args.Length == 0) return;

		var driveArgs = new List<string>();
		var pathArgs = new List<string>();

		foreach (var arg in args)
		{
			var trimmed = arg.Trim();
			if (trimmed.Length == 0) continue;

			if (DriveRootRegex.IsMatch(trimmed))
				driveArgs.Add(trimmed.TrimEnd('\\').ToUpperInvariant());
			else if (Directory.Exists(trimmed) || File.Exists(trimmed))
				pathArgs.Add(trimmed);
		}

		if (pathArgs.Count > 0)
		{
			MainPivot.SelectedIndex = 0;
			foreach (var path in pathArgs)
				AddPathRow(path, isDirectory: Directory.Exists(path));
		}
		else if (driveArgs.Count > 0)
		{
			MainPivot.SelectedIndex = 1;

			var requested = _drives.Where(d => driveArgs.Contains(d.Name, StringComparer.OrdinalIgnoreCase)).ToList();
			if (requested.Any(d => !d.IsRecommendedForClean))
			{
				HideNotRecommendedCheckBox.IsChecked = false;
				ApplyDriveVisibilityFilter();
			}

			foreach (var item in requested)
				DrivesGridView.SelectedItems.Add(item);
		}
	}

	private nint GetWindowHandle() => WindowNative.GetWindowHandle(App.MainWindow);

	private static T? FindDescendant<T>(DependencyObject root) where T : DependencyObject
	{
		var count = VisualTreeHelper.GetChildrenCount(root);
		for (int i = 0; i < count; i++)
		{
			var child = VisualTreeHelper.GetChild(root, i);
			if (child is T typed) return typed;
			var result = FindDescendant<T>(child);
			if (result != null) return result;
		}
		return null;
	}

	private const double MinContentHeight = 760;

	private void RootScroll_SizeChanged(object sender, SizeChangedEventArgs e)
	{
		RootGrid.Height = Math.Max(e.NewSize.Height, MinContentHeight);
	}
}
