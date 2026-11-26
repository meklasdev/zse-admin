using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Mock;

/// <summary>Konta AD z danych przykładowych.</summary>
public sealed class MockAdDirectory : IAdDirectory
{
    private readonly IClock _clock;
    private readonly Dictionary<string, AdUserDto> _users;

    public MockAdDirectory(IClock clock)
    {
        _clock = clock;
        _users = SchoolSeed.Users()
            .ToDictionary(u => u.SamAccountName, StringComparer.OrdinalIgnoreCase);
    }

    public Task<AdUserQueryResult> FindUserAsync(string samAccountName, CancellationToken cancellationToken = default)
    {
        _users.TryGetValue(samAccountName, out var user);

        return Task.FromResult(new AdUserQueryResult(
            user,
            QueryMetadata.Complete(_clock.UtcNow.AddSeconds(-30))));
    }
}
