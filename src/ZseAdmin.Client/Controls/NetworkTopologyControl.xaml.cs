using System.Collections;
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ZseAdmin.Client.Core.ViewModels;
using ZseAdmin.Contracts;

namespace ZseAdmin.Client.Controls;

public partial class NetworkTopologyControl : UserControl
{
    private const double CanvasWidth = 820;
    private const double GroupLeft = 15;
    private const double GroupWidth = 790;
    private const double SwitchLeft = 200;
    private const double SwitchWidth = 150;
    private const double NetworkBusX = 175;
    private const double WorkstationBusX = 380;
    private const double WorkstationLeft = 395;
    private const double WorkstationStep = 180;
    private const double WorkstationWidth = 160;
    private const double WorkstationHeight = 66;
    private const double RowStep = 88;
    private const int WorkstationColumns = 2;

    public static readonly DependencyProperty ComputersProperty = DependencyProperty.Register(
        nameof(Computers),
        typeof(IEnumerable),
        typeof(NetworkTopologyControl),
        new FrameworkPropertyMetadata(null, OnComputersChanged));

    public static readonly DependencyProperty SelectedComputerProperty = DependencyProperty.Register(
        nameof(SelectedComputer),
        typeof(ComputerItemViewModel),
        typeof(NetworkTopologyControl),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnSelectionChanged));

    private INotifyCollectionChanged? _observableComputers;

    public NetworkTopologyControl()
    {
        InitializeComponent();
        Loaded += (_, _) => RenderTopology();
        SizeChanged += (_, _) => RenderTopology();
    }

    public IEnumerable? Computers
    {
        get => (IEnumerable?)GetValue(ComputersProperty);
        set => SetValue(ComputersProperty, value);
    }

    public ComputerItemViewModel? SelectedComputer
    {
        get => (ComputerItemViewModel?)GetValue(SelectedComputerProperty);
        set => SetValue(SelectedComputerProperty, value);
    }

    private static void OnComputersChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args)
    {
        var control = (NetworkTopologyControl)owner;
        if (control._observableComputers is not null)
        {
            control._observableComputers.CollectionChanged -= control.OnCollectionChanged;
        }

        control._observableComputers = args.NewValue as INotifyCollectionChanged;
        if (control._observableComputers is not null)
        {
            control._observableComputers.CollectionChanged += control.OnCollectionChanged;
        }

        control.RenderTopology();
    }

    private static void OnSelectionChanged(DependencyObject owner, DependencyPropertyChangedEventArgs args) =>
        ((NetworkTopologyControl)owner).RenderTopology();

    private void OnCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e) => RenderTopology();

    private void RenderTopology()
    {
        if (DiagramCanvas is null || !IsLoaded)
        {
            return;
        }

        DiagramCanvas.Children.Clear();
        var computers = (Computers?.Cast<object>() ?? Enumerable.Empty<object>())
            .OfType<ComputerItemViewModel>()
            .ToList();

        if (computers.Count == 0)
        {
            DiagramCanvas.Width = CanvasWidth;
            DiagramCanvas.Height = 250;
            AddText("Brak komputerów do pokazania. Wybierz pracownię albo odśwież dane.", 32, 40, 600, 30,
                "Text.Body", FindBrush("Brush.TextMuted"));
            return;
        }

        var groups = computers
            .GroupBy(item => item.ClassroomName, StringComparer.CurrentCultureIgnoreCase)
            .OrderBy(group => group.Key.Equals("poza pracownią", StringComparison.CurrentCultureIgnoreCase) ? 1 : 0)
            .ThenBy(group => group.Key, StringComparer.CurrentCultureIgnoreCase)
            .Select(group => new { Name = group.Key, Items = group.OrderBy(item => item.Name).ToList() })
            .ToList();

        var groupTopPositions = new List<(string Name, List<ComputerItemViewModel> Items, double Top, double Height)>();
        var nextTop = 112d;
        foreach (var group in groups)
        {
            var rows = Math.Max(1, (group.Items.Count + WorkstationColumns - 1) / WorkstationColumns);
            var height = 46 + rows * RowStep + 10;
            groupTopPositions.Add((group.Name, group.Items, nextTop, height));
            nextTop += height + 12;
        }

        DiagramCanvas.Width = CanvasWidth;
        DiagramCanvas.Height = Math.Max(300, nextTop + 6);

        DrawCoreNode();
        DrawNetworkBackbone(groupTopPositions);

        foreach (var group in groupTopPositions)
        {
            DrawRoom(group.Name, group.Items, group.Top, group.Height);
        }

        AddText($"{groups.Count} pracownie · {computers.Count} komputerów", 20, 78, 340, 20,
            "Text.Meta", FindBrush("Brush.TextMuted"));
    }

    private void DrawCoreNode()
    {
        AddNodeShell(315, 18, 190, 54,
            "Rdzeń sieci szkolnej",
            "Punkt dystrybucji · podgląd",
            "↔",
            FindBrush("Brush.AccentSubtle"),
            FindBrush("Brush.Accent"));
    }

    private void DrawNetworkBackbone(
        IReadOnlyList<(string Name, List<ComputerItemViewModel> Items, double Top, double Height)> groups)
    {
        var lastSwitchCenterY = groups[^1].Top + 34 + 28;

        AddLine(410, 72, 410, 91, FindBrush("Brush.BorderStrong"), 2);
        AddLine(NetworkBusX, 91, 410, 91, FindBrush("Brush.BorderStrong"), 2);
        AddLine(NetworkBusX, 91, NetworkBusX, lastSwitchCenterY, FindBrush("Brush.BorderStrong"), 2);

        foreach (var group in groups)
        {
            var switchCenterY = group.Top + 34 + 28;
            AddLine(NetworkBusX, switchCenterY, SwitchLeft, switchCenterY, FindBrush("Brush.BorderStrong"), 2);

            var rowCount = Math.Max(1, (group.Items.Count + WorkstationColumns - 1) / WorkstationColumns);
            var firstRowCenterY = group.Top + 34 + WorkstationHeight / 2;
            var lastRowCenterY = firstRowCenterY + (rowCount - 1) * RowStep;
            AddLine(SwitchLeft + SwitchWidth, switchCenterY,
                WorkstationBusX, switchCenterY, FindBrush("Brush.BorderStrong"), 2);
            AddLine(WorkstationBusX, Math.Min(switchCenterY, firstRowCenterY),
                WorkstationBusX, Math.Max(switchCenterY, lastRowCenterY), FindBrush("Brush.BorderStrong"), 2);

            for (var row = 0; row < rowCount; row++)
            {
                var rowItems = group.Items.Skip(row * WorkstationColumns).Take(WorkstationColumns).ToList();
                if (rowItems.Count == 0)
                {
                    continue;
                }

                var rowY = firstRowCenterY + row * RowStep;
                foreach (var item in rowItems)
                {
                    var column = group.Items.IndexOf(item) % WorkstationColumns;
                    var nodeLeft = WorkstationLeft + column * WorkstationStep;
                    var wireBrush = StatusBrush(item.Status);
                    AddLine(WorkstationBusX, rowY, nodeLeft, rowY, wireBrush, 2);
                }
            }
        }
    }

    private void DrawRoom(string name, IReadOnlyList<ComputerItemViewModel> computers, double top, double height)
    {
        var roomSurface = new Border
        {
            Width = GroupWidth,
            Height = height,
            BorderThickness = new Thickness(1),
            BorderBrush = FindBrush("Brush.Border"),
            Background = FindBrush("Brush.Surface"),
            CornerRadius = new CornerRadius(6),
            IsHitTestVisible = false
        };
        Canvas.SetLeft(roomSurface, GroupLeft);
        Canvas.SetTop(roomSurface, top);
        Panel.SetZIndex(roomSurface, 0);
        DiagramCanvas.Children.Add(roomSurface);

        AddText(name, GroupLeft + 12, top + 7, 300, 18, "Text.Label", FindBrush("Brush.Text"), FontWeights.SemiBold);
        AddText($"{computers.Count} stanowisk", GroupLeft + GroupWidth - 120, top + 7, 105, 18,
            "Text.Meta", FindBrush("Brush.TextMuted"), horizontalAlignment: HorizontalAlignment.Right);

        AddNodeShell(SwitchLeft, top + 34, SwitchWidth, 56,
            "Przełącznik",
            "segment pracowni",
            "⇄",
            FindBrush("Brush.SurfaceMuted"),
            FindBrush("Brush.TextMuted"));

        for (var index = 0; index < computers.Count; index++)
        {
            var item = computers[index];
            var row = index / WorkstationColumns;
            var column = index % WorkstationColumns;
            var left = WorkstationLeft + column * WorkstationStep;
            var itemTop = top + 34 + row * RowStep;
            AddComputerNode(item, left, itemTop);
        }
    }

    private void AddComputerNode(ComputerItemViewModel computer, double left, double top)
    {
        var isSelected = string.Equals(SelectedComputer?.Name, computer.Name, StringComparison.OrdinalIgnoreCase);
        var statusBrush = StatusBrush(computer.Status);
        var node = new Border
        {
            Width = WorkstationWidth,
            Height = WorkstationHeight,
            Padding = new Thickness(8, 6, 8, 5),
            Background = isSelected ? FindBrush("Brush.AccentSubtle") : FindBrush("Brush.Surface"),
            BorderBrush = isSelected ? FindBrush("Brush.Accent") : FindBrush("Brush.Border"),
            BorderThickness = new Thickness(isSelected ? 2 : 1),
            CornerRadius = new CornerRadius(5),
            Cursor = Cursors.Hand,
            ToolTip = $"{computer.Name} · {computer.StatusText} · {computer.IpAddress}"
        };

        var content = new StackPanel();
        var heading = new TextBlock
        {
            Text = computer.Name,
            Style = (Style)FindResource("Text.Label"),
            Foreground = FindBrush("Brush.Text"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var address = new TextBlock
        {
            Text = computer.IpAddress,
            Style = (Style)FindResource("Text.Meta"),
            Margin = new Thickness(0, 1, 0, 2),
            TextTrimming = TextTrimming.CharacterEllipsis
        };
        var state = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        state.Children.Add(new Ellipse
        {
            Width = 7,
            Height = 7,
            Fill = statusBrush,
            Margin = new Thickness(0, 0, 5, 0),
            VerticalAlignment = VerticalAlignment.Center
        });
        state.Children.Add(new TextBlock
        {
            Text = computer.StatusText,
            Style = (Style)FindResource("Text.Meta"),
            Foreground = statusBrush,
            FontWeight = FontWeights.SemiBold
        });
        content.Children.Add(heading);
        content.Children.Add(address);
        content.Children.Add(state);
        node.Child = content;
        node.MouseLeftButtonUp += (_, args) =>
        {
            SelectedComputer = computer;
            args.Handled = true;
        };

        Canvas.SetLeft(node, left);
        Canvas.SetTop(node, top);
        Panel.SetZIndex(node, 3);
        DiagramCanvas.Children.Add(node);
    }

    private void AddNodeShell(
        double left,
        double top,
        double width,
        double height,
        string title,
        string detail,
        string glyph,
        Brush background,
        Brush foreground)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        var icon = new Border
        {
            Width = 30,
            Height = 30,
            Margin = new Thickness(0, 0, 9, 0),
            Background = background,
            CornerRadius = new CornerRadius(5),
            Child = new TextBlock
            {
                Text = glyph,
                Foreground = foreground,
                FontSize = 17,
                FontWeight = FontWeights.SemiBold,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center
            }
        };
        Grid.SetColumn(icon, 0);
        var texts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
        texts.Children.Add(new TextBlock
        {
            Text = title,
            Style = (Style)FindResource("Text.Label"),
            Foreground = FindBrush("Brush.Text"),
            FontWeight = FontWeights.SemiBold,
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        texts.Children.Add(new TextBlock
        {
            Text = detail,
            Style = (Style)FindResource("Text.Meta"),
            TextTrimming = TextTrimming.CharacterEllipsis
        });
        Grid.SetColumn(texts, 1);
        grid.Children.Add(icon);
        grid.Children.Add(texts);

        var shell = new Border
        {
            Width = width,
            Height = height,
            Padding = new Thickness(9, 6, 9, 6),
            Background = FindBrush("Brush.Surface"),
            BorderBrush = FindBrush("Brush.BorderStrong"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6),
            Child = grid,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(shell, left);
        Canvas.SetTop(shell, top);
        Panel.SetZIndex(shell, 2);
        DiagramCanvas.Children.Add(shell);
    }

    private void AddText(
        string text,
        double left,
        double top,
        double width,
        double height,
        string styleKey,
        Brush foreground,
        FontWeight? fontWeight = null,
        HorizontalAlignment horizontalAlignment = HorizontalAlignment.Left)
    {
        var block = new TextBlock
        {
            Text = text,
            Width = width,
            Height = height,
            Style = (Style)FindResource(styleKey),
            Foreground = foreground,
            FontWeight = fontWeight ?? FontWeights.Normal,
            TextTrimming = TextTrimming.CharacterEllipsis,
            VerticalAlignment = VerticalAlignment.Center,
            HorizontalAlignment = horizontalAlignment,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(block, left);
        Canvas.SetTop(block, top);
        Panel.SetZIndex(block, 4);
        DiagramCanvas.Children.Add(block);
    }

    private void AddLine(double x1, double y1, double x2, double y2, Brush brush, double thickness)
    {
        var line = new Line
        {
            X1 = x1,
            Y1 = y1,
            X2 = x2,
            Y2 = y2,
            Stroke = brush,
            StrokeThickness = thickness,
            SnapsToDevicePixels = true,
            IsHitTestVisible = false
        };
        Panel.SetZIndex(line, 1);
        DiagramCanvas.Children.Add(line);
    }

    private Brush StatusBrush(ComputerStatus status) => status switch
    {
        ComputerStatus.Online => FindBrush("Brush.StatusOk"),
        ComputerStatus.Offline => FindBrush("Brush.StatusError"),
        _ => FindBrush("Brush.StatusWarn")
    };

    private Brush FindBrush(string key) => (Brush)FindResource(key);
}
