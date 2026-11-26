namespace ZseAdmin.Server.Configuration;

/// <summary>
/// Uwierzytelnianie i zakres danych (M3).
/// <list type="bullet">
/// <item><c>Mode = Token</c>: wyłącznie development/CI; statyczne tokeny z appsettings.json.</item>
/// <item><c>Mode = Windows</c>: produkcja; tożsamość z Negotiate/Kerberos (IIS/HTTP.sys/Kestrel),
/// rola wyliczana z grup AD, tokeny ignorowane, wymagany <c>wss</c>.</item>
/// </list>
/// </summary>
public sealed class SecurityOptions
{
    public const string SectionName = "Security";
    public const string WindowsMode = "Windows";
    public const string TokenMode = "Token";
    public const string TeacherGroup = "ZSE-Admin-Nauczyciele";

    /// <summary><c>Token</c> (dev) albo <c>Windows</c> (produkcja).</summary>
    public string Mode { get; set; } = TokenMode;

    /// <summary>Gdy <c>true</c>, połączenie bez HTTPS/<c>wss</c> jest odrzucane przed upgrade'em.</summary>
    public bool RequireSecureTransport { get; set; }

    /// <summary>Legacy: w trybie Token — czy wymagać tokenu (false = rola Administrator dla dev).</summary>
    public bool RequireToken { get; set; } = true;

    /// <summary>Tokeny developerskie. W trybie Windows są ignorowane, produkcja ich zabrania.</summary>
    public List<TokenEntry> Tokens { get; set; } = new();

    /// <summary>
    /// Mapowanie grupy AD → rola. Kolejność rozstrzygania: Administrator, Teacher, ReadOnly.
    /// Użytkownik bez którejkolwiek z tych grup nie może się połączyć (kryterium M3).
    /// </summary>
    public Dictionary<string, string> RoleGroups { get; set; } = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ZSE-Admin-Administratorzy"] = "Administrator",
        [TeacherGroup] = "Teacher",
        ["ZSE-Admin-ReadOnly"] = "ReadOnly"
    };

    /// <summary>Stałe przypisanie nauczyciela do pracowni (np. na cały semestr).</summary>
    public Dictionary<string, List<string>> TeacherClassrooms { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Zajęcia z planu lekcji: tylko w ich trakcie nauczyciel widzi historię pracowni.</summary>
    public List<TeachingSlotOptions> TeachingSlots { get; set; } = new();

    /// <summary>Gdy <c>true</c>, nauczyciel dostaje wyłącznie logowania ze swoich pracowni.</summary>
    public bool EnforceTeacherClassroomScope { get; set; } = true;

    public bool IsWindowsMode => string.Equals(Mode, WindowsMode, StringComparison.OrdinalIgnoreCase);

    public bool IsTokenMode => !IsWindowsMode;
}

public sealed class TokenEntry
{
    public string Name { get; set; } = string.Empty;

    public string Token { get; set; } = string.Empty;

    public string Role { get; set; } = ZseAdmin.Server.Security.Roles.ReadOnly;
}

/// <summary>
/// Jedno okno zajęć. Dni i godziny są w UTC, żeby zachowanie było przewidywalne
/// niezależnie od strefy czasowej serwera (i testowalne bez niej).
/// </summary>
public sealed class TeachingSlotOptions
{
    public string Teacher { get; set; } = string.Empty;

    public string ClassroomId { get; set; } = string.Empty;

    /// <summary>Np. <c>["Monday","Wednesday"]</c>. Pusta lista = każdy dzień.</summary>
    public List<string> Days { get; set; } = new();

    /// <summary><c>HH:mm</c>, UTC, włącznie.</summary>
    public string StartUtc { get; set; } = "08:00";

    /// <summary><c>HH:mm</c>, UTC, wyłącznie.</summary>
    public string EndUtc { get; set; } = "09:00";

    /// <summary>Sprawdza, czy użytkownik ma w tym momencie zajęcia w tej pracowni.</summary>
    public bool Matches(string userName, string classroomId, DateTimeOffset nowUtc)
    {
        if (!string.Equals(
                Normalize(Teacher), Normalize(userName), StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(ClassroomId.Trim(), classroomId.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TimeSpan.TryParse(StartUtc, out var start) || !TimeSpan.TryParse(EndUtc, out var end))
        {
            return false;
        }

        var utc = nowUtc.UtcDateTime;

        if (Days.Count > 0)
        {
            var anyDay = Days.Any(day =>
                Enum.TryParse<DayOfWeek>(day, ignoreCase: true, out var parsed) && parsed == utc.DayOfWeek);

            if (!anyDay)
            {
                return false;
            }
        }

        var time = utc.TimeOfDay;
        return start <= end
            ? time >= start && time < end
            : time >= start || time < end; // zajęcia przechodzące przez północ
    }

    /// <summary>Usuwa prefiks domeny z nazwy konta (<c>ZSE\k.mazur</c> → <c>k.mazur</c>).</summary>
    public static string Normalize(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();
        var separator = trimmed.LastIndexOf('\\');
        return separator >= 0 && separator < trimmed.Length - 1
            ? trimmed[(separator + 1)..]
            : trimmed;
    }
}
