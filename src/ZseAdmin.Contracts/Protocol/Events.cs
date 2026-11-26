namespace ZseAdmin.Contracts.Protocol;

/// <summary>
/// Zdarzenie serwera o zmianie stanu komputera. Wysyłane bez <c>requestId</c>
/// i tylko do sesji z uprawnieniem <c>computers.read</c>.
/// </summary>
public sealed record ComputerStatusChangedEvent(ComputerDto Computer);
