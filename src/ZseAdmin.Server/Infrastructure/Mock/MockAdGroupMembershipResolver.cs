using ZseAdmin.Server.Security;

namespace ZseAdmin.Server.Infrastructure.Mock;

/// <summary>
/// Grupy AD z danych przykładowych (dev): pozwala przetestować ścieżkę
/// „grupa → rola” bez kontrolera domeny. Uczniowie (j.kowalski) celowo nie należą
/// do żadnej grupy ZSE-Admin-*.
/// </summary>
public sealed class MockAdGroupMembershipResolver : IAdGroupMembershipResolver
{
    private static readonly Dictionary<string, IReadOnlyList<string>> Groups =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["admin.it"] = new[] { "Administratorzy-Domain", "ZSE-Admin-Administratorzy" },
            ["k.mazur"] = new[] { "Nauczyciele", "ZSE-Admin-Nauczyciele" },
            ["t.lewandowski"] = new[] { "Nauczyciele", "ZSE-Admin-Nauczyciele" },
            ["e.dabrowska"] = new[] { "Nauczyciele", "ZSE-Admin-Nauczyciele" }
        };

    public Task<IReadOnlyList<string>> GetGroupNamesAsync(
        string samAccountName,
        CancellationToken cancellationToken = default) =>
        Task.FromResult(Groups.TryGetValue(samAccountName.Trim(), out var groups)
            ? groups
            : Array.Empty<string>());
}
