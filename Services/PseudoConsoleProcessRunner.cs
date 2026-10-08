using System.Text;
using Porta.Pty;

namespace SDeleteGUI.Services;

/// <summary>
/// Launches a process in a pseudo console and captures its
/// output.
/// </summary>
internal static class PseudoConsoleProcessRunner
{
	private static IPtyConnection? _currentTerminal;

	public static async Task<int> RunAsync(
		string exePath,
		string[] arguments,
		Action<string> onOutput,
		CancellationToken cancellationToken = default)
	{
		var options = new PtyOptions
		{
			Name = "SDeletePTY",
			Cols = 120,
			Rows = 32,
			Cwd = Environment.CurrentDirectory,
			App = exePath,
			CommandLine = arguments,
			Environment = Environment.GetEnvironmentVariables()
				.Cast<System.Collections.DictionaryEntry>()
				.Where(e => e.Value is not null)
				.ToDictionary(e => (string)e.Key, e => (string)e.Value!)
		};

		using IPtyConnection terminal = await PtyProvider
			.SpawnAsync(options, cancellationToken)
			.ConfigureAwait(false);

		_currentTerminal = terminal;

		try {
			using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

			var exitTcs = new TaskCompletionSource<int>(
				TaskCreationOptions.RunContinuationsAsynchronously);

			terminal.ProcessExited += (_, e) => exitTcs.TrySetResult(e.ExitCode);

			long chunkCounter = 0;

			var readTask = Task.Run(async () =>
			{
				var buffer = new byte[1024];

				var decoder = Encoding.UTF8.GetDecoder();
				var chars = new char[Encoding.UTF8.GetMaxCharCount(buffer.Length)];

				try
				{
					while (!readCts.IsCancellationRequested)
					{
						int read = await terminal.ReaderStream
							.ReadAsync(buffer, readCts.Token)
							.ConfigureAwait(false);

						if (read == 0) break;

						Interlocked.Increment(ref chunkCounter);

						var charCount = decoder.GetChars(buffer, 0, read, chars, 0);
						if (charCount > 0)
							onOutput(new string(chars, 0, charCount));
					}
				}
				catch (OperationCanceledException)
				{
				}
				catch (IOException)
				{
				}
				catch (ObjectDisposedException)
				{
				}
			});

			int exitCode = await exitTcs.Task
				.WaitAsync(cancellationToken)
				.ConfigureAwait(false);

			while (!readTask.IsCompleted)
			{
				var before = Interlocked.Read(ref chunkCounter);
				var finished = await Task.WhenAny(readTask, Task.Delay(300)).ConfigureAwait(false);

				if (finished == readTask || Interlocked.Read(ref chunkCounter) == before)
					break;
			}

			readCts.Cancel();

			try
			{
				await readTask.WaitAsync(TimeSpan.FromSeconds(2), CancellationToken.None)
							.ConfigureAwait(false);
			}
			catch (TimeoutException)
			{
			}

			return exitCode;
		}
		finally
		{
			_currentTerminal = null;
		}
	}

	public static void RequestStop()
	{
		var terminal = _currentTerminal;
		if (terminal is null)
			return;

		try
		{
			terminal.Kill();
		}
		catch
		{
		}
	}
}
