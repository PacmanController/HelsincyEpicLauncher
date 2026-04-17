using System.Buffers;
using System.IO.Pipes;
using System.Text;

namespace Launcher.App;

internal static class SingleInstanceCoordinator
{
    private const string MutexName = "HelsincyEpicLauncher_SingleInstance";
    internal const string PipeName = "HelsincyEpicLauncher_Pipe";
    private static readonly SearchValues<char> CommandLineArgumentSeparators = SearchValues.Create(" \t");

    private static Mutex? _mutex;

    internal static string ResolveProcessLaunchArguments(string[] startupArgs)
    {
        if (startupArgs.Length > 0)
        {
            return string.Join(" ", startupArgs);
        }

        if (TryExtractArgumentsFromRawCommandLine(Environment.CommandLine, out var rawCommandLineArguments))
        {
            return rawCommandLineArguments;
        }

        var commandLineArgs = Environment.GetCommandLineArgs();
        if (commandLineArgs.Length <= 1)
        {
            return string.Empty;
        }

        return string.Join(" ", commandLineArgs, 1, commandLineArgs.Length - 1);
    }

    internal static bool EnsureSingleInstance(string? launchArguments)
    {
        _mutex = new Mutex(true, MutexName, out bool isNew);
        if (!isNew)
        {
            NotifyExistingInstance(launchArguments);
            return false;
        }

        return true;
    }

    internal static bool TryParsePipeMessage(string? message, out string payload)
    {
        payload = string.Empty;

        if (string.IsNullOrWhiteSpace(message)
            || !message.StartsWith("AUTH_CALLBACK|", StringComparison.Ordinal))
        {
            return false;
        }

        var encodedPayload = message["AUTH_CALLBACK|".Length..];
        if (string.IsNullOrWhiteSpace(encodedPayload))
        {
            return false;
        }

        try
        {
            payload = Encoding.UTF8.GetString(Convert.FromBase64String(encodedPayload));
            return !string.IsNullOrWhiteSpace(payload);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    internal static bool TryExtractAuthCallbackPayload(string? candidate, out string payload)
    {
        payload = string.Empty;
        if (string.IsNullOrWhiteSpace(candidate))
        {
            return false;
        }

        var trimmed = candidate.Trim();
        if (trimmed.Length >= 2 && trimmed[0] == '"' && trimmed[^1] == '"')
        {
            trimmed = trimmed[1..^1].Trim();
        }

        if (string.IsNullOrWhiteSpace(trimmed))
        {
            return false;
        }

        var looksLikeCallbackUrl = trimmed.Contains("://", StringComparison.Ordinal)
            || trimmed.Contains("code=", StringComparison.OrdinalIgnoreCase);

        if (!looksLikeCallbackUrl)
        {
            return false;
        }

        payload = trimmed;
        return true;
    }

    internal static bool TryExtractArgumentsFromRawCommandLine(string rawCommandLine, out string arguments)
    {
        arguments = string.Empty;
        if (string.IsNullOrWhiteSpace(rawCommandLine))
        {
            return false;
        }

        var trimmedCommandLine = rawCommandLine.TrimStart();
        if (trimmedCommandLine.Length == 0)
        {
            return false;
        }

        var argumentStartIndex = 0;
        if (trimmedCommandLine[0] == '"')
        {
            var closingQuoteIndex = trimmedCommandLine.IndexOf('"', 1);
            if (closingQuoteIndex < 0 || closingQuoteIndex >= trimmedCommandLine.Length - 1)
            {
                return false;
            }

            argumentStartIndex = closingQuoteIndex + 1;
        }
        else
        {
            var executablePathEndIndex = trimmedCommandLine.AsSpan().IndexOfAny(CommandLineArgumentSeparators);
            if (executablePathEndIndex < 0 || executablePathEndIndex >= trimmedCommandLine.Length - 1)
            {
                return false;
            }

            argumentStartIndex = executablePathEndIndex + 1;
        }

        arguments = trimmedCommandLine[argumentStartIndex..].Trim();
        return !string.IsNullOrWhiteSpace(arguments);
    }

    private static void NotifyExistingInstance(string? launchArguments)
    {
        try
        {
            using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
            client.Connect(timeout: 3000);
            using var writer = new StreamWriter(client);
            writer.WriteLine(BuildPipeMessage(launchArguments));
            writer.Flush();
        }
        catch
        {
        }
    }

    private static string BuildPipeMessage(string? launchArguments)
    {
        if (!TryExtractAuthCallbackPayload(launchArguments, out var payload))
        {
            return "ACTIVATE";
        }

        var encodedPayload = Convert.ToBase64String(Encoding.UTF8.GetBytes(payload));
        return $"AUTH_CALLBACK|{encodedPayload}";
    }
}