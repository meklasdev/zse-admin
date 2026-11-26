using ZseAdmin.Contracts;
using ZseAdmin.Server.Abstractions;
using ZseAdmin.Server.Configuration;
using ZseAdmin.Server.Infrastructure.Classrooms;

namespace ZseAdmin.Server.Infrastructure.Ldap;

/// <summary>
/// Komputery z AD + stan z <see cref="IComputerStatusProbe"/>. Źródło prawdy o istnieniu
/// komputera to AD; stan sieciowy to obserwacja z konkretnego momentu. Rozdzielenie tych
/// dwóch warstw jest kryterium M1 — komputer offline nie znika z listy.
/// </summary>
public sealed class AdComputerDirectory : IComputerDirectory
{
    /// <summary>
    /// Stan jest odświeżany nie częściej niż raz na ten interwał. Bez tego dwie sesje klienta
    /// (i cykl monitorowania) pingowałyby wszystkie stacje niezależnie od siebie.
    /// </summary>
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(5);

    // Kolejka sprawdzeń: dwie sesje nie pingują tej samej sieci jednocześnie.
    private readonly SemaphoreSlim _refreshGate = new(1, 1);

    private readonly IAdInventoryReader _inventory;
    private readonly IComputerStatusProbe _probe;
    private readonly IClock _clock;
    private readonly StatusProbeOptions _probeOptions;
    private readonly object _sync = new();
    private readonly Dictionary<string, ComputerDto> _lastKnown = new(StringComparer.OrdinalIgnoreCase);

    private IReadOnlyList<ComputerDto>? _snapshot;
    private DateTimeOffset _snapshotAt;

    public AdComputerDirectory(
        IAdInventoryReader inventory,
        IComputerStatusProbe probe,
        IClock clock,
        StatusProbeOptions probeOptions)
    {
        _inventory = inventory;
        _probe = probe;
        _clock = clock;
        _probeOptions = probeOptions;
    }

    public async Task<ComputerQueryResult> ListAsync(
        string? classroomId,
        CancellationToken cancellationToken = default)
    {
        var inventory = await _inventory.LoadAsync(cancellationToken).ConfigureAwait(false);
        var withStatus = await GetSnapshotAsync(inventory, cancellationToken).ConfigureAwait(false);

        IEnumerable<ComputerDto> query = withStatus;

        if (!string.IsNullOrWhiteSpace(classroomId))
        {
            query = query.Where(c => string.Equals(c.ClassroomId, classroomId, StringComparison.OrdinalIgnoreCase));
        }

        var items = query
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        var observedAt = items
            .Where(c => c.StatusObservedAtUtc.HasValue)
            .Select(c => c.StatusObservedAtUtc)
            .DefaultIfEmpty()
            .Max();

        QueryMetadata metadata;
        if (observedAt is not { } lastObservation)
        {
            // Żadnej stacji nie udało się sprawdzić — to brak danych, nie „wszystko offline”.
            metadata = QueryMetadata.Unavailable(
                "Nie sprawdzono jeszcze stanu żadnej stacji (brak odpowiedzi lub brak adresu).");
        }
        else if (inventory.Truncated)
        {
            metadata = QueryMetadata.Partial(
                lastObservation,
                $"Wyczytano {inventory.Computers.Count} z {inventory.TotalComputers} komputerów — podnieś ZseAdmin:Directory:MaxComputers.");
        }
        else
        {
            metadata = new QueryMetadata(lastObservation, DataCompleteness.Complete, null);
        }

        return new ComputerQueryResult(items, metadata);
    }

    /// <summary>
    /// Sprawdza stan wszystkich stacji z AD i zwraca tylko te, których stan się zmienił
    /// względem poprzedniej obserwacji. Brak adresu IP lub brak odpowiedzi z progu
    /// nie są zamieniane na fałszywe „offline”.
    /// </summary>
    public async Task<IReadOnlyList<ComputerDto>> RefreshStatusesAsync(
        CancellationToken cancellationToken = default)
    {
        var inventory = await _inventory.LoadAsync(cancellationToken).ConfigureAwait(false);
        var current = await GetSnapshotAsync(inventory, cancellationToken, force: false).ConfigureAwait(false);

        var changed = new List<ComputerDto>();

        lock (_sync)
        {
            foreach (var computer in current)
            {
                if (!_lastKnown.TryGetValue(computer.Name, out var previous) ||
                    previous.Status != computer.Status ||
                    previous.StatusObservedAtUtc != computer.StatusObservedAtUtc)
                {
                    changed.Add(computer);
                }

                _lastKnown[computer.Name] = computer;
            }
        }

        return changed;
    }

    /// <summary>
    /// Zwraca spójny obraz komputerów: albo z cache (świeży), albo po nowym sprawdzeniu stanu.
    /// Migawka jest budowana raz na <see cref="RefreshInterval"/>, a nie przy każdym żądaniu.
    /// </summary>
    private async Task<IReadOnlyList<ComputerDto>> GetSnapshotAsync(
        AdInventory inventory,
        CancellationToken cancellationToken,
        bool force = false)
    {
        lock (_sync)
        {
            if (!force && _snapshot is not null && _clock.UtcNow - _snapshotAt < RefreshInterval)
            {
                return _snapshot;
            }
        }

        await _refreshGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            // Podwójne sprawdzenie: inna sesja mogła odświeżyć stan, gdy czekaliśmy.
            lock (_sync)
            {
                if (!force && _snapshot is not null && _clock.UtcNow - _snapshotAt < RefreshInterval)
                {
                    return _snapshot;
                }
            }

            var refreshed = await RefreshAsync(inventory, cancellationToken).ConfigureAwait(false);

            lock (_sync)
            {
                _snapshot = refreshed;
                _snapshotAt = _clock.UtcNow;
            }

            return refreshed;
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<IReadOnlyList<ComputerDto>> RefreshAsync(
        AdInventory inventory,
        CancellationToken cancellationToken)
    {
        if (inventory.Computers.Count == 0)
        {
            return Array.Empty<ComputerDto>();
        }

        var hosts = inventory.Computers
            .Select(c => ResolveHost(c))
            .Where(h => !string.IsNullOrWhiteSpace(h))
            .Select(h => h!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

        var budget = TimeSpan.FromMilliseconds(Math.Max(0, _probeOptions.BudgetMilliseconds));
        var probes = await _probe.ProbeAsync(hosts, budget, cancellationToken).ConfigureAwait(false);
        var now = _clock.UtcNow;
        var result = new List<ComputerDto>(inventory.Computers.Count);

        foreach (var computer in inventory.Computers)
        {
            var host = ResolveHost(computer);

            if (string.IsNullOrWhiteSpace(host))
            {
                // Nie ma czego pingować — to jest „brak danych”, nie „offline”.
                result.Add(ToDto(computer, _lastKnown.GetValueOrDefault(computer.Name)));
                continue;
            }

            if (!probes.TryGetValue(host, out var reachable))
            {
                result.Add(ToDto(computer, _lastKnown.GetValueOrDefault(computer.Name)));
                continue;
            }

            // Źródło IP to na razie AD/dNSHostName, więc adres może się zmienić — nie udajemy,
            // że jest bieżący, i przy nieudanym sprawdzeniu zachowujemy ostatnio znany.
            var ipAddress = reachable ? computer.IpAddress : _lastKnown.GetValueOrDefault(computer.Name)?.IpAddress;

            result.Add(new ComputerDto(
                Name: computer.Name,
                ClassroomId: computer.ClassroomId,
                ClassroomName: computer.ClassroomName,
                OuDistinguishedName: computer.DistinguishedName,
                IpAddress: ipAddress,
                Status: reachable ? ComputerStatus.Online : ComputerStatus.Offline,
                StatusObservedAtUtc: now,
                LastLogonAtUtc: computer.LastLogonAtUtc,
                OperatingSystem: computer.OperatingSystem));
        }

        return result;
    }

    /// <summary>Komputer bez pracowni ma <c>classroomId = null</c> — UI pokazuje „poza pracownią”.</summary>
    private ComputerDto ToDto(AdComputer computer, ComputerDto? previous) => new(
        Name: computer.Name,
        ClassroomId: computer.ClassroomId,
        ClassroomName: computer.ClassroomName,
        OuDistinguishedName: computer.DistinguishedName,
        IpAddress: computer.IpAddress ?? previous?.IpAddress,
        Status: previous?.Status ?? ComputerStatus.Unknown,
        StatusObservedAtUtc: previous?.StatusObservedAtUtc,
        LastLogonAtUtc: computer.LastLogonAtUtc,
        OperatingSystem: computer.OperatingSystem);

    private static string? ResolveHost(AdComputer computer)
    {
        if (!string.IsNullOrWhiteSpace(computer.IpAddress))
        {
            return computer.IpAddress;
        }

        return string.IsNullOrWhiteSpace(computer.Name) ? null : computer.Name;
    }
}
