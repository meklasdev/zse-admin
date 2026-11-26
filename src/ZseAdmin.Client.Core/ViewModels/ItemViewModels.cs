using ZseAdmin.Client.Core.Display;
using ZseAdmin.Contracts;

namespace ZseAdmin.Client.Core.ViewModels;

/// <summary>Pozycja listy pracowni. <see cref="Id"/> = <c>null</c> oznacza „wszystkie pracownie”.</summary>
public sealed class ClassroomItemViewModel
{
    public ClassroomItemViewModel(string? id, string name, string? ouDistinguishedName, int computerCount)
    {
        Id = id;
        Name = name;
        OuDistinguishedName = ouDistinguishedName;
        ComputerCount = computerCount;
    }

    public string? Id { get; }

    public string Name { get; }

    public string? OuDistinguishedName { get; }

    public int ComputerCount { get; }

    public string Summary
    {
        get
        {
            var computers = DisplayFormat.Computers(ComputerCount);
            return OuDistinguishedName is null
                ? computers
                : $"{computers} · {ShortOu(OuDistinguishedName)}";
        }
    }

    public static ClassroomItemViewModel All(IReadOnlyList<ClassroomDto> classrooms) =>
        new(null, "Wszystkie pracownie", null, classrooms.Sum(c => c.ComputerCount));

    public static ClassroomItemViewModel From(ClassroomDto dto) =>
        new(dto.Id, dto.Name, dto.OuDistinguishedName, dto.ComputerCount);

    private static string ShortOu(string distinguishedName)
    {
        var first = distinguishedName.Split(',')[0];
        return first.StartsWith("OU=", StringComparison.OrdinalIgnoreCase) ? first[3..] : first;
    }
}

/// <summary>Pozycja listy komputerów. Stan zawsze niesie informację o czasie obserwacji.</summary>
public sealed class ComputerItemViewModel
{
    public ComputerItemViewModel(ComputerDto dto)
    {
        Name = dto.Name;
        ClassroomName = dto.ClassroomName ?? "poza pracownią";
        IpAddress = string.IsNullOrWhiteSpace(dto.IpAddress) ? DisplayFormat.Missing : dto.IpAddress;
        StatusText = DisplayFormat.Status(dto.Status);
        StatusObservedText = DisplayFormat.DateTime(dto.StatusObservedAtUtc);
        LastLogonText = DisplayFormat.DateTime(dto.LastLogonAtUtc);
        OperatingSystem = dto.OperatingSystem ?? DisplayFormat.Missing;
        Status = dto.Status;
        Dto = dto;
    }

    public ComputerDto Dto { get; }

    public string Name { get; }

    public string ClassroomName { get; }

    public string IpAddress { get; }

    public string StatusText { get; }

    public string StatusObservedText { get; }

    public string LastLogonText { get; }

    public string OperatingSystem { get; }

    public ComputerStatus Status { get; }
}

/// <summary>Pojedyncze zdarzenie logowania w czytelnej formie.</summary>
public sealed class LoginEventItemViewModel
{
    public LoginEventItemViewModel(LoginEventDto dto)
    {
        Headline = $"{dto.UserSamAccountName} → {dto.ComputerName}";
        Detail = $"{DisplayFormat.LoginType(dto.Type)} · {DisplayFormat.DateTime(dto.OccurredAtUtc)} · {dto.Source}";
    }

    public string Headline { get; }

    public string Detail { get; }
}
