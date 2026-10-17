using System.Diagnostics;
using System.IO.Enumeration;

namespace SDeleteGUI.Services;

internal readonly record struct PathStats(long Files, long Folders, long Bytes);

internal static class PathStatsCalculator
{
	private static readonly TimeSpan ReportInterval = TimeSpan.FromMilliseconds(150);

	public static PathStats Calculate(
		IReadOnlyList<(string Path, bool IsDirectory)> entries,
		IProgress<PathStats> progress,
		CancellationToken cancellationToken)
	{
		long files = 0, folders = 0, bytes = 0;

		var clock = Stopwatch.StartNew();
		var nextReport = ReportInterval;
		var tick = 0;

		void ReportIfDue()
		{
			if ((++tick & 0xFF) != 0)
				return;

			cancellationToken.ThrowIfCancellationRequested();

			if (clock.Elapsed < nextReport)
				return;

			nextReport = clock.Elapsed + ReportInterval;
			progress.Report(new PathStats(files, folders, bytes));
		}

		var options = new EnumerationOptions
		{
			RecurseSubdirectories = true,
			IgnoreInaccessible = true,
			AttributesToSkip = 0,
			ReturnSpecialDirectories = false
		};

		foreach (var (path, isDirectory) in entries)
		{
			cancellationToken.ThrowIfCancellationRequested();

			try
			{
				if (!isDirectory)
				{
					var info = new FileInfo(path);
					if (info.Exists)
					{
						files++;
						bytes += info.Length;
					}

					ReportIfDue();
					continue;
				}

				if (!Directory.Exists(path))
					continue;

				folders++;

				var enumerable = new FileSystemEnumerable<(bool IsDirectory, long Length)>(
					path,
					static (ref FileSystemEntry e) => (e.IsDirectory, e.IsDirectory ? 0L : e.Length),
					options)
				{
					ShouldRecursePredicate = static (ref FileSystemEntry e) =>
						(e.Attributes & FileAttributes.ReparsePoint) == 0
				};

				foreach (var (entryIsDirectory, length) in enumerable)
				{
					if (entryIsDirectory)
					{
						folders++;
					}
					else
					{
						files++;
						bytes += length;
					}

					ReportIfDue();
				}
			}
			catch (IOException)
			{
			}
			catch (UnauthorizedAccessException)
			{
			}
		}

		return new PathStats(files, folders, bytes);
	}
}
