using Microsoft.UI.Xaml.Controls;
using SDeleteGUI.Models;
using SDeleteGUI.Services;

namespace SDeleteGUI;

public sealed partial class MainPage
{
	private void ApplySettingsToUI(AppSettings settings)
	{
		Use64BitCheckBox.IsChecked = string.Equals(settings.Executable, "Sdelete64", StringComparison.OrdinalIgnoreCase);

		PassesNumberBox.Value = settings.Passes < 1 ? 1 : settings.Passes;

		SdeleteFolderTextBox.Text = settings.SdeleteFolderPath ?? string.Empty;

		RecurseCheckBox.IsChecked = settings.Recursive;
		RemoveReadOnlyCheckBox.IsChecked = settings.RemoveReadOnlyAttribute;

		if (string.Equals(settings.CleanMode, "ZeroFill", StringComparison.OrdinalIgnoreCase))
			ZeroFillRadio.IsChecked = true;
		else
			CleanFreeRadio.IsChecked = true;

		MainPivot.SelectedIndex = string.Equals(settings.Mode, "CleanFreeSpace", StringComparison.OrdinalIgnoreCase)
			? 1
			: 0;

		HideNotRecommendedCheckBox.IsChecked = settings.HideNotRecommendedDrives;
	}

	private AppSettings CollectSettingsFromUI()
	{
		return new AppSettings
		{
			Version = 1,
			Executable = Use64BitCheckBox.IsChecked == true ? "Sdelete64" : "Sdelete",
			SdeleteFolderPath = string.IsNullOrWhiteSpace(SdeleteFolderTextBox.Text)
				? null
				: SdeleteFolderTextBox.Text,
			Passes = double.IsNaN(PassesNumberBox.Value) ? 3 : (int)PassesNumberBox.Value,
			Mode = MainPivot.SelectedIndex == 0 ? "DeleteFiles" : "CleanFreeSpace",
			Recursive = RecurseCheckBox.IsChecked == true,
			RemoveReadOnlyAttribute = RemoveReadOnlyCheckBox.IsChecked == true,
			CleanMode = ZeroFillRadio.IsChecked == true ? "ZeroFill" : "CleanFree",
			HideNotRecommendedDrives = HideNotRecommendedCheckBox.IsChecked == true,
		};
	}

	public void SaveSettingsOnClose()
	{
		try
		{
			var settings = CollectSettingsFromUI();
			SettingsService.Save(settings);
		}
		catch (Exception ex)
		{
			System.Diagnostics.Debug.WriteLine($"SaveSettingsOnClose failed: {ex}");
		}
	}
}
