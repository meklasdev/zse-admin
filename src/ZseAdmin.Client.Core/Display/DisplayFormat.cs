using ZseAdmin.Contracts;

namespace ZseAdmin.Client.Core.Display;

/// <summary>Formatowanie dla UI: polskie nazwy i jawne „brak danych” zamiast pustych pól.</summary>
public static class DisplayFormat
{
    public const string Missing = "—";

    public static string DateTime(DateTimeOffset? value) =>
        value is { } v ? v.ToLocalTime().ToString("dd.MM.yyyy HH:mm") : Missing;

    public static string Time(DateTimeOffset? value) =>
        value is { } v ? v.ToLocalTime().ToString("HH:mm:ss") : Missing;

    public static string Status(ComputerStatus status) => status switch
    {
        ComputerStatus.Online => "online",
        ComputerStatus.Offline => "offline",
        _ => "brak danych"
    };

    public static string LoginType(LoginEventType type) => type switch
    {
        LoginEventType.Logon => "logowanie",
        LoginEventType.Logoff => "wylogowanie",
        _ => "nieudane logowanie"
    };

    public static string Completeness(DataCompleteness completeness) => completeness switch
    {
        DataCompleteness.Complete => "kompletne",
        DataCompleteness.Partial => "częściowe",
        _ => "niedostępne"
    };

    /// <summary>Odmiana rzeczownika po liczbie: 1 komputer, 2 komputery, 5 komputerów.</summary>
    public static string Count(int value, string one, string few, string many)
    {
        var absolute = Math.Abs(value);
        if (absolute == 1)
        {
            return $"{value} {one}";
        }

        var lastDigit = absolute % 10;
        var lastTwo = absolute % 100;

        if (lastDigit is >= 2 and <= 4 && lastTwo is not (>= 12 and <= 14))
        {
            return $"{value} {few}";
        }

        return $"{value} {many}";
    }

    public static string Computers(int value) => Count(value, "komputer", "komputery", "komputerów");

    public static string Events(int value) => Count(value, "zdarzenie", "zdarzenia", "zdarzeń");
}
