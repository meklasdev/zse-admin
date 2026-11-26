using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Infrastructure.Classrooms;

namespace ZseAdmin.Server.Infrastructure.Ldap;

/// <summary>
/// Pracownie pochodzą z konfiguracji (<c>classrooms.json</c>), a licznik komputerów — z AD.
/// Dzięki temu pracownia istnieje nawet wtedy, gdy w jej OU nie ma jeszcze żadnego komputera.
/// </summary>
public sealed class AdClassroomDirectory : IClassroomDirectory
{
    private readonly ClassroomMap _classrooms;
    private readonly IAdInventoryReader _inventory;
    private readonly IClock _clock;

    public AdClassroomDirectory(ClassroomMap classrooms, IAdInventoryReader inventory, IClock clock)
    {
        _classrooms = classrooms;
        _inventory = inventory;
        _clock = clock;
    }

    public async Task<ClassroomQueryResult> ListAsync(CancellationToken cancellationToken = default)
    {
        var inventory = await _inventory.LoadAsync(cancellationToken).ConfigureAwait(false);

        var counts = inventory.Computers
            .Where(c => c.ClassroomId is not null)
            .GroupBy(c => c.ClassroomId!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);

        var items = _classrooms.Classrooms
            .Select(c => new ClassroomDto(
                c.Id,
                c.Name,
                _classrooms.PrimaryOu(c.Id),
                counts.GetValueOrDefault(c.Id)))
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var metadata = inventory.Truncated
            ? QueryMetadata.Partial(
                _clock.UtcNow,
                "Liczba komputerów policzona na części inwentarza AD (limit MaxComputers).")
            : QueryMetadata.Complete(_clock.UtcNow);

        return new ClassroomQueryResult(items, metadata);
    }
}
