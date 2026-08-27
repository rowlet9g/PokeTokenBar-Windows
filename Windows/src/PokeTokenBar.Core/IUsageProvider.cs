namespace PokeTokenBar.Core;

public interface IUsageProvider
{
    string Id { get; }

    string DisplayName { get; }

    bool ReportsCost { get; }

    Task<ProviderSnapshot?> FetchAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken = default);
}
