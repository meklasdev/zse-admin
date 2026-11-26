using System.Text.Json;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using Xunit;

namespace ZseAdmin.Tests;

/// <summary>Nowe typy M2/M3/M4 są zgodne z kopertą i konwencją camelCase (ADR-0002).</summary>
public sealed class M2M3M4ProtocolTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Welcome_niesie_zakres_pracowni_w_camelCase()
    {
        var welcome = new WelcomeResponse(
            "0.4.0", ProtocolJson.ProtocolVersion, "s1", "k.mazur", "Teacher",
            new[] { "classrooms.read", "logins.read" }, Now,
            new[] { "prac-101" });

        var json = JsonSerializer.Serialize(WsEnvelope.Create(MessageTypes.Welcome, null, welcome), ProtocolJson.Options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("welcome", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("prac-101", document.RootElement.GetProperty("payload").GetProperty("classroomScope")[0].GetString());
        Assert.Equal("Teacher", document.RootElement.GetProperty("payload").GetProperty("role").GetString());
    }

    [Fact]
    public void Zapytanie_o_logowania_niesie_zakres_pracowni()
    {
        var request = new LoginQueryRequest(
            Take: 50,
            ClassroomId: "prac-101",
            ClassroomIds: new[] { "prac-101", "prac-102" });

        var json = JsonSerializer.Serialize(request, ProtocolJson.Options);
        using var document = JsonDocument.Parse(json);

        Assert.Equal("prac-101", document.RootElement.GetProperty("classroomId").GetString());
        Assert.Equal(2, document.RootElement.GetProperty("classroomIds").GetArrayLength());
    }

    [Fact]
    public void Zdarzenie_logowania_ma_pracownie_do_filtrowania_zakresu()
    {
        var loginEvent = new LoginEventDto(
            Guid.NewGuid(), "j.kowalski", null, "KS-101-01", Now,
            LoginEventType.Logon, "Security log (4624)", "prac-101");

        var envelope = WsEnvelope.Create(
            MessageTypes.LoginEventsEvent,
            null,
            new LoginEventsChangedEvent(new[] { loginEvent }));

        var json = JsonSerializer.Serialize(envelope, ProtocolJson.Options);
        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal("event.logins.new", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("prac-101", payload.GetProperty("items")[0].GetProperty("classroomId").GetString());
        Assert.Equal("logon", payload.GetProperty("items")[0].GetProperty("type").GetString());
        Assert.False(document.RootElement.TryGetProperty("requestId", out _));
    }

    [Fact]
    public void Akcja_i_audyt_uzywaja_enumow_jako_stringow()
    {
        var prepared = new ActionPrepareResponse(
            "TOKEN", AdminActionKind.RestartComputer, "KS-101-01",
            "computers.control", "Restart stacji", "stan: online",
            new[] { "Operacja zostanie zapisana w audycie." }, Now.AddMinutes(2));

        var json = JsonSerializer.Serialize(
            WsEnvelope.Create(MessageTypes.ActionPrepare, "r-1", prepared),
            ProtocolJson.Options);
        using var document = JsonDocument.Parse(json);
        var payload = document.RootElement.GetProperty("payload");

        Assert.Equal("r-1", document.RootElement.GetProperty("requestId").GetString());
        Assert.Equal("restartComputer", payload.GetProperty("action").GetString());
        Assert.Equal("TOKEN", payload.GetProperty("confirmationToken").GetString());
        Assert.True(payload.TryGetProperty("warnings", out _));
    }

    [Fact]
    public void Wpis_audytu_jest_czytelny_dla_klienta()
    {
        var entry = new AuditEntryDto(
            Guid.NewGuid(), Now, "admin.it", "Administrator", "users.write",
            "ResetPassword", "j.kowalski", "hasło: ***", "hasło: *** (tymczasowe)",
            "success", "10.20.0.5", null);

        var freshness = new DataFreshnessDto("Rejestr audytu", Now, false, DataCompleteness.Complete, null);
        var envelope = WsEnvelope.Create(
            MessageTypes.AuditQuery,
            "r-2",
            new AuditQueryResponse(new[] { entry }, 1, freshness));

        var json = JsonSerializer.Serialize(envelope, ProtocolJson.Options);
        var parsed = JsonSerializer.Deserialize<WsEnvelope>(json, ProtocolJson.Options)!
            .ReadPayload<AuditQueryResponse>();

        Assert.NotNull(parsed);
        Assert.Equal(1, parsed!.TotalMatched);
        Assert.Equal("success", parsed.Items[0].Result);
        Assert.Equal(DataCompleteness.Complete, parsed.Freshness.Completeness);
    }
}
