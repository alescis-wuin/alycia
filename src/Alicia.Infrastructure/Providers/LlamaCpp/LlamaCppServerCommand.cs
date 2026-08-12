namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal static class LlamaCppServerCommand
{
    public const int DefaultPort = 8080;

    public const string ModelAlias = "alicia-local";

    public static string[] CreateArguments(
        string modelReference,
        string logFilePath,
        int? contextSize = null,
        int port = DefaultPort)
    {
        string normalizedModelReference = LlamaCppModelReference.Normalize(modelReference);

        if (contextSize is <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(contextSize));
        }

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

        List<string> arguments =
        [
            "-hf",
            normalizedModelReference,
            "--host",
            "127.0.0.1",
            "--port",
            port.ToString(System.Globalization.CultureInfo.InvariantCulture),
        ];

        if (contextSize is int configuredContextSize)
        {
            arguments.Add("--ctx-size");
            arguments.Add(configuredContextSize.ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        arguments.AddRange(
        [
            "--alias",
            ModelAlias,
            "--jinja",
            "--no-mmproj",
            "--log-file",
            Path.GetFullPath(logFilePath),
            "--log-colors",
            "off",
        ]);

        return arguments.ToArray();
    }
}
