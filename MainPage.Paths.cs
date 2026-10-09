using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using WinRT.Interop;

using SDeleteGUI.Models;
using SDeleteGUI.Services;

namespace SDeleteGUI;

public sealed partial class MainPage
{
	private async void AddFiles_Click(object sender, RoutedEventArgs e)
	{
		var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
		picker.FileTypeFilter.Add("*");
		InitializeWithWindow.Initialize(picker, GetWindowHandle());

		var files = await picker.PickMultipleFilesAsync();
		foreach (var file in files)
		{
			if (_pathEntries.Any(p => string.Equals(p.Path, file.Path, StringComparison.OrdinalIgnoreCase)))
				continue;

			AddPathRow(file.Path, isDirectory: false);
		}

		await ShowBlockedPathsWarningAsync();
	}

	private async void AddFolder_Click(object sender, RoutedEventArgs e)
	{
		var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
		picker.FileTypeFilter.Add("*");
		InitializeWithWindow.Initialize(picker, GetWindowHandle());

		var folder = await picker.PickSingleFolderAsync();
		if (folder != null)
		{
			if (!_pathEntries.Any(p => string.Equals(p.Path, folder.Path, StringComparison.OrdinalIgnoreCase)))
				AddPathRow(folder.Path, isDirectory: true);

			await ShowBlockedPathsWarningAsync();
		}
	}

	private bool AddPathRow(string path, bool isDirectory, bool contentsOnly = false)
	{
		if (SystemPathGuard.IsProtected(path))
		{
			_blockedPaths.Add(path);
			return false;
		}

		var entry = new PathEntry { Path = path, IsDirectory = isDirectory, ContentsOnly = contentsOnly };
		_pathEntries.Add(entry);

		var row = BuildPathRow(entry);
		PathsPanel.Children.Add(row);

		UpdateEmptyPathsHint();
		RefreshCommandPreview();
		RecalculatePathStats();

		return true;
	}

	private async Task ShowBlockedPathsWarningAsync()
	{
		if (_blockedPaths.Count == 0 || XamlRoot is null)
			return;

		var blocked = _blockedPaths.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
		_blockedPaths.Clear();

		const int maxListed = 5;
		var details = string.Join(Environment.NewLine, blocked.Take(maxListed));
		if (blocked.Count > maxListed)
			details += Environment.NewLine + Loc.Format("BlockedPathsMore", blocked.Count - maxListed);

		var content = new StackPanel { Spacing = 12 };
		content.Children.Add(new TextBlock
		{
			Text = Loc.Get("BlockedPathsText"),
			TextWrapping = TextWrapping.Wrap
		});
		content.Children.Add(new TextBlock
		{
			Text = details,
			TextWrapping = TextWrapping.Wrap,
			FontFamily = new FontFamily("Consolas"),
			FontSize = 12,
			IsTextSelectionEnabled = true,
			Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"]
		});

		var dialog = new ContentDialog
		{
			XamlRoot = XamlRoot,
			Title = Loc.Get("BlockedPathsTitle"),
			Content = content,
			CloseButtonText = Loc.Get("BlockedPathsClose"),
			DefaultButton = ContentDialogButton.Close
		};

		await dialog.ShowAsync();
	}

	private void ClearPathsButton_Click(object sender, RoutedEventArgs e)
	{
		_pathEntries.Clear();
		_pathRowControls.Clear();

		for (var i = PathsPanel.Children.Count - 1; i >= 0; i--)
		{
			if (!ReferenceEquals(PathsPanel.Children[i], EmptyPathsHint))
				PathsPanel.Children.RemoveAt(i);
		}

		UpdateEmptyPathsHint();
		RefreshCommandPreview();
		RecalculatePathStats();
	}

	private void RootGrid_DragOver(object sender, DragEventArgs e)
	{
		if (_isRunning ||
			!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
		{
			e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.None;
			return;
		}

		e.AcceptedOperation = Windows.ApplicationModel.DataTransfer.DataPackageOperation.Copy;
		e.DragUIOverride.Caption = Loc.Get("DropToAddCaption");
	}

	private async void RootGrid_Drop(object sender, DragEventArgs e)
	{
		if (_isRunning ||
			!e.DataView.Contains(Windows.ApplicationModel.DataTransfer.StandardDataFormats.StorageItems))
			return;

		var deferral = e.GetDeferral();
		try
		{
			var items = await e.DataView.GetStorageItemsAsync();
			var added = false;

			foreach (var item in items)
			{
				if (string.IsNullOrEmpty(item.Path))
					continue;

				if (_pathEntries.Any(p => string.Equals(p.Path, item.Path, StringComparison.OrdinalIgnoreCase)))
					continue;

				AddPathRow(item.Path, isDirectory: item.IsOfType(Windows.Storage.StorageItemTypes.Folder));
				added = true;
			}

			if (added)
				MainPivot.SelectedIndex = 0;
		}
		finally
		{
			deferral.Complete();
		}

		await ShowBlockedPathsWarningAsync();
	}

	private FrameworkElement BuildPathRow(PathEntry entry)
	{
		var grid = new Grid { ColumnSpacing = 8 };
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
		grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

		var iconFallback = new FontIcon
		{
			Glyph = entry.IsDirectory ? "\uE8B7" : "\uE8A5",
			FontSize = 20,
			HorizontalAlignment = HorizontalAlignment.Center,
			VerticalAlignment = VerticalAlignment.Center
		};
		var iconImage = new Image { Width = 24, Height = 24, Visibility = Visibility.Collapsed };
		var iconHost = new Grid { Width = 24, Height = 24, VerticalAlignment = VerticalAlignment.Center };
		iconHost.Children.Add(iconFallback);
		iconHost.Children.Add(iconImage);

		var iconButton = new Button
		{
			Style = (Style)Resources["PathIconButtonStyle"],
			Content = iconHost
		};
		ToolTipService.SetToolTip(iconButton, Loc.Get("OpenPathToolTip"));
		iconButton.Click += (_, _) => OpenPathInExplorer(entry);
		Grid.SetColumn(iconButton, 0);
		_ = LoadPathIconAsync(entry, iconImage, iconFallback);

		var pathTextBox = new TextBox
		{
			Text = entry.Path,
			IsReadOnly = true,
			VerticalAlignment = VerticalAlignment.Center
		};
		Grid.SetColumn(pathTextBox, 1);

		var contentsOnlyCheckBox = new CheckBox
		{
			Content = Loc.Get("ContentsOnlyCheckBox"),
			IsChecked = entry.ContentsOnly,
			VerticalAlignment = VerticalAlignment.Center,
			Visibility = entry.IsDirectory ? Visibility.Visible : Visibility.Collapsed
		};
		contentsOnlyCheckBox.Checked += (_, _) => { entry.ContentsOnly = true; RefreshCommandPreview(); };
		contentsOnlyCheckBox.Unchecked += (_, _) => { entry.ContentsOnly = false; RefreshCommandPreview(); };
		Grid.SetColumn(contentsOnlyCheckBox, 2);

		var changeButton = new Button { Content = Loc.Get("ChangePathButton") };
		changeButton.Click += async (_, _) =>
			await ChangePathAsync(entry, pathTextBox, contentsOnlyCheckBox, iconImage, iconFallback);
		Grid.SetColumn(changeButton, 3);

		var removeButton = new Button { Content = "✕" };
		removeButton.Click += (_, _) => RemovePathRow(entry, grid);
		Grid.SetColumn(removeButton, 4);

		grid.Children.Add(iconButton);
		grid.Children.Add(pathTextBox);
		grid.Children.Add(contentsOnlyCheckBox);
		grid.Children.Add(changeButton);
		grid.Children.Add(removeButton);

		_pathRowControls[entry] = new PathRowControls
		{
			Row = grid,
			PathTextBox = pathTextBox,
			ContentsOnlyCheckBox = contentsOnlyCheckBox,
			ChangeButton = changeButton,
			IconButton = iconButton
		};

		if (entry.IsDeleted)
			SetRowDeletedVisual(entry, true);

		return grid;
	}

	private async Task ChangePathAsync(
		PathEntry entry,
		TextBox pathTextBox,
		CheckBox contentsOnlyCheckBox,
		Image iconImage,
		FontIcon iconFallback)
	{
		string? newPath;
		bool isDirectory = entry.IsDirectory;

		if (isDirectory)
		{
			var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
			picker.FileTypeFilter.Add("*");
			InitializeWithWindow.Initialize(picker, GetWindowHandle());

			var folder = await picker.PickSingleFolderAsync();
			if (folder is null) return;
			newPath = folder.Path;
		}
		else
		{
			var picker = new FileOpenPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
			picker.FileTypeFilter.Add("*");
			InitializeWithWindow.Initialize(picker, GetWindowHandle());

			var file = await picker.PickSingleFileAsync();
			if (file is null) return;
			newPath = file.Path;
		}

		if (SystemPathGuard.IsProtected(newPath))
		{
			_blockedPaths.Add(newPath);
			await ShowBlockedPathsWarningAsync();
			return;
		}

		if (_pathEntries.Any(p => !ReferenceEquals(p, entry) &&
			string.Equals(p.Path, newPath, StringComparison.OrdinalIgnoreCase)))
		{
			return;
		}

		entry.Path = newPath;
		entry.ContentsOnly = false;
		pathTextBox.Text = newPath;
		contentsOnlyCheckBox.IsChecked = false;
		contentsOnlyCheckBox.Visibility = isDirectory ? Visibility.Visible : Visibility.Collapsed;

		iconImage.Source = null;
		iconImage.Visibility = Visibility.Collapsed;
		iconFallback.Glyph = isDirectory ? "\uE8B7" : "\uE8A5";
		iconFallback.Visibility = Visibility.Visible;
		_ = LoadPathIconAsync(entry, iconImage, iconFallback);

		RefreshCommandPreview();
		RecalculatePathStats();
	}

	private static readonly SemaphoreSlim IconLoadGate = new(3);

	private static async Task LoadPathIconAsync(PathEntry entry, Image image, FontIcon fallback)
	{
		var path = entry.Path;
		var isDirectory = entry.IsDirectory;

		const int attempts = 3;
		for (var attempt = 1; attempt <= attempts; attempt++)
		{
			var bitmap = await TryLoadThumbnailAsync(path, isDirectory);
			if (bitmap is not null)
			{
				if (entry.Path == path)
				{
					image.Source = bitmap;
					image.Visibility = Visibility.Visible;
					fallback.Visibility = Visibility.Collapsed;
				}
				return;
			}

			if (attempt < attempts)
				await Task.Delay(attempt * 250);
		}
	}

	private static async Task<Microsoft.UI.Xaml.Media.Imaging.BitmapImage?> TryLoadThumbnailAsync(
		string path, bool isDirectory)
	{
		await IconLoadGate.WaitAsync();
		try
		{
			Windows.Storage.IStorageItemProperties item;
			if (isDirectory)
				item = await Windows.Storage.StorageFolder.GetFolderFromPathAsync(path);
			else
				item = await Windows.Storage.StorageFile.GetFileFromPathAsync(path);

			using var thumbnail = await item.GetThumbnailAsync(
				Windows.Storage.FileProperties.ThumbnailMode.ListView,
				32,
				Windows.Storage.FileProperties.ThumbnailOptions.UseCurrentScale);

			if (thumbnail is null || thumbnail.Size == 0)
				return null;

			var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
			await bitmap.SetSourceAsync(thumbnail);
			return bitmap;
		}
		catch (Exception ex)
		{
			Debug.WriteLine($"[PathIcon] failed for '{path}': {ex.Message}");
			return null;
		}
		finally
		{
			IconLoadGate.Release();
		}
	}

	[DllImport("shell32.dll", CharSet = CharSet.Unicode)]
	private static extern int SHParseDisplayName(
		string pszName, IntPtr pbc, out IntPtr ppidl, uint sfgaoIn, out uint psfgaoOut);

	[DllImport("shell32.dll")]
	private static extern int SHOpenFolderAndSelectItems(
		IntPtr pidlFolder, uint cidl, IntPtr[]? apidl, uint dwFlags);

	[DllImport("ole32.dll")]
	private static extern void CoTaskMemFree(IntPtr pv);

	private static void OpenPathInExplorer(PathEntry entry)
	{
		try
		{
			if (entry.IsDirectory)
			{
				if (Directory.Exists(entry.Path))
					Process.Start(new ProcessStartInfo(entry.Path) { UseShellExecute = true });
				return;
			}

			if (!File.Exists(entry.Path))
				return;

			if (!TrySelectInShell(entry.Path))
			{
				var parent = Path.GetDirectoryName(entry.Path);
				if (!string.IsNullOrEmpty(parent))
					Process.Start(new ProcessStartInfo(parent) { UseShellExecute = true });
			}
		}
		catch
		{
		}
	}

	private static bool TrySelectInShell(string path)
	{
		var pidl = IntPtr.Zero;
		try
		{
			if (SHParseDisplayName(path, IntPtr.Zero, out pidl, 0, out _) != 0 || pidl == IntPtr.Zero)
				return false;

			return SHOpenFolderAndSelectItems(pidl, 0, null, 0) == 0;
		}
		finally
		{
			if (pidl != IntPtr.Zero)
				CoTaskMemFree(pidl);
		}
	}

	private void RemovePathRow(PathEntry entry, FrameworkElement row)
	{
		_pathRowControls.Remove(entry);
		_pathEntries.Remove(entry);
		PathsPanel.Children.Remove(row);
		UpdateEmptyPathsHint();
		RefreshCommandPreview();
		RecalculatePathStats();
	}

	private void SetRowDeletedVisual(PathEntry entry, bool isDeleted)
	{
		if (!_pathRowControls.TryGetValue(entry, out var controls))
			return;

		controls.PathTextBox.IsEnabled = !isDeleted;
		controls.ContentsOnlyCheckBox.IsEnabled = !isDeleted;
		controls.ChangeButton.IsEnabled = !isDeleted;
		controls.IconButton.IsEnabled = !isDeleted;
		controls.Row.Opacity = isDeleted ? 0.5 : 1.0;
	}

	private void MarkProcessedPathsAsDeleted()
	{
		foreach (var entry in _pathEntries.Where(p => !p.IsDeleted))
		{
			entry.IsDeleted = true;
			SetRowDeletedVisual(entry, true);
		}

		RecalculatePathStats();
	}

	private void UpdateEmptyPathsHint()
	{
		EmptyPathsHint.Visibility = _pathEntries.Count == 0
			? Visibility.Visible
			: Visibility.Collapsed;
	}

	private enum PathStatsState { Counting, Done, Stopped }

	private CancellationTokenSource? _statsCts;
	private PathStats _lastStats;
	private bool _statsInProgress;

	private async void RecalculatePathStats()
	{
		_statsCts?.Cancel();

		if (_pathEntries.Count == 0)
		{
			_statsInProgress = false;
			PathsStatsText.Text = string.Empty;
			return;
		}

		var cts = new CancellationTokenSource();
		_statsCts = cts;
		_statsInProgress = true;
		_lastStats = default;

		var snapshot = _pathEntries
			.Where(p => !p.IsDeleted)
			.Select(p => (p.Path, p.IsDirectory))
			.ToList();

		var progress = new Progress<PathStats>(stats =>
		{
			if (cts.IsCancellationRequested)
				return;

			_lastStats = stats;
			ShowStats(stats, PathStatsState.Counting);
		});

		try
		{
			await Task.Delay(150, cts.Token);

			var result = await Task.Run(
				() => PathStatsCalculator.Calculate(snapshot, progress, cts.Token),
				cts.Token);

			if (cts.IsCancellationRequested)
				return;

			_lastStats = result;
			ShowStats(result, PathStatsState.Done);
		}
		catch (OperationCanceledException)
		{
		}
		finally
		{
			if (ReferenceEquals(_statsCts, cts))
				_statsInProgress = false;
		}
	}

	private void StopPathStatsCounting()
	{
		if (!_statsInProgress || _statsCts is null)
			return;

		_statsCts.Cancel();
		_statsInProgress = false;

		if (_lastStats == default)
			PathsStatsText.Text = string.Empty;
		else
			ShowStats(_lastStats, PathStatsState.Stopped);
	}

	private void ShowStats(PathStats stats, PathStatsState state)
	{
		var key = state switch
		{
			PathStatsState.Counting => "PathsStatsCounting",
			PathStatsState.Stopped => "PathsStatsStopped",
			_ => "PathsStats"
		};

		PathsStatsText.Text = Loc.Format(
			key,
			stats.Files.ToString("N0"),
			stats.Folders.ToString("N0"),
			SizeFormatter.Format(stats.Bytes));
	}
}
