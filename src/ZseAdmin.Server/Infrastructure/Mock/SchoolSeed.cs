using ZseAdmin.Contracts;

namespace ZseAdmin.Server.Infrastructure.Mock;

/// <summary>
/// Deterministyczne dane przykładowe. Mają pokryć przypadki brzegowe, o których mówi README:
/// komputer offline, komputer bez IP, komputer poza znanym OU i konto bez ostatniego logowania.
/// </summary>
internal static class SchoolSeed
{
    private const string DomainSuffix = "DC=zse,DC=local";

    public static readonly string[] ClassroomIds = { "prac-101", "prac-102", "prac-205" };

    public static IReadOnlyList<ClassroomDto> Classrooms() => new List<ClassroomDto>
    {
        new("prac-101", "Pracownia 101", $"OU=PRAC101,OU=Pracownie,{DomainSuffix}", 6),
        new("prac-102", "Pracownia 102", $"OU=PRAC102,OU=Pracownie,{DomainSuffix}", 6),
        new("prac-205", "Pracownia 205", $"OU=PRAC205,OU=Pracownie,{DomainSuffix}", 4)
    };

    public static IReadOnlyList<ComputerDto> Computers(DateTimeOffset now)
    {
        var random = new Random(20260101);
        var computers = new List<ComputerDto>();

        foreach (var classroom in Classrooms())
        {
            var prefix = classroom.Id.Replace("prac-", "KS-");
            for (var index = 1; index <= classroom.ComputerCount; index++)
            {
                var name = $"{prefix}-{index:00}";
                var roll = random.Next(100);

                var status = roll switch
                {
                    < 72 => ComputerStatus.Online,
                    < 88 => ComputerStatus.Offline,
                    _ => ComputerStatus.Unknown
                };

                var hasIp = status == ComputerStatus.Online || random.Next(100) < 60;
                var ip = hasIp ? $"10.20.{classroom.Id[^3..]}.{10 + index}" : null;
                var observedMinutesAgo = status == ComputerStatus.Unknown ? 240 : random.Next(0, 90);

                computers.Add(new ComputerDto(
                    Name: name,
                    ClassroomId: classroom.Id,
                    ClassroomName: classroom.Name,
                    OuDistinguishedName: $"CN={name},{classroom.OuDistinguishedName}",
                    IpAddress: ip,
                    Status: status,
                    StatusObservedAtUtc: status == ComputerStatus.Unknown ? null : now.AddMinutes(-observedMinutesAgo),
                    LastLogonAtUtc: now.AddHours(-random.Next(1, 96)),
                    OperatingSystem: "Windows 10 Pro 22H2"));
            }
        }

        // Komputer przeniesiony poza znane OU — widoczny, ale bez przypisanej pracowni.
        computers.Add(new ComputerDto(
            Name: "KS-SERWER-01",
            ClassroomId: null,
            ClassroomName: null,
            OuDistinguishedName: $"OU=Serwerownia,{DomainSuffix}",
            IpAddress: "10.20.0.5",
            Status: ComputerStatus.Online,
            StatusObservedAtUtc: now.AddMinutes(-2),
            LastLogonAtUtc: now.AddMinutes(-35),
            OperatingSystem: "Windows Server 2016"));

        return computers;
    }

    public static IReadOnlyList<AdUserDto> Users()
    {
        var students = $"OU=Uczniowie,OU=ZSE,{DomainSuffix}";
        var staff = $"OU=Nauczyciele,OU=ZSE,{DomainSuffix}";

        return new List<AdUserDto>
        {
            new("j.kowalski", "Jan Kowalski", "j.kowalski@zse.local", true,
                new[] { "Uczniowie", "Klasa-3TI" }, students, new DateTimeOffset(2026, 11, 24, 8, 12, 0, TimeSpan.Zero)),
            new("a.nowak", "Anna Nowak", "a.nowak@zse.local", true,
                new[] { "Uczniowie", "Klasa-3TI" }, students, new DateTimeOffset(2026, 11, 24, 9, 3, 0, TimeSpan.Zero)),
            new("m.wisniewski", "Marek Wiśniewski", "m.wisniewski@zse.local", false,
                new[] { "Uczniowie", "Klasa-2TI" }, students, null),
            new("p.zielinski", "Paweł Zieliński", "p.zielinski@zse.local", true,
                new[] { "Uczniowie", "Klasa-2TI" }, students, new DateTimeOffset(2026, 11, 21, 12, 45, 0, TimeSpan.Zero)),
            new("k.mazur", "Katarzyna Mazur", "k.mazur@zse.local", true,
                new[] { "Nauczyciele", "Administratorzy-Pracowni" }, staff, new DateTimeOffset(2026, 11, 25, 7, 5, 0, TimeSpan.Zero)),
            new("t.lewandowski", "Tomasz Lewandowski", "t.lewandowski@zse.local", true,
                new[] { "Nauczyciele", "Informatyka" }, staff, new DateTimeOffset(2026, 11, 24, 13, 20, 0, TimeSpan.Zero)),
            new("e.dabrowska", "Ewa Dąbrowska", "e.dabrowska@zse.local", true,
                new[] { "Nauczyciele", "Informatyka" }, staff, null),
            new("admin.it", "Administrator IT", "admin.it@zse.local", true,
                new[] { "Administratorzy-Domain", "Administratorzy-Pracowni" }, staff, new DateTimeOffset(2026, 11, 25, 6, 40, 0, TimeSpan.Zero))
        };
    }

    public static IReadOnlyList<LoginEventDto> Logins(DateTimeOffset now, int count = 250)
    {
        var random = new Random(20251125);
        var computers = Computers(now);
        var users = Users();
        var events = new List<LoginEventDto>(count);

        for (var index = 0; index < count; index++)
        {
            var computer = computers[random.Next(computers.Count)];
            var user = users[random.Next(users.Count)];
            // Pracownia zapisana przy zdarzeniu (jak w magazynie M2) — potrzebna
            // do ograniczenia widoczności historii dla nauczycieli (M3).
            var minutesAgo = random.Next(0, 14 * 24 * 60);
            var type = random.Next(100) switch
            {
                < 72 => LoginEventType.Logon,
                < 88 => LoginEventType.Logoff,
                _ => LoginEventType.FailedLogon
            };

            var source = type switch
            {
                LoginEventType.Logon => "Security log (4624)",
                LoginEventType.Logoff => "Security log (4634)",
                _ => "Security log (4625)"
            };

            events.Add(new LoginEventDto(
                Id: Guid.NewGuid(),
                UserSamAccountName: user.SamAccountName,
                UserDisplayName: user.DisplayName,
                ComputerName: computer.Name,
                OccurredAtUtc: now.AddMinutes(-minutesAgo),
                Type: type,
                Source: source,
                ClassroomId: computer.ClassroomId));
        }

        return events;
    }
}
