using System.Diagnostics;
using System.Globalization;
using PokeTokenBar.Core;

namespace PokeTokenBar.Platform.Windows;

public interface IHiggsfieldCliClient
{
    Task<string> RunAsync(IReadOnlyList<string> arguments, CancellationToken cancellationToken);
}

public sealed class HiggsfieldUsageProvider : IUsageProvider
{
    private const int PageSize = 100;
    private const int MaxPages = 1_000;
    private readonly IHiggsfieldCliClient? _client;

    public HiggsfieldUsageProvider(IHiggsfieldCliClient? client = null)
    {
        _client = client ?? HiggsfieldCliClient.TryCreate();
    }

    public string Id => "higgsfield";

    public string DisplayName => "Higgsfield";

    public bool ReportsCost => false;

    public async Task<ProviderSnapshot?> FetchAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        if (_client is null)
        {
            return null;
        }

        var statusJson = await _client.RunAsync(
            ["account", "status", "--json", "--no-color"],
            cancellationToken).ConfigureAwait(false);
        var status = HiggsfieldUsage.ParseStatus(statusJson);
        var transactions = new List<HiggsfieldTransaction>();
        var transactionIds = new HashSet<string>(StringComparer.Ordinal);
        string? cursor = null;
        var seenCursors = new HashSet<string>(StringComparer.Ordinal);
        for (var pageNumber = 0; pageNumber < MaxPages; pageNumber++)
        {
            var arguments = new List<string>
            {
                "account", "transactions", "--size", PageSize.ToString(CultureInfo.InvariantCulture),
                "--json", "--no-color",
            };
            if (!string.IsNullOrWhiteSpace(cursor))
            {
                arguments.Add("--cursor");
                arguments.Add(cursor);
            }

            var json = await _client.RunAsync(arguments, cancellationToken).ConfigureAwait(false);
            var page = HiggsfieldUsage.ParseTransactions(json);
            foreach (var transaction in page.Items)
            {
                if (transaction.Id is null || transactionIds.Add(transaction.Id))
                {
                    transactions.Add(transaction);
                }
            }

            if (string.IsNullOrWhiteSpace(page.Cursor))
            {
                return HiggsfieldUsage.CreateSnapshot(
                    transactions,
                    status,
                    now,
                    CultureInfo.CurrentCulture.DateTimeFormat.FirstDayOfWeek);
            }

            if (!seenCursors.Add(page.Cursor))
            {
                throw new InvalidDataException("Higgsfield returned a repeated transaction cursor.");
            }

            cursor = page.Cursor;
        }

        throw new InvalidDataException("Higgsfield transaction history exceeded the safe pagination limit.");
    }
}

public sealed class HiggsfieldCliClient : IHiggsfieldCliClient
{
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromSeconds(20);
    private readonly string _binary;

    public HiggsfieldCliClient(string binary)
    {
        _binary = Path.GetFullPath(binary);
    }

    public static HiggsfieldCliClient? TryCreate()
    {
        var configured = Environment.GetEnvironmentVariable("PTB_HIGGSFIELD_CLI");
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
        {
            return new HiggsfieldCliClient(configured);
        }

        var candidates = new List<string>();
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        if (!string.IsNullOrWhiteSpace(appData))
        {
            candidates.Add(Path.Combine(
                appData,
                "npm",
                "node_modules",
                "@higgsfield",
                "cli",
                "vendor",
                "hf.exe"));
        }

        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (!string.IsNullOrWhiteSpace(localAppData))
        {
            candidates.Add(Path.Combine(localAppData, "Higgsfield", "bin", "higgsfield.exe"));
        }

        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty)
                     .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            candidates.Add(Path.Combine(directory.Trim('"'), "higgsfield.exe"));
        }

        var binary = candidates.FirstOrDefault(File.Exists);
        return binary is null ? null : new HiggsfieldCliClient(binary);
    }

    public async Task<string> RunAsync(
        IReadOnlyList<string> arguments,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = _binary,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Higgsfield CLI could not be started.");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(CommandTimeout);
        try
        {
            var outputTask = process.StandardOutput.ReadToEndAsync(timeout.Token);
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            var output = await outputTask.ConfigureAwait(false);
            var error = await errorTask.ConfigureAwait(false);
            if (process.ExitCode != 0)
            {
                throw new InvalidOperationException(
                    string.IsNullOrWhiteSpace(error)
                        ? "Higgsfield CLI request failed. Sign in with `higgsfield auth login`."
                        : $"Higgsfield CLI request failed: {SingleLine(error)}");
            }

            return output;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            TryKill(process);
            throw new TimeoutException("Higgsfield CLI did not respond within 20 seconds.");
        }
    }

    private static string SingleLine(string value)
    {
        var singleLine = string.Join(" ", value
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries));
        return singleLine.Length <= 300 ? singleLine : singleLine[..300];
    }

    private static void TryKill(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // The process exited between the state check and Kill.
        }
    }
}
