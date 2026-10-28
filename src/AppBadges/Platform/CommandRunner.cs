using System.Diagnostics;

namespace AppBadges.Platform;

public sealed record CommandResult(int ExitCode, string Output, byte[] OutputBytes);

/// <summary>Runs a system tool. A seam so the parsing around it is testable without macOS.</summary>
public interface ICommandRunner
{
	Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout,
		CancellationToken cancellationToken);
}

public sealed class ProcessCommandRunner : ICommandRunner
{
	// The tools used print a few kilobytes; anything far larger means something is wrong.
	private const int MaxOutputBytes = 8 * 1024 * 1024;

	public async Task<CommandResult> RunAsync(string executable, IReadOnlyList<string> arguments, TimeSpan timeout,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(arguments);

		// An argument list, never a shell command line, so nothing from widget data is interpreted by a shell.
		var startInfo = new ProcessStartInfo(executable)
		{
			RedirectStandardOutput = true,
			RedirectStandardError = true,
			UseShellExecute = false,
			CreateNoWindow = true
		};

		foreach (var argument in arguments)
		{
			startInfo.ArgumentList.Add(argument);
		}

		using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
		timeoutSource.CancelAfter(timeout);
		using var process = Process.Start(startInfo)
			?? throw new InvalidOperationException($"Could not start {executable}");

		try
		{
			using var output = new MemoryStream();
			using var error = new MemoryStream();
			var copy = CopyCappedAsync(process.StandardOutput.BaseStream, output, timeoutSource);
			var drainError = CopyCappedAsync(process.StandardError.BaseStream, error, timeoutSource);
			await Task.WhenAll(copy, drainError, process.WaitForExitAsync(timeoutSource.Token)).ConfigureAwait(false);

			var bytes = output.ToArray();
			return new CommandResult(process.ExitCode, System.Text.Encoding.UTF8.GetString(bytes), bytes);
		}
		finally
		{
			// Whatever went wrong, a tool that is still running is not left behind.
			if (!process.HasExited)
			{
				process.Kill(entireProcessTree: true);
			}
		}
	}

	private static async Task CopyCappedAsync(Stream source, MemoryStream target, CancellationTokenSource stop)
	{
		var buffer = new byte[81920];
		int read;

		while ((read = await source.ReadAsync(buffer, stop.Token).ConfigureAwait(false)) > 0)
		{
			if (target.Length + read > MaxOutputBytes)
			{
				// Stops the wait for exit too, instead of letting it run into the full timeout.
				await stop.CancelAsync().ConfigureAwait(false);
				throw new InvalidDataException("The tool printed more output than expected.");
			}

			await target.WriteAsync(buffer.AsMemory(0, read), stop.Token).ConfigureAwait(false);
		}
	}
}
