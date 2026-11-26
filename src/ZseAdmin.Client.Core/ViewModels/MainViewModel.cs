using System.Collections.ObjectModel;
using ZseAdmin.Client.Core.Api;
using ZseAdmin.Client.Core.Connection;
using ZseAdmin.Client.Core.Display;
using ZseAdmin.Client.Core.Mvvm;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;

namespace ZseAdmin.Client.Core.ViewModels;

/// <summary>
/// Widok główny: pracownie → komputery → logowania, plus zakładki „Konto AD” (M4) i
/// „Audyt” (M3/M4), pasek zaufania do danych i akcje na stacjach.
/// Wątek UI jest jedynym miejscem zmiany kolekcji, dlatego każda aktualizacja idzie przez <see cref="IUiDispatcher"/>.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const int RecentLoginsTake = 60;

    private readonly ZseAdminApiClient _api;
    private readonly WsClientConnection _connection;
    private readonly IUiDispatcher _dispatcher;
    private readonly IUserInteraction _interaction;

    private ClassroomItemViewModel? _selectedClassroom;
    private ComputerItemViewModel? _selectedComputer;
    private string _connectionStatusText = "brak połączenia";
    private string _freshnessText = "Dane: —";
    private string? _statusMessage = "Łączenie z serwerem…";
    private bool _isBusy;
    private bool _suppressSelectionReload;
    private bool _canControlComputers;
    private bool _canWriteUsers;
    private bool _canReadAudit;
    private string _userSearchText = string.Empty;
    private UserDetailsViewModel? _foundUser;
    private string _auditFilter = string.Empty;

    public MainViewModel(
        ZseAdminApiClient api,
        WsClientConnection connection,
        IUiDispatcher dispatcher,
        IUserInteraction interaction)
    {
        _api = api;
        _connection = connection;
        _dispatcher = dispatcher;
        _interaction = interaction;

        RefreshCommand = new AsyncRelayCommand(() => ReloadAllAsync(CancellationToken.None), () => !IsBusy);
        RestartComputerCommand = new AsyncRelayCommand(RestartComputerAsync, () => CanControlComputers && SelectedComputer is not null);
        LockComputerCommand = new AsyncRelayCommand(LockComputerAsync, () => CanControlComputers && SelectedComputer is not null);
        SearchUserCommand = new AsyncRelayCommand(() => SearchUserAsync(CancellationToken.None), () => !string.IsNullOrWhiteSpace(UserSearchText));
        ResetPasswordCommand = new AsyncRelayCommand(ResetPasswordAsync, () => CanWriteUsers && FoundUser is not null);
        AddToGroupCommand = new AsyncRelayCommand(AddToGroupAsync, () => CanWriteUsers && FoundUser is not null);
        RefreshAuditCommand = new AsyncRelayCommand(RefreshAuditAsync, () => CanReadAudit);

        _connection.StateChanged += OnConnectionStateChanged;
        _api.ComputerStatusChanged += OnComputerStatusChanged;
        _api.WelcomeReceived += OnWelcomeReceived;

        // Powitanie mogło dotrzeć, zanim VM zasubskrybowała — dociągnij uprawnienia.
        if (_api.Welcome is { } welcome)
        {
            ApplyCapabilities(welcome.Capabilities);
        }
    }

    public ObservableCollection<ClassroomItemViewModel> Classrooms { get; } = new();

    public ObservableCollection<ComputerItemViewModel> Computers { get; } = new();

    public ObservableCollection<LoginEventItemViewModel> Logins { get; } = new();

    public ObservableCollection<AuditEntryItemViewModel> AuditEntries { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

    public AsyncRelayCommand RestartComputerCommand { get; }

    public AsyncRelayCommand LockComputerCommand { get; }

    public AsyncRelayCommand SearchUserCommand { get; }

    public AsyncRelayCommand ResetPasswordCommand { get; }

    public AsyncRelayCommand AddToGroupCommand { get; }

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
            if (SetProperty(ref _selectedComputer, value))
            {
                RestartComputerCommand.RaiseCanExecuteChanged();
                LockComputerCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string ConnectionStatusText
    {
        get => _connectionStatusText;
        private set => SetProperty(ref _connectionStatusText, value);
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

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                RefreshCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>M4: akcje na stacjach widoczne wyłącznie z uprawnieniem computers.control (zawsze też odrzucane serwerowo).</summary>
    public bool CanControlComputers
    {
        get => _canControlComputers;
        private set
        {
            if (SetProperty(ref _canControlComputers, value))
            {
                RestartComputerCommand.RaiseCanExecuteChanged();
                LockComputerCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>M4: przycisk „reset hasła / dodaj do grupy” widoczny wyłącznie z uprawnieniem users.write.</summary>
    public bool CanWriteUsers
    {
        get => _canWriteUsers;
        private set
        {
            if (SetProperty(ref _canWriteUsers, value))
            {
                ResetPasswordCommand.RaiseCanExecuteChanged();
                AddToGroupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    /// <summary>M3/M4: zakładka audytu widoczna wyłącznie z uprawnieniem audit.read (Administrator).</summary>
    public bool CanReadAudit
    {
        get => _canReadAudit;
        private set
        {
            if (SetProperty(ref _canReadAudit, value))
            {
                RefreshAuditCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public string UserSearchText
    {
        get => _userSearchText;
        set
        {
            if (SetProperty(ref _userSearchText, value))
            {
                SearchUserCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public UserDetailsViewModel? FoundUser
    {
        get => _foundUser;
        private set
        {
            if (SetProperty(ref _foundUser, value))
            {
                OnPropertyChanged(nameof(HasFoundUser));
                ResetPasswordCommand.RaiseCanExecuteChanged();
                AddToGroupCommand.RaiseCanExecuteChanged();
            }
        }
    }

    public bool HasFoundUser => FoundUser is not null;

    public string AuditFilter
    {
        get => _auditFilter;
        set => SetProperty(ref _auditFilter, value);
    }

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _connection.StartAsync(cancellationToken);

    private void ApplyCapabilities(IReadOnlyList<string> capabilities)
    {
        var set = new HashSet<string>(capabilities, StringComparer.Ordinal);

        _dispatcher.Post(() =>
        {
            CanControlComputers = set.Contains(Capabilities.ComputersControl);
            CanWriteUsers = set.Contains(Capabilities.UsersWrite);
            CanReadAudit = set.Contains(Capabilities.AuditRead);
        });
    }

    private void OnWelcomeReceived(object? sender, WelcomeResponse welcome) =>
        ApplyCapabilities(welcome.Capabilities);

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

            if (CanReadAudit)
            {
                _ = RefreshAuditAsync();
            }
        }
    }

    private void OnComputerStatusChanged(object? sender, ComputerStatusChangedEvent changed)
    {
        _dispatcher.Post(() =>
        {
            var selectedName = SelectedComputer?.Name;
            var index = FindComputerIndex(changed.Computer.Name);

            // Jeśli komputer nie należy do aktualnego filtra, nie dopisujemy go do listy.
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

            SelectedComputer = selectedName is null
                ? SelectedComputer
                : Computers.FirstOrDefault(c => string.Equals(c.Name, selectedName, StringComparison.OrdinalIgnoreCase));

            FreshnessText = $"Stan komputerów: {DisplayFormat.Time(changed.Computer.StatusObservedAtUtc)} · aktualizacja na żywo";
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
            var logins = await _api
                .QueryLoginsAsync(new LoginQueryRequest(Take: RecentLoginsTake), cancellationToken)
                .ConfigureAwait(false);

            _dispatcher.Post(() =>
            {
                ApplyClassrooms(classrooms.Items);
                ApplyComputers(computers.Items);
                ApplyLogins(logins.Items);
                FreshnessText = BuildFreshnessText(computers.Freshness, logins.Freshness);
                StatusMessage = $"Zaktualizowano {DateTime.Now:HH:mm:ss} · {DisplayFormat.Events(logins.TotalMatched)} w historii";
                IsBusy = false;
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() =>
            {
                StatusMessage = $"Nie udało się pobrać danych: {ex.Message}";
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
            _dispatcher.Post(() => StatusMessage = $"Nie udało się pobrać komputerów: {ex.Message}");
        }
    }

    private async Task RestartComputerAsync()
    {
        var target = SelectedComputer?.Name;
        if (target is null)
        {
            return;
        }

        if (!_interaction.AskConfirmation(
                "Restart stacji",
                $"Wymusić restart komputera {target}?\n\nNiezapisane prace uczniów zostaną utracone. Operacja zostanie zapisana w audycie."))
        {
            StatusMessage = "Restart anulowany.";
            return;
        }

        await RunActionAsync(
            $"Restart {target}",
            async () =>
            {
                var response = await _api.RunComputerActionAsync(target, ComputerAction.Restart, CancellationToken.None)
                    .ConfigureAwait(false);
                return $"Restart zlecony: {target} — {response.Message}";
            });
    }

    private async Task LockComputerAsync()
    {
        var target = SelectedComputer?.Name;
        if (target is null)
        {
            return;
        }

        if (!_interaction.AskConfirmation(
                "Blokada stacji",
                $"Zablokować ekran komputera {target}?\n\nUżytkownik musi się ponownie zalogować, aby dokończyć pracę."))
        {
            StatusMessage = "Blokada anulowana.";
            return;
        }

        await RunActionAsync(
            $"Blokada {target}",
            async () =>
            {
                var response = await _api.RunComputerActionAsync(target, ComputerAction.Lock, CancellationToken.None)
                    .ConfigureAwait(false);
                return $"Blokada zlecona: {target} — {response.Message}";
            });
    }

    private async Task SearchUserAsync(CancellationToken cancellationToken)
    {
        var sam = UserSearchText.Trim();
        if (sam.Length == 0)
        {
            StatusMessage = "Podaj samAccountName do wyszukania.";
            return;
        }

        try
        {
            var response = await _api.GetUserAsync(sam, cancellationToken).ConfigureAwait(false);
            _dispatcher.Post(() =>
            {
                FoundUser = new UserDetailsViewModel(response.User);
                StatusMessage = $"Znaleziono konto: {response.User.SamAccountName} ({DisplayFormat.Time(response.Freshness.ObservedAtUtc)}).";
            });
        }
        catch (ServerErrorException ex) when (ex.Code == ErrorCode.NotFound)
        {
            _dispatcher.Post(() =>
            {
                FoundUser = null;
                StatusMessage = $"Nie znaleziono konta '{sam}'.";
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() => StatusMessage = $"Nie udało się pobrać konta: {ex.Message}");
        }
    }

    private async Task ResetPasswordAsync()
    {
        var sam = FoundUser?.SamAccountName;
        if (sam is null)
        {
            return;
        }

        var password = _interaction.AskNewPassword(
            "Reset hasła",
            $"Nowe hasło dla konta {sam} (min. 8 znaków). Hasło nie zostanie zapisane w logach ani w audycie.");

        if (password is null)
        {
            StatusMessage = "Reset hasła anulowany.";
            return;
        }

        await RunActionAsync(
            $"Reset hasła {sam}",
            async () =>
            {
                await _api.ResetPasswordAsync(sam, password, CancellationToken.None).ConfigureAwait(false);
                return $"Hasło zmienione dla {sam}.";
            });
    }

    private async Task AddToGroupAsync()
    {
        var sam = FoundUser?.SamAccountName;
        if (sam is null)
        {
            return;
        }

        var group = _interaction.AskText(
            "Dodaj do grupy",
            $"Nazwa grupy AD, do której dodać konto {sam} (np. Klasa-3TI):");

        if (string.IsNullOrWhiteSpace(group))
        {
            StatusMessage = "Dodanie do grupy anulowane.";
            return;
        }

        group = group.Trim();

        await RunActionAsync(
            $"Grupa: {sam} → {group}",
            async () =>
            {
                var response = await _api.AddToGroupAsync(sam, group, CancellationToken.None).ConfigureAwait(false);
                return response.Changed
                    ? $"Dodano {sam} do grupy {group}."
                    : $"Konto {sam} już należy do grupy {group}.";
            });

        // Odśwież kartę konta — grupa powinna być widoczna od razu.
        await SearchUserAsync(CancellationToken.None).ConfigureAwait(false);
    }

    private async Task RefreshAuditAsync()
    {
        if (!CanReadAudit)
        {
            return;
        }

        try
        {
            var filter = string.IsNullOrWhiteSpace(AuditFilter) ? null : AuditFilter.Trim();
            var response = await _api.QueryAuditAsync(filter, take: 50, cancellationToken: CancellationToken.None)
                .ConfigureAwait(false);

            _dispatcher.Post(() =>
            {
                AuditEntries.Clear();
                foreach (var entry in response.Items)
                {
                    AuditEntries.Add(new AuditEntryItemViewModel(entry));
                }
            });
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() => StatusMessage = $"Nie udało się pobrać audytu: {ex.Message}");
        }
    }

    /// <summary>
    /// Wspólny przebieg akcji M4: sukces/porażka z serwera → pasek statusu (nigdy udawany sukces),
    /// a przy audycie — odświeżenie zakładki audytu.
    /// </summary>
    private async Task RunActionAsync(string actionName, Func<Task<string>> operation)
    {
        try
        {
            var message = await operation().ConfigureAwait(false);
            _dispatcher.Post(() => StatusMessage = message);
        }
        catch (ServerErrorException ex)
        {
            _dispatcher.Post(() => StatusMessage = $"{actionName} — nie powiodło się: {ex.Message}");
            return;
        }
        catch (Exception ex)
        {
            _dispatcher.Post(() => StatusMessage = $"{actionName} — nie powiodło się: {ex.Message}");
            return;
        }

        if (CanReadAudit)
        {
            await RefreshAuditAsync().ConfigureAwait(false);
        }
    }

    private void ApplyClassrooms(IReadOnlyList<ClassroomDto> classrooms)
    {
        var selectedId = _selectedClassroom?.Id;
        if (selectedId is null && Classrooms.Count > 0)
        {
            selectedId = Classrooms[0].Id;
        }

        _suppressSelectionReload = true;
        try
        {
            Classrooms.Clear();
            Classrooms.Add(ClassroomItemViewModel.All(classrooms));

            foreach (var classroom in classrooms)
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
        var selectedName = SelectedComputer?.Name;

        Computers.Clear();
        foreach (var computer in computers)
        {
            Computers.Add(new ComputerItemViewModel(computer));
        }

        SelectedComputer = selectedName is null
            ? null
            : Computers.FirstOrDefault(c => string.Equals(c.Name, selectedName, StringComparison.OrdinalIgnoreCase));
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
