using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Layout;
using Avalonia.Input.Platform;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using TeletextRecoveReese.Core;

namespace TeletextRecoveReese;

// Reused inside capture workflows and the modal broadcast inspection dialog.
public sealed class BroadcastServiceDataPanel : UserControl
{
    private BroadcastServiceHistory _history = new();
    private bool _live;
    private bool _dialogLayout;
    private readonly TextBlock _summary = new() { Text = "Broadcast service data" };
    private readonly TextBlock _notice = new() { TextWrapping = TextWrapping.Wrap, FontSize = 11 };
    private readonly ObservableCollection<string> _rows = new();
    private readonly ListBox _list = new() { MaxHeight = 520 };
    private readonly Expander _expander;

    public BroadcastServiceDataPanel()
    {
        var copy = new Button { Content = "Copy selected" };
        var export = new Button { Content = "Export packets CSV…" };
        _list.ItemsSource = _rows;
        _list.ItemTemplate = new FuncDataTemplate<string>((text, _) => new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
        });
        ScrollViewer.SetHorizontalScrollBarVisibility(_list, Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled);
        _expander = new Expander
        {
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Stretch,
            Header = _summary,
            Content = new StackPanel
            {
                Spacing = 6,
                Children =
                {
                    _list,
                    new StackPanel
                    {
                        Orientation = Orientation.Horizontal, Spacing = 8,
                        Children = { copy, export },
                    },
                    _notice,
                },
            },
        };
        Content = _expander;
        _expander.PropertyChanged += (_, args) =>
        {
            if (args.Property != Expander.IsExpandedProperty) return;
            if (_dialogLayout || _expander.IsExpanded) RefreshRows();
        };
        copy.Click += async (_, _) =>
        {
            if (_list.SelectedItem is string selected && TopLevel.GetTopLevel(this)?.Clipboard is { } clipboard)
            {
                try { await clipboard.SetTextAsync(selected); }
                catch (Exception ex) { _notice.Text = $"Could not copy: {ex.Message}"; }
            }
        };
        export.Click += async (_, _) =>
        {
            var top = TopLevel.GetTopLevel(this);
            if (top is null || _history.Packets.Count == 0) return;
            export.IsEnabled = false;
            try
            {
                var destination = await top.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
                {
                    Title = "Export broadcast service packets",
                    SuggestedFileName = "broadcast-service-data.csv",
                    DefaultExtension = "csv",
                    FileTypeChoices = new[] { new FilePickerFileType("CSV") { Patterns = new[] { "*.csv" } } },
                });
                if (destination is null) return;
                // Build a snapshot before yielding: live capture can keep adding packets.
                string csv = BuildCsv(_history.Packets);
                await using var output = await destination.OpenWriteAsync();
                if (output.CanSeek) output.SetLength(0);
                await using var writer = new StreamWriter(output, new UTF8Encoding(true));
                await writer.WriteAsync(csv);
                _notice.Text = "Broadcast service packets exported.";
            }
            catch (Exception ex) { _notice.Text = $"Could not export: {ex.Message}"; }
            finally { export.IsEnabled = true; }
        };
        Reset();
    }

    public void Reset(bool live = false)
    {
        _expander.IsExpanded = false;
        _live = live;
        _history = new BroadcastServiceHistory(live ? 5000 : null);
        _rows.Clear();
        Refresh();
    }

    public void UseDialogLayout(Button? closeButton = null)
    {
        if (_dialogLayout) return;
        _dialogLayout = true;
        var body = (StackPanel)_expander.Content!;
        Control[] children = body.Children.ToArray();
        body.Children.Clear();
        _expander.Content = null;
        _expander.Header = null;
        if (closeButton is not null)
        {
            Grid.SetColumn(closeButton, 1);
            closeButton.VerticalAlignment = VerticalAlignment.Bottom;
            children[2] = new Grid
            {
                ColumnDefinitions = new ColumnDefinitions("*,Auto"),
                ColumnSpacing = 8,
                Children = { children[2], closeButton },
            };
        }
        _list.ClearValue(MaxHeightProperty);
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        VerticalContentAlignment = VerticalAlignment.Stretch;
        var layout = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto,Auto"),
            RowSpacing = 8,
        };
        for (int row = 0; row < children.Length; row++)
        {
            Grid.SetRow(children[row], row);
            layout.Children.Add(children[row]);
        }
        Content = layout;
        RefreshRows();
    }

    public async Task LoadAsync(IReadOnlyList<byte[]> packets, CancellationToken cancellationToken = default)
    {
        _live = false;
        _history = await Task.Run(() =>
        {
            var history = new BroadcastServiceHistory();
            for (int index = 0; index < packets.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                history.Feed(packets[index], index + 1L);
            }
            return history;
        }, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        _rows.Clear();
        Refresh();
    }

    public void FeedBatch(IReadOnlyList<byte[]> packets, ref long packetNumber)
    {
        bool changed = false;
        foreach (var packet in packets) changed |= _history.Feed(packet, ++packetNumber);
        if (changed) Refresh();
    }

    private void Refresh()
    {
        long discarded = _history.TotalReceived - _history.Packets.Count;
        _notice.Text = $"{_history.TotalReceived:N0} service packets, in stream order. "
            + (discarded > 0 ? $"Showing the latest {_history.Packets.Count:N0}; {discarded:N0} older packets are in the captured T42 stream. " : string.Empty)
            + (_live ? "Live positions count decoded packets." : string.Empty);
        if (_dialogLayout || _expander.IsExpanded) RefreshRows();
    }

    private void RefreshRows()
    {
        int selected = _list.SelectedIndex;
        int count = _history.Packets.Count;
        if (_rows.Count > count) _rows.Clear();
        for (int index = 0; index < count; index++)
        {
            var item = _history.Packets[index];
            var data = item.Data;
            string row = $"#{item.PacketNumber:N0} · Packet 8/30 Format {data.Format} · {Timestamp(data)}\n"
                + $"Status: {Status(data)}\n"
                + $"Initial page: {data.InitialPage ?? (data.InitialPageDamaged ? "damaged" : "not specified")}"
                + (data.Format == 1
                    ? $" · Network ID: 0x{data.NetworkId:X4} · UTC offset: {OffsetText(data.LocalOffset)}\nLocal: {LocalText(data)}"
                    : string.Empty)
                + (data.StatusParityErrors > 0 ? $"\nStatus parity errors: {data.StatusParityErrors} (�)." : string.Empty);
            if (index >= _rows.Count) _rows.Add(row);
            else if (_rows[index] != row) _rows[index] = row;
        }
        if (selected >= 0 && selected < _rows.Count) _list.SelectedIndex = selected;
    }

    private static string Status(BroadcastServiceInfo data) => string.IsNullOrWhiteSpace(data.Status)
        ? "(empty status message)" : data.Status.TrimEnd();
    private static string DateText(BroadcastServiceInfo data) => data.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "date unavailable";
    private static string TimeText(BroadcastServiceInfo data) => data.UtcTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "time unavailable";
    private static string Timestamp(BroadcastServiceInfo data) => data.Format == 1
        ? $"{DateText(data)} {TimeText(data)} UTC" : "no clock in Format 2";
    private static string LocalText(BroadcastServiceInfo data) => data.LocalDateTime?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "unavailable";
    private static string OffsetText(TimeSpan? offset) => offset is { } value
        ? $"{(value < TimeSpan.Zero ? "−" : "+")}{value.Duration():hh\\:mm}" : "unavailable";

    private string BuildCsv(IReadOnlyList<BroadcastServicePacket> packets)
    {
        var text = new StringBuilder(_live ? "decoded_packet" : "packet");
        text.Append(",format,status,status_parity_errors,utc_date,utc_time,local_time,utc_offset,network_id,initial_page,initial_page_damaged\r\n");
        static string Quote(string? value) => "\"" + (value ?? string.Empty).Replace("\"", "\"\"") + "\"";
        foreach (var packet in packets)
        {
            var data = packet.Data;
            string[] cells =
            [
                packet.PacketNumber.ToString(CultureInfo.InvariantCulture), data.Format.ToString(CultureInfo.InvariantCulture),
                data.Status, data.StatusParityErrors.ToString(CultureInfo.InvariantCulture),
                data.Date?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) ?? "",
                data.UtcTime?.ToString("HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
                data.LocalDateTime?.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture) ?? "",
                data.LocalOffset is null ? "" : OffsetText(data.LocalOffset),
                data.NetworkId is { } network ? $"0x{network:X4}" : "", data.InitialPage ?? "",
                data.InitialPageDamaged.ToString(),
            ];
            text.AppendJoin(',', cells.Select(Quote)).Append("\r\n");
        }
        return text.ToString();
    }
}
