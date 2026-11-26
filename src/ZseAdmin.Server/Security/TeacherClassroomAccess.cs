using ZseAdmin.Server.Configuration;

namespace ZseAdmin.Server.Security;

/// <summary>
/// Zakres pracowni nauczyciela (M3): stałe przypisanie + okna zajęć z planu lekcji.
/// Brak aktywnych pracowni = brak dostępu do historii (fail-closed).
/// </summary>
public interface ITeacherClassroomAccess
{
    IReadOnlyList<string> ActiveClassrooms(string userName, DateTimeOffset nowUtc);

    bool HasLessonIn(string userName, string classroomId, DateTimeOffset nowUtc);
}

public sealed class ConfiguredTeacherClassroomAccess : ITeacherClassroomAccess
{
    private readonly SecurityOptions _options;

    public ConfiguredTeacherClassroomAccess(SecurityOptions options)
    {
        _options = options;
    }

    public IReadOnlyList<string> ActiveClassrooms(string userName, DateTimeOffset nowUtc)
    {
        var normalized = TeachingSlotOptions.Normalize(userName);
        var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var pair in _options.TeacherClassrooms)
        {
            if (!string.Equals(TeachingSlotOptions.Normalize(pair.Key), normalized, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            foreach (var classroomId in pair.Value)
            {
                if (!string.IsNullOrWhiteSpace(classroomId))
                {
                    result.Add(classroomId.Trim());
                }
            }
        }

        foreach (var slot in _options.TeachingSlots)
        {
            if (!string.IsNullOrWhiteSpace(slot.ClassroomId) &&
                slot.Matches(normalized, slot.ClassroomId, nowUtc))
            {
                result.Add(slot.ClassroomId.Trim());
            }
        }

        return result.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList();
    }

    public bool HasLessonIn(string userName, string classroomId, DateTimeOffset nowUtc) =>
        ActiveClassrooms(userName, nowUtc)
            .Any(c => string.Equals(c, classroomId, StringComparison.OrdinalIgnoreCase));
}
