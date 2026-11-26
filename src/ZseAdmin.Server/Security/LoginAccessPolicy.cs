using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Realtime;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Jedyne miejsce, które ogranicza widoczność historii logowań (M3).
/// Serwer nadpisuje filtr pracowni z żądania — klient nie może poszerzyć sobie zakresu.
/// </summary>
public interface ILoginAccessPolicy
{
    /// <summary>Zwraca zapytanie z bezpiecznym zakresem albo rzuca <c>forbidden</c>.</summary>
    Task<LoginQueryRequest> ScopeAsync(
        ClientIdentity identity,
        LoginQueryRequest query,
        CancellationToken cancellationToken = default);

    /// <summary>Czy dana sesja może dostać zdarzenie push <c>event.logins.new</c>.</summary>
    bool CanSeeEvent(ClientIdentity identity, LoginEventDto loginEvent);

    /// <summary>Zakres wysyłany w <c>welcome</c> (null = bez ograniczenia).</summary>
    IReadOnlyList<string>? ClassroomScopeFor(ClientIdentity identity);
}

public sealed class LoginAccessPolicy : ILoginAccessPolicy
{
    private readonly ITeacherClassroomAccess _access;
    private readonly IComputerDirectory _computers;
    private readonly IClock _clock;
    private readonly SecurityOptions _security;

    public LoginAccessPolicy(
        ITeacherClassroomAccess access,
        IComputerDirectory computers,
        IClock clock,
        SecurityOptions security)
    {
        _access = access;
        _computers = computers;
        _clock = clock;
        _security = security;
    }

    public async Task<LoginQueryRequest> ScopeAsync(
        ClientIdentity identity,
        LoginQueryRequest query,
        CancellationToken cancellationToken = default)
    {
        if (!AppliesTo(identity))
        {
            return query;
        }

        var active = Active(identity);
        if (active.Count == 0)
        {
            throw new ProtocolException(
                ErrorCode.Forbidden,
                "Nauczyciel nie ma w tym momencie zajęć w żadnej pracowni — historia logowań jest niedostępna.");
        }

        // 1. Jawnie wskazana pracownia.
        if (!string.IsNullOrWhiteSpace(query.ClassroomId))
        {
            if (!Contains(active, query.ClassroomId))
            {
                throw Forbidden(query.ClassroomId);
            }

            return query with { ClassroomIds = new[] { query.ClassroomId.Trim() } };
        }

        // 2. Filtr po komputerze: rozwiąż go na pracownię w katalogu, nie ufaj clientowi.
        if (!string.IsNullOrWhiteSpace(query.ComputerName))
        {
            var computers = await _computers.ListAsync(null, cancellationToken).ConfigureAwait(false);
            var computer = computers.Items.FirstOrDefault(c =>
                string.Equals(c.Name, query.ComputerName, StringComparison.OrdinalIgnoreCase));

            if (computer?.ClassroomId is null || !Contains(active, computer.ClassroomId))
            {
                throw new ProtocolException(
                    ErrorCode.Forbidden,
                    $"Nauczyciel nie ma zajęć w pracowni komputera '{query.ComputerName}'.");
            }

            return query with { ClassroomIds = new[] { computer.ClassroomId } };
        }

        // 3. Zapytanie bez filtra albo z filtrem po pracowniach: przetnij z aktywnym zakresem.
        var requested = query.ClassroomIds is { Count: > 0 } ids
            ? ids
            : (IReadOnlyList<string>)active;

        var allowed = requested
            .Where(id => Contains(active, id))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (allowed.Count == 0)
        {
            throw Forbidden(string.Join(", ", requested));
        }

        return query with { ClassroomIds = allowed };
    }

    public bool CanSeeEvent(ClientIdentity identity, LoginEventDto loginEvent)
    {
        if (!AppliesTo(identity))
        {
            return true;
        }

        return loginEvent.ClassroomId is { Length: > 0 } classroomId &&
               Contains(Active(identity), classroomId);
    }

    public IReadOnlyList<string>? ClassroomScopeFor(ClientIdentity identity) =>
        AppliesTo(identity) ? Active(identity) : null;

    private bool AppliesTo(ClientIdentity identity) =>
        _security.EnforceTeacherClassroomScope &&
        string.Equals(identity.Role, Roles.Teacher, StringComparison.OrdinalIgnoreCase);

    private IReadOnlyList<string> Active(ClientIdentity identity) =>
        _access.ActiveClassrooms(identity.Name, _clock.UtcNow);

    private static bool Contains(IReadOnlyList<string> active, string classroomId) =>
        active.Any(c => string.Equals(c, classroomId.Trim(), StringComparison.OrdinalIgnoreCase));

    private static ProtocolException Forbidden(string classroomId) => new(
        ErrorCode.Forbidden,
        $"Nauczyciel nie ma w tym momencie zajęć w pracowni '{classroomId}' — dostęp do historii zabroniony.");
}
