using System.Text.Json;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;
using Xunit;

namespace ZseAdmin.Tests;

public sealed class ProtocolSerializationTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 25, 10, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Koperta_uzywa_camel_case_i_nie_wysyla_pustego_payloadu()
    {
        var json = JsonSerializer.Serialize(WsEnvelope.Create(MessageTypes.Ping, "req-1"), ProtocolJson.Options);

        using var document = JsonDocument.Parse(json);
        var root = document.RootElement;

        Assert.Equal("ping", root.GetProperty("type").GetString());
        Assert.Equal("req-1", root.GetProperty("requestId").GetString());
        Assert.False(root.TryGetProperty("payload", out _));
    }

    [Fact]
    public void Enumy_sa_stringami_a_odpowiedz_da_sie_odczytac()
    {
        var computer = new ComputerDto(
            "KS-101-01", "prac-101", "Pracownia 101", "CN=KS-101-01,OU=PRAC101",
            "10.20.101.11", ComputerStatus.Online, Now, Now.AddHours(-3), "Windows 10 Pro");

        var response = new ComputersResponse(
            new[] { computer },
            new DataFreshnessDto("test", Now, false, DataCompleteness.Complete, null));

        var envelope = WsEnvelope.Create(MessageTypes.ComputersList, "req-2", response);
        var json = JsonSerializer.Serialize(envelope, ProtocolJson.Options);

        using var document = JsonDocument.Parse(json);
        var payloadElement = document.RootElement.GetProperty("payload");
        var item = payloadElement.GetProperty("items")[0];

        Assert.Equal("online", item.GetProperty("status").GetString());
        Assert.Equal("complete", payloadElement.GetProperty("freshness").GetProperty("completeness").GetString());

        var parsed = JsonSerializer.Deserialize<WsEnvelope>(json, ProtocolJson.Options);
        var payload = parsed!.ReadPayload<ComputersResponse>();

        Assert.NotNull(payload);
        Assert.Single(payload!.Items);
        Assert.Equal(ComputerStatus.Online, payload.Items[0].Status);
        Assert.Equal(DataCompleteness.Complete, payload.Freshness.Completeness);
    }

    [Fact]
    public void Akcja_na_stacji_round_trip_zachowuje_confirm()
    {
        var request = new ComputerActionRequest("KS-101-01", ComputerAction.Restart, Confirm: true);

        var json = JsonSerializer.Serialize(
            WsEnvelope.Create(MessageTypes.ComputersAction, "req-m4-1", request),
            ProtocolJson.Options);

        var parsed = JsonSerializer.Deserialize<WsEnvelope>(json, ProtocolJson.Options)!
            .ReadPayload<ComputerActionRequest>();

        Assert.NotNull(parsed);
        Assert.True(parsed!.Confirm);
        Assert.Equal("KS-101-01", parsed.ComputerName);
        Assert.Equal(ComputerAction.Restart, parsed.Action);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("restart", document.RootElement.GetProperty("payload").GetProperty("action").GetString());
    }

    [Fact]
    public void Reset_hasla_round_trip_zachowuje_haslo_tylko_w_transkrypcie()
    {
        var request = new ResetPasswordRequest("j.kowalski", "tajne-haslo-1", Confirm: true);

        var json = JsonSerializer.Serialize(
            WsEnvelope.Create(MessageTypes.UsersResetPassword, "req-m4-2", request),
            ProtocolJson.Options);

        var parsed = JsonSerializer.Deserialize<WsEnvelope>(json, ProtocolJson.Options)!
            .ReadPayload<ResetPasswordRequest>();

        Assert.NotNull(parsed);
        Assert.Equal("tajne-haslo-1", parsed!.NewPassword);
        Assert.True(parsed.Confirm);
    }

    [Fact]
    public void Odpowiedz_audytu_da_sie_odczytac_wraz_z_hashem()
    {
        var entry = new AuditEntryDto(
            Guid.NewGuid(),
            Now,
            "admin.it",
            "Administrator",
            "computers.control",
            "KS-101-01",
            "status=online",
            "Wydano: restart",
            "accepted",
            "192.168.1.10");

        var response = new AuditResponse(
            new[] { entry },
            new DataFreshnessDto("Dziennik audytu (lokalny)", Now, false, DataCompleteness.Complete, null));

        var json = JsonSerializer.Serialize(
            WsEnvelope.Create(MessageTypes.AuditQuery, "req-m4-3", response),
            ProtocolJson.Options);

        var parsed = JsonSerializer.Deserialize<WsEnvelope>(json, ProtocolJson.Options)!
            .ReadPayload<AuditResponse>();

        Assert.NotNull(parsed);
        Assert.Single(parsed!.Items);
        Assert.Equal("KS-101-01", parsed.Items[0].Target);
        Assert.Equal("accepted", parsed.Items[0].Result);
    }

    [Fact]
    public void Odpowiedz_dodania_do_grupy_ma_flage_changed()
    {
        var response = new AddToGroupResponse("j.kowalski", "Klasa-3TI", true, "Dodano.", Guid.NewGuid());

        var json = JsonSerializer.Serialize(
            WsEnvelope.Create(MessageTypes.UsersAddToGroup, "req-m4-4", response),
            ProtocolJson.Options);

        var parsed = JsonSerializer.Deserialize<WsEnvelope>(json, ProtocolJson.Options)!
            .ReadPayload<AddToGroupResponse>();

        Assert.NotNull(parsed);
        Assert.True(parsed!.Changed);
        Assert.Equal("Klasa-3TI", parsed.GroupName);
    }

    [Fact]
    public void Blad_ma_kod_i_wiadomosc()
    {
        var envelope = WsEnvelope.Create(
            MessageTypes.Error,
            "req-3",
            new ErrorDto(ErrorCode.Forbidden, "Brak uprawnienia.", null));

        var json = JsonSerializer.Serialize(envelope, ProtocolJson.Options);
        var parsed = JsonSerializer.Deserialize<WsEnvelope>(json, ProtocolJson.Options)!.ReadPayload<ErrorDto>();

        Assert.NotNull(parsed);
        Assert.Equal(ErrorCode.Forbidden, parsed!.Code);
        Assert.Equal("Brak uprawnienia.", parsed.Message);
    }

    [Fact]
    public void Event_zmiany_stanu_komputera_jest_parsowalny()
    {
        var computer = new ComputerDto(
            "KS-101-02", "prac-101", "Pracownia 101", null,
            null, ComputerStatus.Unknown, null, null, null);

        var json = JsonSerializer.Serialize(
            WsEnvelope.Create(MessageTypes.ComputerStatusEvent, null, new ComputerStatusChangedEvent(computer)),
            ProtocolJson.Options);

        using var document = JsonDocument.Parse(json);
        Assert.Equal("event.computer.status", document.RootElement.GetProperty("type").GetString());
        Assert.False(document.RootElement.TryGetProperty("requestId", out _));

        var parsed = JsonSerializer.Deserialize<WsEnvelope>(json, ProtocolJson.Options)!
            .ReadPayload<ComputerStatusChangedEvent>();

        Assert.NotNull(parsed);
        Assert.Equal(ComputerStatus.Unknown, parsed!.Computer.Status);
        Assert.Null(parsed.Computer.IpAddress);
    }
}
