using System.Collections.ObjectModel;
using ZseAdmin.Client.Core.Api;
using ZseAdmin.Client.Core.Connection;
using ZseAdmin.Client.Core.Display;
using ZseAdmin.Client.Core.Mvvm;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;

namespace ZseAdmin.Client.Core.ViewModels;

/// <summary>
/// Widok główny: pracownie → komputery → logowania, pasek zaufania do danych,
/// akcje M4 z potwierdzeniem oraz audyt widoczny w szczegółach obiektu.
/// Wątek UI jest jedynym miejscem zmiany kolekcji — każda aktualizacja idzie przez
/// <see cref="IUiDispatcher"/>. Uprawnienia pochodzą wyłącznie z <c>welcome</c> serwera.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const int RecentLoginsTake = 60;
    private const int AuditTake = 20;

    private const string CapabilityComputersControl = "computers.control";
    private const string CapabilityUsersWrite = "users.write";
    private const string CapabilityAuditRead = "audit.read";

    private readonly ZseAdminApiClient _api;
    private readonly WsClientConnection _connection;
    private readonly IUiDispatcher _dispatcher;

    private ClassroomItemViewModel? _selectedClassroom;
    private ComputerItemViewModel? _selectedComputer;
    private HashSet<string> _capabilities = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<string>? _classroomScope;
    private ActionPrepareResponse? _pendingAction;

    private string _connectionStatusText = "brak połączenia";
    private string _identityText = "niezidentyfikowany";
    private string _freshnessText = "Dane: —";
    private string? _statusMessage = "Łączenie z serwerem…";
    private string _actionTargetText = string.Empty;
    private string? _groupNameText;
    private string? _actionReasonText;
    private string? _pendingActionText;
    private string? _temporaryPasswordText;
    private string _auditSummaryText = "Audyt — wybierz komputer lub podaj konto.";
    private bool _isBusy;
    private bool _suppressSelectionReload;
    private bool _canControlComputers;
    private bool _canWriteUsers;
    private bool _canReadAudit;
    private bool _canConfirmAction;

    public MainViewModel(ZseAdminApiClient api, WsClientConnection connection, IUiDispatcher dispatcher)
    {
        _api = api;
        _connection = connection;
        _dispatcher = dispatcher;

        RefreshCommand = new AsyncRelayCommand(() => ReloadAllAsync(CancellationToken.None), () => !IsBusy);
        RestartCommand = new AsyncRelayCommand(
            () => PrepareActionAsync(AdminActionKind.RestartComputer),
            () => CanControlComputers && !IsBusy);
        LockCommand = new AsyncRelayCommand(
            () => PrepareActionAsync(AdminActionKind.LockComputer),
            () => CanControlComputers && !IsBusy);
        ResetPasswordCommand = new AsyncRelayCommand(
            () => PrepareActionAsync(AdminActionKind.ResetPassword),
            () => CanWriteUsers && !IsBusy);
        AddToGroupCommand = new AsyncRelayCommand(
            () => PrepareActionAsync(AdminActionKind.AddToGroup),
            () => CanWriteUsers && !IsBusy);
        ConfirmActionCommand = new AsyncRelayCommand(ConfirmActionAsync, () => CanConfirmAction && !IsBusy);
        CancelActionCommand = new AsyncRelayCommand(CancelActionAsync, () => CanConfirmAction);
        RefreshAuditCommand = new AsyncRelayCommand(
            () => ReloadAuditAsync(CancellationToken.None),
            () => CanReadAudit);

        _connection.StateChanged += OnConnectionStateChanged;
        _api.ComputerStatusChanged += OnComputerStatusChanged;
        _api.LoginEventsReceived += OnLoginEventsReceived;
        _api.WelcomeReceived += OnWelcomeReceived;
    }

    public ObservableCollection<ClassroomItemViewModel> Classrooms { get; } = new();

    public ObservableCollection<ComputerItemViewModel> Computers { get; } = new();

    public ObservableCollection<LoginEventItemViewModel> Logins { get; } = new();

    public ObservableCollection<AuditEntryItemViewModel> AuditItems { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }
    public AsyncRelayCommand RestartCommand { get; }
    public AsyncRelayCommand LockCommand { get; }
    public AsyncRelayCommand ResetPasswordCommand { get; }
    public AsyncRelayCommand AddToGroupCommand { get; }
    public AsyncRelayCommand ConfirmActionCommand { get; }
    public AsyncRelayCommand CancelActionCommand { get; }
    public AsyncRelayCommand RefreshAuditCommand { get; }

    public ClassroomItemViewModel? SelectedClassroom
    {
        get => _selectedClassroom;
        set
        {
            if (!SetProperty(ref _selectedClassroom, value) || _suppressSelectionReload)
            {
                return;
            }

            _ = ReloadComputersAsync(CancellationToken.None);
        }
    }

    public ComputerItemViewModel? SelectedComputer
    {
        get => _selectedComputer;
        set
        {
            if (!SetProperty(ref _selectedComputer, value))
            {
                return;
            }

            if (value is not null)
            {
                ActionTargetText = value.Name;
            }

            _ = ReloadAuditAsync(CancellationToken.None);
        }
    }

    public string ActionTargetText
    {
        get => _actionTargetText;
        set => SetProperty(ref _actionTargetText, value);
    }

    public string? GroupNameText
    {
        get => _groupNameText;
        set => SetProperty(ref _groupNameText, value);
    }

    public string? ActionReasonText
    {
        get => _actionReasonText;
        set => SetProperty(ref _actionReasonText, value);
    }

    public string? PendingActionText
    {
        get => _pendingActionText;
        private set => SetProperty(ref _pendingActionText, value);
    }

    public string? TemporaryPasswordText
    {
        get => _temporaryPasswordText;
        private set => SetProperty(ref _temporaryPasswordText, value);
    }

    public string AuditSummaryText
    {
        get => _auditSummaryText;
        private set => SetProperty(ref _auditSummaryText, value);
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => SetProperty(ref _connectionStatusText, value);
    }

    public string IdentityText
    {
        get => _identityText;
        private set => SetProperty(ref _identityText, value);
    }

    public string FreshnessText
    {
        get => _freshnessText;
        private set => SetProperty(ref _freshnessText, value);
    }

    public string? StatusMessage
    {
        get => _statusMessage;
        private set => SetProperty(ref _statusMessage, value);
    }

    /// <summary>Uprawnienia z <c>welcome</c> decydują o widoczności akcji (M4).</summary>
    public bool CanControlComputers
    {
        get => _canControlComputers;
        private set => SetProperty(ref _canControlComputers, value);
    }

    public bool CanWriteUsers
    {
        get => _canWriteUsers;
        private set => SetProperty(ref _canWriteUsers, value);
    }

    public bool CanReadAudit
    {
        get => _canReadAudit;
        private set => SetProperty(ref _canReadAudit, value);
    }

    public bool CanConfirmAction
    {
        get => _canConfirmAction;
        private set
        {
            if (SetProperty(ref _canConfirmAction, value))
            {
                ConfirmActionCommand.RaiseCanExecuteChanged();
                CancelActionCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RaiseAllCanExecute();
            }
        }
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _connection.StartAsync(cancellationToken);

    private void OnWelcomeReceived(object? sender, WelcomeResponse welcome)
    {
        _dispatcher.Post(() =>
        {
            _capabilities = new HashSet<string>(welcome.Capabilities, StringComparer.OrdinalIgnoreCase);
            _classroomScope = welcome.ClassroomScope;

            IdentityText = $"{welcome.UserName} ({welcome.Role})";
            CanControlComputers = _capabilities.Contains(CapabilityComputersControl);
            CanWriteUsers = _capabilities.Contains(CapabilityUsersWrite);
            CanReadAudit = _capabilities.Contains(CapabilityAuditRead);
            RaiseAllCanExecute();

            if (welcome.ProtocolVersion > ProtocolJson.ProtocolVersion)
            {
                StatusMessage = "Serwer używa nowszego protokołu — zaktualizuj klienta.";
            }

            if (_classroomScope is { Count: 0 })
            {
                StatusMessage = "Brak zajęć w tej chwili — historia logowań jest niedostępna.";
            }

            _ = ReloadAllAsync(CancellationToken.None);
            _ = ReloadAuditAsync(CancellationToken.None);
        });
    }

    private void OnConnectionStateChanged(object? sender, ConnectionState state)
    {
        _dispatcher.Post(() =>
        {
            ConnectionStatusText = state switch
            {
                ConnectionState.Connected => "połączono z serwerem",
                ConnectionState.Connecting => "łączenie z serwerem…",
                ConnectionState.Reconnecting => "ponowne łączenie…",
                _ => "brak połączenia"
            };

            if (state is ConnectionState.Reconnecting or ConnectionState.Disconnected)
            {
                StatusMessage = _connection.LastTransportError is { Length: > 0 } error
                    ? $"Brak połączenia: {error}"
                    : "Brak połączenia z serwerem — dane mogą być nieaktualne.";
            }
        });

        if (state == ConnectionState.Connected)
        {
            _ = ReloadAllAsync(CancellationToken.None);
        }
    }

    private void OnComputerStatusChanged(object? sender, ComputerStatusChangedEvent changed)
    {
        _dispatcher.Post(() =>
        {
            var index = FindComputerIndex(changed.Computer.Name);

            if (SelectedClassroom?.Id is { } classroomId &&
                !string.Equals(changed.Computer.ClassroomId, classroomId, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            if (index >= 0)
            {
                Computers[index] = new ComputerItemViewModel(changed.Computer);
            }
            else
            {
                Computers.Add(new ComputerItemViewModel(changed.Computer));
            }

            FreshnessText = $"Stan komputerów: {DisplayFormat.Time(changed.Computer.StatusObservedAtUtc)} · aktualizacja na żywo";
        });
    }

    private void OnLoginEventsReceived(object? sender, LoginEventsChangedEvent changed)
    {
        _dispatcher.Post(() =>
        {
            var first = changed.Items.FirstOrDefault();
            if (first is null)
            {
                return;
            }

            foreach (var loginEvent in changed.Items.Reverse())
            {
                if (_classroomScope is { } scope &&
                    (loginEvent.ClassroomId is null ||
                     !scope.Any(c => string.Equals(c, loginEvent.ClassroomId, StringComparison.OrdinalIgnoreCase))))
                {
                    continue;
                }

                Logins.Insert(0, new LoginEventItemViewModel(loginEvent));
            }

            while (Logins.Count > RecentLoginsTake)
            {
                Logins.RemoveAt(Logins.Count - 1);
            }

            FreshnessText = $"Historia logowań: aktualizacja na żywo · {DisplayFormat.Time(DateTimeOffset.UtcNow)}";
            StatusMessage = $"Nowe logowanie: {first.UserSamAccountName} → {first.ComputerName} " +
                            $"({DisplayFormat.DateTime(first.OccurredAtUtc)}).";
        });
    }

    private async Task ReloadAllAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        await _dispatcher.InvokeAsync(() =>
        {
            IsBusy = true;
            StatusMessage = "Pobieranie danych…";
        });

        try
        {
            var classrooms = await _api.GetClassroomsAsync(cancellationToken).ConfigureAwait(false);
            var computers = await _api
                .GetComputersAsync(SelectedClassroom?.Id, cancellationToken)
                .ConfigureAwait(false);

            LoginsResponse? logins = null;
            if (_classroomScope is not { Count: 0 })
            {
                logins = await _api.QueryLoginsAsync(
                    new LoginQueryRequest(
                        Take: RecentLoginsTake,
                        ClassroomIds: _classroomScope),
                    cancellationToken).ConfigureAwait(false);
            }

            _dispatcher.Post(() =>
            {
                ApplyClassrooms(classrooms.Items);
                ApplyComputers(computers.Items);
                ApplyLogins(logins?.Items ?? Array.Empty<LoginEventDto>());
                FreshnessText = BuildFreshnessText(computers.Freshness, logins?.Freshness);
                StatusMessage = _classroomScope is { Count: 0 }
                    ? "Brak zajęć w tej chwili — historia logowań niedostępna."
                    : $"Zaktualizowano {DateTime.Now:HH:mm:ss} · {DisplayFormat.Events(logins?.TotalMatched ?? 0)} w historii";
                IsBusy = false;
            });

            await ReloadAuditAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() =>
            {
                StatusMessage = FormatError("Nie udało się pobrać danych", ex);
                IsBusy = false;
            });
        }
    }

    private async Task ReloadComputersAsync(CancellationToken cancellationToken)
    {
        try
        {
            var computers = await _api
                .GetComputersAsync(SelectedClassroom?.Id, cancellationToken)
                .ConfigureAwait(false);

            _dispatcher.Post(() =>
            {
                ApplyComputers(computers.Items);
                FreshnessText = BuildFreshnessText(computers.Freshness, null);
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() => StatusMessage = FormatError("Nie udało się pobrać komputerów", ex));
        }
    }

    private async Task ReloadAuditAsync(CancellationToken cancellationToken)
    {
        if (!CanReadAudit)
        {
            _dispatcher.Post(() =>
            {
                AuditItems.Clear();
                AuditSummaryText = "Audyt jest dostępny wyłącznie dla administratora.";
            });
            return;
        }

        var target = string.IsNullOrWhiteSpace(ActionTargetText) ? null : ActionTargetText.Trim();

        try
        {
            var result = await _api.QueryAuditAsync(
                new AuditQueryRequest(target, AuditTake),
                cancellationToken).ConfigureAwait(false);

            _dispatcher.Post(() =>
            {
                AuditItems.Clear();
                foreach (var entry in result.Items)
                {
                    AuditItems.Add(new AuditEntryItemViewModel(entry));
                }

                AuditSummaryText = $"Audyt: {DisplayFormat.Events(result.TotalMatched)} dla " +
                                  $"'{target ?? "wszystkich obiektów"}'";
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() =>
                AuditSummaryText = FormatError("Nie udało się pobrać audytu", ex));
        }
    }

    private async Task PrepareActionAsync(AdminActionKind kind)
    {
        var target = ActionTargetText.Trim();
        if (target.Length == 0)
        {
            StatusMessage = "Wybierz komputer lub wpisz nazwę konta w polu „Cel operacji”.";
            return;
        }

        try
        {
            var prepared = await _api.PrepareActionAsync(
                new ActionPrepareRequest(kind, target, GroupNameText, ActionReasonText))
                .ConfigureAwait(false);

            _dispatcher.Post(() =>
            {
                _pendingAction = prepared;
                CanConfirmAction = true;
                TemporaryPasswordText = null;
                PendingActionText =
                    $"{prepared.Description}\nPrzed: {prepared.Before}\n" +
                    string.Join("\n", prepared.Warnings.Select(w => $"• {w}")) +
                    $"\nWygasa: {DisplayFormat.DateTime(prepared.ExpiresAtUtc)}";
                StatusMessage = "Akcja przygotowana — sprawdź opis i potwierdź. Nic jeszcze nie zostało zmienione.";
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() =>
                StatusMessage = FormatError("Nie udało się przygotować akcji", ex));
        }
    }

    private async Task ConfirmActionAsync()
    {
        var pending = _pendingAction;
        if (pending is null)
        {
            return;
        }

        try
        {
            var result = await _api.ExecuteActionAsync(
                new ActionExecuteRequest(pending.ConfirmationToken, true, ActionReasonText))
                .ConfigureAwait(false);

            _dispatcher.Post(() =>
            {
                ClearPendingAction();
                TemporaryPasswordText = result.TemporaryPassword is { Length: > 0 } password
                    ? $"Hasło tymczasowe (pokaż tylko raz): {password}"
                    : null;
                StatusMessage = $"Wykonano {result.Action}: {result.Message} · wpis audytu {result.AuditId}";
            });

            await ReloadAuditAsync(CancellationToken.None).ConfigureAwait(false);
            await ReloadComputersAsync(CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            // Kryterium M4: awaria źródła jest pokazana wraz ze szczegółem,
            // a nie jako „sukces”. Wpis audytu o porażce też jest pobierany od nowa.
            _dispatcher.Post(() =>
            {
                ClearPendingAction();
                StatusMessage = FormatError("Akcja nie powiodła się", ex);
            });

            await ReloadAuditAsync(CancellationToken.None).ConfigureAwait(false);
        }
    }

    private Task CancelActionAsync()
    {
        ClearPendingAction();
        StatusMessage = "Anulowano przygotowaną akcję. Nic nie zostało zmienione.";
        return Task.CompletedTask;
    }

    private void ClearPendingAction()
    {
        _pendingAction = null;
        CanConfirmAction = false;
        PendingActionText = null;
    }

    private void ApplyClassrooms(IReadOnlyList<ClassroomDto> classrooms)
    {
        var scoped = _classroomScope is { } scope
            ? classrooms
                .Where(c => scope.Any(s => string.Equals(s, c.Id, StringComparison.OrdinalIgnoreCase)))
                .ToList()
            : classrooms;

        var selectedId = _selectedClassroom?.Id;
        if (selectedId is null && Classrooms.Count > 0)
        {
            selectedId = Classrooms[0].Id;
        }

        _suppressSelectionReload = true;
        try
        {
            Classrooms.Clear();
            Classrooms.Add(ClassroomItemViewModel.All(scoped));

            foreach (var classroom in scoped)
            {
                Classrooms.Add(ClassroomItemViewModel.From(classroom));
            }

            SelectedClassroom = selectedId is null
                ? Classrooms[0]
                : Classrooms.FirstOrDefault(c => c.Id == selectedId) ?? Classrooms[0];
        }
        finally
        {
            _suppressSelectionReload = false;
        }
    }

    private void ApplyComputers(IReadOnlyList<ComputerDto> computers)
    {
        Computers.Clear();
        foreach (var computer in computers)
        {
            Computers.Add(new ComputerItemViewModel(computer));
        }
    }

    private void ApplyLogins(IReadOnlyList<LoginEventDto> logins)
    {
        Logins.Clear();
        foreach (var login in logins)
        {
            Logins.Add(new LoginEventItemViewModel(login));
        }
    }

    private int FindComputerIndex(string name)
    {
        for (var index = 0; index < Computers.Count; index++)
        {
            if (string.Equals(Computers[index].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private void RaiseAllCanExecute()
    {
        RefreshCommand.RaiseCanExecuteChanged();
        RestartCommand.RaiseCanExecuteChanged();
        LockCommand.RaiseCanExecuteChanged();
        ResetPasswordCommand.RaiseCanExecuteChanged();
        AddToGroupCommand.RaiseCanExecuteChanged();
        ConfirmActionCommand.RaiseCanExecuteChanged();
        CancelActionCommand.RaiseCanExecuteChanged();
        RefreshAuditCommand.RaiseCanExecuteChanged();
    }

    private static string FormatError(string prefix, Exception ex) =>
        ex is ServerErrorException serverError
            ? $"{prefix}: {serverError.Message}" +
              (string.IsNullOrWhiteSpace(serverError.Detail) ? string.Empty : $" ({serverError.Detail})")
            : $"{prefix}: {ex.Message}";

    private static string BuildFreshnessText(DataFreshnessDto computers, DataFreshnessDto? logins)
    {
        var parts = new List<string>
        {
            $"komputery: {DisplayFormat.Time(computers.ObservedAtUtc)} ({DisplayFormat.Completeness(computers.Completeness)}{(computers.IsStale ? ", nieaktualne" : string.Empty)})"
        };

        if (logins is not null)
        {
            parts.Add($"logowania: {DisplayFormat.Time(logins.ObservedAtUtc)} ({DisplayFormat.Completeness(logins.Completeness)}{(logins.IsStale ? ", nieaktualne" : string.Empty)})");
        }

        return "Dane · " + string.Join(" · ", parts);
    }
}
