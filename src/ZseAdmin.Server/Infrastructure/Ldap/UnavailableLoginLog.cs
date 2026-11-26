using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;

namespace ZseAdmin.Server.Infrastructure.Ldap;

/// <summary>
/// Historia logowań jest etapem M2. Do tego czasu serwer w trybie produkcyjnym zwraca
/// <c>unavailable</c> z powodem — pusta lista sugerowałaby, że nikt się nie logował.
/// </summary>
public sealed class UnavailableLoginLog : ILoginLog
{
    public DateTimeOffset? RetentionStartUtc => null;

    public Task<LoginQueryResult> QueryAsync(
        LoginQueryRequest query,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(new LoginQueryResult(
            Array.Empty<Contracts.LoginEventDto>(),
            0,
            QueryMetadata.Unavailable("Historia logowań nie jest jeszcze podłączona (etap M2).")));
}
