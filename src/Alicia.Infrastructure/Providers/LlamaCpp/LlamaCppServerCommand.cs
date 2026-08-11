namespace Alicia.Infrastructure.Providers.LlamaCpp;

internal static class LlamaCppServerCommand
{
    public const int DefaultPort = 8080;

    public const string ModelAlias = "alicia-local";

    public static IReadOnlyList<string> CreateArguments(
        string modelReference,
        string logFilePath,
        int port = DefaultPort)
    {
        string normalizedModelReference = LlamaCppModelReference.Normalize(modelReference);

        if (port is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(port));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(logFilePath);

        return
        [
            "-hf",
            normalizedModelReference,
            "--host",
            "127.0.0.1",
            "--port",
            port.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--n-gpu-layers",
            "auto",
            "--alias",
            ModelAlias,
            "--jinja",
            "--no-mmproj",
            "--log-file",
            Path.GetFullPath(logFilePath),
            "--log-colors",
            "off",
        ];
    }
}
