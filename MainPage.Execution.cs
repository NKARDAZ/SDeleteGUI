using System.IO;
using System.Text;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Windows.Storage.Pickers;
using Windows.UI;
using WinRT.Interop;

using SDeleteGUI.Models;
using SDeleteGUI.Services;

namespace SDeleteGUI;

public sealed partial class MainPage
{
	private async void BrowseSdeleteFolderButton_Click(object sender, RoutedEventArgs e)
	{
		var picker = new FolderPicker { SuggestedStartLocation = PickerLocationId.ComputerFolder };
		picker.FileTypeFilter.Add("*");
		InitializeWithWindow.Initialize(picker, GetWindowHandle());

		var folder = await picker.PickSingleFolderAsync();
		if (folder != null)
		{
			SdeleteFolderTextBox.Text = folder.Path;
		}
	}

	private void OnOptionChanged(object sender, RoutedEventArgs e)
	{
		if (!_isReady) return;
		RefreshCommandPreview();
		UpdateSdeletePresenceWarning();
	}

	private void OnPassesChanged(NumberBox sender, NumberBoxValueChangedEventArgs args)
	{
		if (!_isReady) return;
		RefreshCommandPreview();
	}

	private void OnSdeleteFolderChanged(object sender, TextChangedEventArgs e)
	{
		if (!_isReady) return;
		RefreshCommandPreview();
		UpdateSdeletePresenceWarning();
	}

	private void OnPivotSelectionChanged(object sender, SelectionChangedEventArgs e)
	{
		if (!_isReady) return;
		RefreshCommandPreview();
	}

	private void SyncOptionsFromUI()
	{
		_options.Mode = MainPivot.SelectedIndex == 0 ? SDeleteMode.DeleteFiles : SDeleteMode.CleanFreeSpace;

		_options.Executable = Use64BitCheckBox.IsChecked == true ? SDeleteExecutable.Sdelete64 : SDeleteExecutable.Sdelete;
		_options.SdeleteFolderPath = string.IsNullOrWhiteSpace(SdeleteFolderTextBox.Text) ? null : SdeleteFolderTextBox.Text;
		_options.Passes = double.IsNaN(PassesNumberBox.Value) ? 1 : (int)PassesNumberBox.Value;

		_options.TargetPaths = _pathEntries.Where(p => !p.IsDeleted).ToList();
		_options.Recursive = RecurseCheckBox.IsChecked == true;
		_options.RemoveReadOnlyAttribute = RemoveReadOnlyCheckBox.IsChecked == true;

		_options.SelectedDrives = DrivesGridView.SelectedItems
			.Cast<DriveItem>()
			.Select(d => d.DriveLetter)
			.ToList();
		_options.CleanMode = ZeroFillRadio.IsChecked == true ? CleanMode.ZeroFill : CleanMode.CleanFree;
	}

	private void RefreshCommandPreview()
	{
		try
		{
			SyncOptionsFromUI();
			var errors = _options.Validate();

			var previewText = errors.Count > 0
				? Loc.Get("CommandCannotBeBuilt") + "\n" + string.Join('\n', errors.Select(err => "- " + err))
				: "$ " + _options.BuildCommandLine();

			_outputBuffer.Clear();
			ResetProgressTracking();
			_outputBuffer.AppendLine(previewText);

			OutputTextBox.Text = previewText;
			_outputDirty = false;
		}
		catch (Exception ex)
		{
			var errorText = $"{Loc.Get("CommandBuildInternalError")}\n{ex}";
			_outputBuffer.Clear();
			ResetProgressTracking();
			_outputBuffer.AppendLine(errorText);
			OutputTextBox.Text = errorText;
			_outputDirty = false;
		}
	}

	private async void RunButton_Click(object sender, RoutedEventArgs e)
	{
		if (_isRunning)
		{
			_stopRequestedByUser = true;
			PseudoConsoleProcessRunner.RequestStop();
			return;
		}

		SyncOptionsFromUI();
		var errors = _options.Validate();
		if (errors.Count > 0)
		{
			RefreshCommandPreview();
			return;
		}

		var confirmed = await ShowConfirmationDialogAsync();
		if (!confirmed)
			return;

		_stopRequestedByUser = false;
		StopPathStatsCounting();

		_isRunning = true;
		SleepInhibitor.Prevent();
		UpdateRunButtonVisual();
		SetStatusRunning();
		SetUiEnabled(false);

		try
		{
			await RunSDeleteAsync();
		}
		finally
		{
			SleepInhibitor.Allow();
			_isRunning = false;
			UpdateRunButtonVisual();
			SetUiEnabled(true);
			FlushOutputToUi();
			RecalculatePathStats();
		}
	}

	private void UpdateRunButtonVisual()
	{
		if (_isRunning)
		{
			_originalButtonPointerOverBackground ??= RunButton.Resources.TryGetValue("AccentButtonBackgroundPointerOver", out var po) ? po as Brush : null;
			_originalButtonPressedBackground ??= RunButton.Resources.TryGetValue("AccentButtonBackgroundPressed", out var pr) ? pr as Brush : null;

			RunButton.Background = new SolidColorBrush(Color.FromArgb(255, 196, 43, 28));
			RunButton.Resources["AccentButtonBackgroundPointerOver"] = new SolidColorBrush(Color.FromArgb(255, 165, 34, 22));
			RunButton.Resources["AccentButtonBackgroundPressed"] = new SolidColorBrush(Color.FromArgb(255, 137, 27, 17));

			RunButton.Content = Loc.Get("StopButton");
		}
		else
		{
			RunButton.ClearValue(Button.BackgroundProperty);

			if (_originalButtonPointerOverBackground is not null)
				RunButton.Resources["AccentButtonBackgroundPointerOver"] = _originalButtonPointerOverBackground;
			else
				RunButton.Resources.Remove("AccentButtonBackgroundPointerOver");

			if (_originalButtonPressedBackground is not null)
				RunButton.Resources["AccentButtonBackgroundPressed"] = _originalButtonPressedBackground;
			else
				RunButton.Resources.Remove("AccentButtonBackgroundPressed");

			RunButton.Content = Loc.Get("RunButton");
		}
	}

	private async Task<bool> ShowConfirmationDialogAsync()
	{
		ConfirmDialogText.Text = _options.Mode == SDeleteMode.DeleteFiles
			? Loc.Get("ConfirmDialogDeleteFilesText")
			: Loc.Get("ConfirmDialogCleanFreeSpaceText");

		const int countdownSeconds = 5;
		var remaining = countdownSeconds;

		ConfirmDialog.IsPrimaryButtonEnabled = false;
		ConfirmDialog.PrimaryButtonText = Loc.Format("ConfirmDialogYesCountdown", remaining);

		var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
		timer.Tick += (_, _) =>
		{
			remaining--;
			if (remaining <= 0)
			{
				timer.Stop();
				ConfirmDialog.PrimaryButtonText = Loc.Get("ConfirmDialogYes");
				ConfirmDialog.IsPrimaryButtonEnabled = true;
			}
			else
			{
				ConfirmDialog.PrimaryButtonText = Loc.Format("ConfirmDialogYesCountdown", remaining);
			}
		};
		timer.Start();

		ConfirmDialog.XamlRoot = this.XamlRoot;
		var result = await ConfirmDialog.ShowAsync();

		timer.Stop();

		return result == ContentDialogResult.Primary;
	}

	private void SetUiEnabled(bool enabled)
	{
		MainPivot.IsEnabled = enabled;

		Use64BitCheckBox.IsEnabled = enabled;
		PassesNumberBox.IsEnabled = enabled;
		SdeleteFolderTextBox.IsEnabled = enabled;
		BrowseSdeleteFolderButton.IsEnabled = enabled;
	}

	private static readonly System.Text.RegularExpressions.Regex AnsiEscapeRegex =
		new(@"\x1b\[[0-9;?]*[a-zA-Z]|\x1b\][^\a]*\a",
		System.Text.RegularExpressions.RegexOptions.Compiled);

	private static readonly System.Text.RegularExpressions.Regex PercentLineRegex =
		new(@"^(?<pre>.*?)(?<pct>\d{1,3})%(?<tail>[\p{L}\s]*)$",
			System.Text.RegularExpressions.RegexOptions.Compiled);

	private static readonly System.Text.RegularExpressions.Regex SpinnerLineRegex =
		new(@"^(?<key>.*\.\.\.)[\\|/\-]\s*$",
			System.Text.RegularExpressions.RegexOptions.Compiled);

	private readonly record struct ProgressLine(string Key, int Percent, bool IsSpinner);

	private ProgressLine? _lastProgress;
	private int _lastLineStart = -1;

	private static ProgressLine? ParseProgressLine(string line)
	{
		var percent = PercentLineRegex.Match(line);
		if (percent.Success)
		{
			var key = percent.Groups["pre"].Value + "%" + percent.Groups["tail"].Value;
			return new ProgressLine(key, int.Parse(percent.Groups["pct"].Value), false);
		}

		var spinner = SpinnerLineRegex.Match(line);
		if (spinner.Success)
			return new ProgressLine(spinner.Groups["key"].Value, 0, true);

		return null;
	}

	private void ResetProgressTracking()
	{
		_lastProgress = null;
		_lastLineStart = -1;
	}

	private async Task RunSDeleteAsync()
	{
		var dispatcher = DispatcherQueue.GetForCurrentThread();
		RefreshProcessPath();

		var pending = new StringBuilder();

		void OnOutput(string chunk)
		{
			var cleaned = AnsiEscapeRegex.Replace(chunk, string.Empty);

			if (string.IsNullOrEmpty(cleaned)) return;

			pending.Append(cleaned);
			while (true)
			{
				var text = pending.ToString();
				var newlineIndex = text.IndexOfAny(new[] { '\n', '\r' });
				if (newlineIndex < 0) break;

				var line = text[..newlineIndex];
				pending.Clear();
				pending.Append(text[(newlineIndex + 1)..]);

				if (line.Length == 0) continue;

				dispatcher.TryEnqueue(() => AppendOutputLine(line));
			}
		}

		AppendOutputLine(Loc.Get("RunCommandStarted"));

		try
		{
			var exitCode = await PseudoConsoleProcessRunner.RunAsync(
				_options.ResolveExecutablePath(),
				_options.BuildArguments().ToArray(),
				OnOutput);

			if (pending.Length > 0)
				AppendOutputLine(pending.ToString());

			AppendOutputLine(Loc.Format("RunCommandCompleted", exitCode));

			if (exitCode == 0)
			{
				SetStatusCompleted(exitCode);

				if (_options.Mode == SDeleteMode.DeleteFiles)
					MarkProcessedPathsAsDeleted();
			}
			else if (_stopRequestedByUser)
				SetStatusStopped();
			else
				SetStatusCompleted(exitCode);

			CompletionNotification(exitCode);
		}
		catch (Exception ex)
		{
			AppendOutputLine(Loc.Format("RunCommandError", ex.Message));
			SetStatusStopped();
			CompletionNotification(-1);
		}
	}

	private static void RefreshProcessPath()
	{
		var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
		var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";

		var combined = string.Join(
			Path.PathSeparator,
			new[] { machinePath, userPath }.Where(p => !string.IsNullOrWhiteSpace(p)));

		Environment.SetEnvironmentVariable("PATH", combined, EnvironmentVariableTarget.Process);
	}

	private void AppendOutputLine(string line)
	{
		var progress = ParseProgressLine(line);

		if (progress is { } current && _lastProgress is { } previous
			&& current.IsSpinner == previous.IsSpinner
			&& current.Key == previous.Key
			&& (current.IsSpinner || current.Percent >= previous.Percent))
		{
			_outputBuffer.Length = _lastLineStart;
		}
		else
		{
			if (_lastProgress is { IsSpinner: true } spinner
				&& _lastLineStart >= 0 && _lastLineStart <= _outputBuffer.Length)
			{
				_outputBuffer.Length = _lastLineStart;
				_outputBuffer.AppendLine(spinner.Key);
			}

			_lastLineStart = _outputBuffer.Length;
		}

		_outputBuffer.AppendLine(line);
		_lastProgress = progress;

		if (_outputBuffer.Length > MaxOutputBufferChars)
		{
			var overflow = _outputBuffer.Length - MaxOutputBufferChars;
			_outputBuffer.Remove(0, overflow);
			_lastLineStart = Math.Max(0, _lastLineStart - overflow);
		}

		_outputDirty = true;
	}

	private void FlushOutputToUi()
	{
		if (!_outputDirty)
			return;

		OutputTextBox.Text = _outputBuffer.ToString();

		OutputTextBox.SelectionStart = OutputTextBox.Text.Length;
		OutputTextBox.SelectionLength = 0;

		_outputScrollViewer ??= FindDescendant<ScrollViewer>(OutputTextBox);
		_outputScrollViewer?.ChangeView(null, _outputScrollViewer.ScrollableHeight, null, disableAnimation: true);

		_outputDirty = false;
	}

	private void UpdateSdeletePresenceWarning()
	{
		SyncOptionsFromUI();

		var exePath = _options.ResolveExecutablePath() + ".exe";
		bool found;

		if (Path.IsPathRooted(exePath))
		{
			found = File.Exists(exePath);
		}
		else
		{
			found = IsExecutableInPath(exePath);
		}

		if (found)
		{
			SdeleteNotFoundInfoBar.IsOpen = false;
		}
		else
		{
			SdeleteNotFoundInfoBar.Message = Loc.Get("SdeleteNotFound");
			SdeleteNotFoundInfoBar.IsOpen = true;
		}
	}

	private static bool IsExecutableInPath(string fileName)
	{
		var machinePath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine) ?? "";
		var userPath = Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User) ?? "";
		var path = string.Join(
			Path.PathSeparator,
			new[] { machinePath, userPath }.Where(p => !string.IsNullOrWhiteSpace(p)));

		foreach (var dir in path.Split(Path.PathSeparator))
		{
			if (string.IsNullOrWhiteSpace(dir)) continue;
			try
			{
				var fullPath = Path.Combine(dir.Trim('"'), fileName);
				if (File.Exists(fullPath)) return true;
			}
			catch { }
		}
		return false;
	}

	private void CompletionNotification(int exitCode)
	{
		var elapsed = DateTime.Now - _processStartTime;
		if (elapsed.TotalSeconds < 30)
			return;

		var title = exitCode == 0
			? Loc.Get("NotifyCompletedTitle")
			: _stopRequestedByUser
				? Loc.Get("NotifyStoppedTitle")
				: Loc.Get("NotifyErrorTitle");

		AppNotifier.NotifyCompleted(title, Loc.Format("NotifyBody", elapsed.ToString(@"hh\:mm\:ss")));
	}
}
