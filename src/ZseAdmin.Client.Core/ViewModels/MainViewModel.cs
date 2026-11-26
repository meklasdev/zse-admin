using System.Collections.ObjectModel;
using ZseAdmin.Client.Core.Api;
using ZseAdmin.Client.Core.Connection;
using ZseAdmin.Client.Core.Display;
using ZseAdmin.Client.Core.Mvvm;
using ZseAdmin.Contracts;
using ZseAdmin.Contracts.Protocol;

namespace ZseAdmin.Client.Core.ViewModels;

/// <summary>
/// Widok główny: pracownie → komputery → logowania, plus pasek zaufania do danych.
/// Wątek UI jest jedynym miejscem zmiany kolekcji, dlatego każda aktualizacja idzie przez <see cref="IUiDispatcher"/>.
/// </summary>
public sealed class MainViewModel : ObservableObject
{
    private const int RecentLoginsTake = 60;

    private readonly ZseAdminApiClient _api;
    private readonly WsClientConnection _connection;
    private readonly IUiDispatcher _dispatcher;

    private ClassroomItemViewModel? _selectedClassroom;
    private string _connectionStatusText = "brak połączenia";
    private string _freshnessText = "Dane: —";
    private string? _statusMessage = "Łączenie z serwerem…";
    private bool _isBusy;
    private bool _suppressSelectionReload;

    public MainViewModel(ZseAdminApiClient api, WsClientConnection connection, IUiDispatcher dispatcher)
    {
        _api = api;
        _connection = connection;
        _dispatcher = dispatcher;

        RefreshCommand = new AsyncRelayCommand(() => ReloadAllAsync(CancellationToken.None), () => !IsBusy);

        _connection.StateChanged += OnConnectionStateChanged;
        _api.ComputerStatusChanged += OnComputerStatusChanged;
    }

    public ObservableCollection<ClassroomItemViewModel> Classrooms { get; } = new();

    public ObservableCollection<ComputerItemViewModel> Computers { get; } = new();

    public ObservableCollection<LoginEventItemViewModel> Logins { get; } = new();

    public AsyncRelayCommand RefreshCommand { get; }

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

    public Task StartAsync(CancellationToken cancellationToken = default) =>
        _connection.StartAsync(cancellationToken);

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
