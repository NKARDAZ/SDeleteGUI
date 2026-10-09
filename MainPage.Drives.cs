using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using SDeleteGUI.Models;
using SDeleteGUI.Services;

namespace SDeleteGUI;

public sealed partial class MainPage
{
	private void RefreshDrivesButton_Click(object sender, RoutedEventArgs e)
	{
		LoadDrives();
		ApplyDriveVisibilityFilter();
		RefreshCommandPreview();
	}

	private void LoadDrives()
	{
		_drives.Clear();

		foreach (var drive in DriveInfo.GetDrives())
		{
			if (!drive.IsReady)
				continue;

			var label = string.IsNullOrWhiteSpace(drive.VolumeLabel) ? Loc.Get("LocalDisk") : drive.VolumeLabel;
			var letter = drive.Name.TrimEnd('\\');
			var kind = DriveTypeDetector.Detect(drive);

			_drives.Add(new DriveItem
			{
				Name = drive.Name,
				DriveLetter = letter,
				DisplayName = TruncateLabel(label),
				Glyph = GetGlyphForDriveType(drive.DriveType),
				TypeLabel = DriveTypeDetector.GetLabel(kind),
				IsFlashBased = kind is DriveKind.Ssd or DriveKind.Flash,
				TotalSize = drive.TotalSize,
				FreeSpace = drive.TotalFreeSpace,
				IsRecommendedForClean = kind is not (DriveKind.Ssd or DriveKind.Flash or DriveKind.Network or DriveKind.CdRom),
			});
		}
	}

	private void ApplyDriveVisibilityFilter()
	{
		var hideNotRecommended = HideNotRecommendedCheckBox.IsChecked == true;

		_visibleDrives.Clear();
		foreach (var drive in _drives)
		{
			if (!hideNotRecommended || drive.IsRecommendedForClean)
				_visibleDrives.Add(drive);
		}

		if (_drives.Count == 0)
		{
			EmptyDrivesHint.Text = Loc.Get("EmptyDrivesHintNoneText");
			EmptyDrivesHint.Visibility = Visibility.Visible;
		}
		else if (_visibleDrives.Count == 0)
		{
			EmptyDrivesHint.Text = Loc.Get("EmptyDrivesHintFilteredText");
			EmptyDrivesHint.Visibility = Visibility.Visible;
		}
		else
		{
			EmptyDrivesHint.Visibility = Visibility.Collapsed;
		}
	}

	private void OnHideNotRecommendedChanged(object sender, RoutedEventArgs e)
	{
		if (!_isReady) return;
		ApplyDriveVisibilityFilter();
		RefreshCommandPreview();
	}

	private static string GetGlyphForDriveType(DriveType type) => type switch
	{
		DriveType.Removable => "\uE88E",
		DriveType.Network => "\uE8CE",
		DriveType.CDRom => "\uE958",
		DriveType.Ram => "\uEDA2",
		_ => "\uEDA2",
	};

	private async void OnDrivesSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_isReady) return;
		RefreshCommandPreview();

		var flash = e.AddedItems.OfType<DriveItem>().Where(d => d.IsFlashBased).ToList();
		if (flash.Count > 0)
			await ShowFlashWarningAsync(flash);
	}

	private async Task ShowFlashWarningAsync(IEnumerable<DriveItem> drives)
	{
		if (XamlRoot is null)
			return;

		var kinds = string.Join("/", drives.Select(d => d.TypeLabel).Distinct());

		var dialog = new ContentDialog
		{
			XamlRoot = XamlRoot,
			Title = Loc.Get("FlashWarningTitle"),
			Content = new TextBlock
			{
				Text = Loc.Format("FlashWarningText", kinds),
				TextWrapping = TextWrapping.Wrap
			},
			CloseButtonText = Loc.Get("DialogOkButton"),
			DefaultButton = ContentDialogButton.Close
		};

		await dialog.ShowAsync();
	}

	private static string TruncateLabel(string label, int maxLength = 18)
	{
		return label.Length > maxLength
			? label[..maxLength] + "…"
			: label;
	}
}
