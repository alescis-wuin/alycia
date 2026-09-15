using System.Diagnostics;
using System.Text;

namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal sealed record ProcessCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError)
{
    public string CombinedOutput => string.Join(
        Environment.NewLine,
        new[] { StandardOutput, StandardError }
            .Where(value => !string.IsNullOrWhiteSpace(value)));
}

internal static class ProcessCommandRunner
{
    public static async Task<ProcessCommandResult> RunAsync(
        string executable,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        IReadOnlyDictionary<string, string?>? environment,
        CancellationToken cancellationToken,
        Action<string>? outputLine = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(executable);
        ArgumentNullException.ThrowIfNull(arguments);

        ProcessStartInfo startInfo = new(executable)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        if (!string.IsNullOrWhiteSpace(workingDirectory))
        {
            startInfo.WorkingDirectory = workingDirectory;
        }

        foreach (string argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        if (environment is not null)
        {
            foreach ((string name, string? value) in environment)
            {
                if (value is null)
                {
                    startInfo.Environment.Remove(name);
                }
                else
                {
                    startInfo.Environment[name] = value;
                }
            }
        }

        using Process process = new() { StartInfo = startInfo };

        if (!process.Start())
        {
            throw new InvalidOperationException($"Unable to start '{executable}'.");
        }

        Task<string> standardOutputTask = ReadOutputAsync(
            process.StandardOutput,
            outputLine,
            cancellationToken);
        Task<string> standardErrorTask = ReadOutputAsync(
            process.StandardError,
            outputLine,
            cancellationToken);

        using CancellationTokenRegistration registration = cancellationToken.Register(
            static state =>
            {
                Process runningProcess = (Process)state!;

                try
                {
                    if (!runningProcess.HasExited)
                    {
                        runningProcess.Kill(entireProcessTree: true);
                    }
                }
                catch (InvalidOperationException)
                {
                }
            },
            process);

        await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        string standardOutput = await standardOutputTask.ConfigureAwait(false);
        string standardError = await standardErrorTask.ConfigureAwait(false);

        return new ProcessCommandResult(
            process.ExitCode,
            standardOutput,
            standardError);
    }

    private static async Task<string> ReadOutputAsync(
        StreamReader reader,
        Action<string>? outputLine,
        CancellationToken cancellationToken)
    {
        StringBuilder output = new();

        while (true)
        {
            string? line = await reader
                .ReadLineAsync(cancellationToken)
                .ConfigureAwait(false);

            if (line is null)
            {
                break;
            }

            if (output.Length > 0)
            {
                output.AppendLine();
            }

            output.Append(line);
            outputLine?.Invoke(line);
        }

        return output.ToString().Trim();
    }
}
