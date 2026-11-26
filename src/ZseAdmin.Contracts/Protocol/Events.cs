namespace ZseAdmin.Contracts.Protocol;

/// <summary>
/// Zdarzenie serwera o zmianie stanu komputera. Wysyłane bez <c>requestId</c>
/// i tylko do sesji z uprawnieniem <c>computers.read</c>.
/// </summary>
public sealed record ComputerStatusChangedEvent(ComputerDto Computer);

/// <summary>
/// Nowe zdarzenia logowań odebrane z dzienników stacji (M2). Wysyłane bez <c>requestId</c>
/// tylko do sesji, które mogą zobaczyć dane zdarzenie (M3: zakres pracowni nauczyciela).
/// </summary>
public sealed record LoginEventsChangedEvent(IReadOnlyList<LoginEventDto> Items);
