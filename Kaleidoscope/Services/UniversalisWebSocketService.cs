using System.Collections.Concurrent;
using System.Net.WebSockets;
using System.Text.Json;
using Dalamud.Plugin.Services;
using Kaleidoscope.Models.Universalis;
using OtterGui.Services;

namespace Kaleidoscope.Services;

/// <summary>
/// Service for connecting to the Universalis WebSocket API for real-time price updates.
/// Uses BSON serialization for messages as per Universalis specification.
/// </summary>
public sealed class UniversalisWebSocketService : IDisposable, IService
{
    private const string WebSocketUrl = "wss://universalis.app/api/ws";
    private const int ReconnectDelayMs = 5000;
    private const int MaxFeedEntries = 1000;

    private readonly IPluginLog _log;
    private readonly ConfigurationService _configService;
    private readonly UniversalisService _universalisService;

    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Task? _receiveTask;
    private bool _isConnected;
    private volatile bool _disposed;
    private DateTime _lastConnectAttempt = DateTime.MinValue;

    // Subscribed channels
    private readonly HashSet<string> _subscribedChannels = new();
    private readonly object _channelLock = new();

    // Live feed data - thread-safe circular buffer
    private readonly ConcurrentQueue<PriceFeedEntry> _liveFeed = new();
    private int _feedCount = 0;

    /// <summary>Event fired when a new price update is received.</summary>
    public event Action<PriceFeedEntry>? OnPriceUpdate;

    /// <summary>Event fired when connection state changes.</summary>
    public event Action<bool>? OnConnectionStateChanged;

    /// <summary>Gets whether the WebSocket is currently connected.</summary>
    public bool IsConnected => _isConnected;

    /// <summary>Gets the current live feed entries.</summary>
    public IEnumerable<PriceFeedEntry> LiveFeed => _liveFeed.ToArray();

    /// <summary>Gets the count of entries in the live feed.</summary>
    public int LiveFeedCount => _feedCount;

    private PriceTrackingSettings Settings => _configService.Config.PriceTracking;

    public UniversalisWebSocketService(
        IPluginLog log,
        ConfigurationService configService,
        UniversalisService universalisService)
    {
        _log = log;
        _configService = configService;
        _universalisService = universalisService;

        _log.Debug("[UniversalisWebSocket] Service initialized");
    }

    /// <summary>
    /// Starts the WebSocket connection if price tracking is enabled.
    /// </summary>
    public async Task StartAsync()
    {
        _log.Debug($"[UniversalisWebSocket] StartAsync called - disposed={_disposed}, enabled={Settings.Enabled}, connected={_isConnected}");
        
        if (_disposed)
        {
            _log.Debug("[UniversalisWebSocket] Cannot start - already disposed");
            return;
        }
        
        if (!Settings.Enabled)
        {
            _log.Debug("[UniversalisWebSocket] Price tracking is disabled, not starting");
            return;
        }

        if (_isConnected || _webSocket?.State == WebSocketState.Connecting)
        {
            _log.Debug("[UniversalisWebSocket] Already connected or connecting");
            return;
        }

        await ConnectAsync();
    }

    /// <summary>
    /// Stops the WebSocket connection.
    /// </summary>
    public async Task StopAsync()
    {
        _log.Debug("[UniversalisWebSocket] Stopping WebSocket connection");

        _cts?.Cancel();

        if (_webSocket != null)
        {
            try
            {
                if (_webSocket.State == WebSocketState.Open)
                {
                    await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Shutting down", CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                _log.Debug($"[UniversalisWebSocket] Error during close: {ex.Message}");
            }
            finally
            {
                _webSocket.Dispose();
                _webSocket = null;
            }
        }

        _isConnected = false;
        OnConnectionStateChanged?.Invoke(false);
    }

    private async Task ConnectAsync()
    {
        if (_disposed)
        {
            _log.Debug("[UniversalisWebSocket] ConnectAsync - already disposed");
            return;
        }
        
        // Rate limit connection attempts
        var now = DateTime.UtcNow;
        var msSinceLastAttempt = (now - _lastConnectAttempt).TotalMilliseconds;
        if (msSinceLastAttempt < ReconnectDelayMs)
        {
            _log.Debug($"[UniversalisWebSocket] ConnectAsync - rate limited, {msSinceLastAttempt:F0}ms since last attempt");
            return;
        }
        _lastConnectAttempt = now;

        try
        {
            _log.Debug($"[UniversalisWebSocket] Connecting to {WebSocketUrl}");

            _cts?.Cancel();
            _cts = new CancellationTokenSource();

            _webSocket?.Dispose();
            _webSocket = new ClientWebSocket();
            _webSocket.Options.SetRequestHeader("User-Agent", "Kaleidoscope-FFXIV-Plugin");

            await _webSocket.ConnectAsync(new Uri(WebSocketUrl), _cts.Token);

            _isConnected = true;
            _log.Debug("[UniversalisWebSocket] Connected successfully");
            OnConnectionStateChanged?.Invoke(true);

            // Re-subscribe to channels
            await ResubscribeChannelsAsync();

            // Start receive loop
            _receiveTask = ReceiveLoopAsync(_cts.Token);
        }
        catch (Exception ex)
        {
            _log.Warning($"[UniversalisWebSocket] Connection failed: {ex.Message}");
            _isConnected = false;
            OnConnectionStateChanged?.Invoke(false);
        }
    }

    private async Task ReceiveLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[8192];

        try
        {
            while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
            {
                using var ms = new MemoryStream();

                WebSocketReceiveResult result;
                do
                {
                    result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct);
                    ms.Write(buffer, 0, result.Count);
                } while (!result.EndOfMessage);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _log.Debug("[UniversalisWebSocket] Server closed connection");
                    break;
                }

                if (result.MessageType == WebSocketMessageType.Binary)
                {
                    // Universalis sends BSON, but we'll try to parse as JSON first
                    // since ClientWebSocket doesn't have built-in BSON support
                    await ProcessMessageAsync(ms.ToArray());
                }
                else if (result.MessageType == WebSocketMessageType.Text)
                {
                    var text = System.Text.Encoding.UTF8.GetString(ms.ToArray());
                    _log.Debug($"[UniversalisWebSocket] Received text message: {text}");
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal cancellation
        }
        catch (WebSocketException ex)
        {
            _log.Warning($"[UniversalisWebSocket] WebSocket error: {ex.Message}");
        }
        catch (Exception ex)
        {
            _log.Error($"[UniversalisWebSocket] Receive loop error: {ex.Message}");
        }
        finally
        {
            _isConnected = false;
            OnConnectionStateChanged?.Invoke(false);

            // Attempt reconnect if not cancelled
            if (!ct.IsCancellationRequested && Settings.Enabled)
            {
                _log.Debug("[UniversalisWebSocket] Scheduling reconnect");
                _ = Task.Delay(ReconnectDelayMs, CancellationToken.None).ContinueWith(_ => ConnectAsync());
            }
        }
    }

    private async Task ProcessMessageAsync(byte[] data)
    {
        try
        {
            // Try to deserialize as BSON using MongoDB.Bson
            // For now, we'll attempt JSON parsing for simpler implementation
            // The Universalis WebSocket uses BSON, but the structure is the same
            
            // Attempt to parse the event type from BSON
            var eventType = ParseEventType(data);
            if (string.IsNullOrEmpty(eventType))
            {
                _log.Debug("[UniversalisWebSocket] Could not parse event type");
                return;
            }

            _log.Debug($"[UniversalisWebSocket] Received event: {eventType}");

            switch (eventType)
            {
                case UniversalisSocketEvents.ListingsAdd:
                    ProcessListingsAdd(data);
                    break;
                case UniversalisSocketEvents.ListingsRemove:
                    ProcessListingsRemove(data);
                    break;
                case UniversalisSocketEvents.SalesAdd:
                    ProcessSalesAdd(data);
                    break;
                default:
                    _log.Debug($"[UniversalisWebSocket] Unknown event type: {eventType}");
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[UniversalisWebSocket] Error processing message: {ex.Message}");
        }

        await Task.CompletedTask;
    }

    private string? ParseEventType(byte[] data)
    {
        try
        {
            // Simple BSON parsing for event field
            // BSON format: int32 (size) + element* + \x00
            if (data.Length < 5) return null;

            var position = 4; // Skip size
            while (position < data.Length - 1)
            {
                var elementType = data[position++];
                if (elementType == 0) break; // End of document

                // Read field name (null-terminated string)
                var nameStart = position;
                while (position < data.Length && data[position] != 0) position++;
                if (position >= data.Length) return null;
                
                var fieldName = System.Text.Encoding.UTF8.GetString(data, nameStart, position - nameStart);
                position++; // Skip null terminator

                if (fieldName == "event" && elementType == 0x02) // String type
                {
                    if (position + 4 > data.Length) return null;
                    var strLen = BitConverter.ToInt32(data, position);
                    position += 4;
                    if (position + strLen > data.Length) return null;
                    return System.Text.Encoding.UTF8.GetString(data, position, strLen - 1); // -1 for null terminator
                }
                else
                {
                    // Skip this element based on type
                    position = SkipBsonElement(data, position, elementType);
                    if (position < 0) return null;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[UniversalisWebSocket] Error parsing BSON event type: {ex.Message}");
        }

        return null;
    }

    private int SkipBsonElement(byte[] data, int position, byte elementType)
    {
        try
        {
            switch (elementType)
            {
                case 0x01: // Double
                    return position + 8;
                case 0x02: // String
                case 0x0D: // JavaScript
                case 0x0E: // Symbol (deprecated)
                    if (position + 4 > data.Length) return -1;
                    var strLen = BitConverter.ToInt32(data, position);
                    return position + 4 + strLen;
                case 0x03: // Document
                case 0x04: // Array
                    if (position + 4 > data.Length) return -1;
                    var docLen = BitConverter.ToInt32(data, position);
                    return position + docLen;
                case 0x05: // Binary
                    if (position + 4 > data.Length) return -1;
                    var binLen = BitConverter.ToInt32(data, position);
                    return position + 5 + binLen;
                case 0x06: // Undefined (deprecated)
                case 0x0A: // Null
                    return position;
                case 0x07: // ObjectId
                    return position + 12;
                case 0x08: // Boolean
                    return position + 1;
                case 0x09: // DateTime
                case 0x11: // Timestamp
                case 0x12: // Int64
                    return position + 8;
                case 0x10: // Int32
                    return position + 4;
                default:
                    return -1;
            }
        }
        catch
        {
            return -1;
        }
    }

    private void ProcessListingsAdd(byte[] data)
    {
        try
        {
            var parsed = ParseBsonMessage(data);
            if (parsed == null) return;

            var itemId = GetIntField(parsed, "item");
            var worldId = GetIntField(parsed, "world");

            var listings = GetArrayField(parsed, "listings");
            if (listings == null) return;

            foreach (var listing in listings)
            {
                var entry = new PriceFeedEntry
                {
                    ReceivedAt = DateTime.UtcNow,
                    EventType = "Listing Added",
                    ItemId = itemId,
                    WorldId = worldId,
                    WorldName = GetStringField(listing, "worldName"),
                    PricePerUnit = GetIntField(listing, "pricePerUnit"),
                    Quantity = GetIntField(listing, "quantity"),
                    IsHq = GetBoolField(listing, "hq"),
                    Total = GetIntField(listing, "total"),
                    RetainerName = GetStringField(listing, "retainerName")
                };

                AddToFeed(entry);
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[UniversalisWebSocket] Error processing listings add: {ex.Message}");
        }
    }

    private void ProcessListingsRemove(byte[] data)
    {
        try
        {
            var parsed = ParseBsonMessage(data);
            if (parsed == null) return;

            var itemId = GetIntField(parsed, "item");
            var worldId = GetIntField(parsed, "world");

            var listings = GetArrayField(parsed, "listings");
            if (listings == null) return;

            foreach (var listing in listings)
            {
                var entry = new PriceFeedEntry
                {
                    ReceivedAt = DateTime.UtcNow,
                    EventType = "Listing Removed",
                    ItemId = itemId,
                    WorldId = worldId,
                    WorldName = GetStringField(listing, "worldName"),
                    PricePerUnit = GetIntField(listing, "pricePerUnit"),
                    Quantity = GetIntField(listing, "quantity"),
                    IsHq = GetBoolField(listing, "hq"),
                    Total = GetIntField(listing, "total"),
                    RetainerName = GetStringField(listing, "retainerName")
                };

                AddToFeed(entry);
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[UniversalisWebSocket] Error processing listings remove: {ex.Message}");
        }
    }

    private void ProcessSalesAdd(byte[] data)
    {
        try
        {
            var parsed = ParseBsonMessage(data);
            if (parsed == null) return;

            var itemId = GetIntField(parsed, "item");
            var worldId = GetIntField(parsed, "world");

            var sales = GetArrayField(parsed, "sales");
            if (sales == null) return;

            foreach (var sale in sales)
            {
                var entry = new PriceFeedEntry
                {
                    ReceivedAt = DateTime.UtcNow,
                    EventType = "Sale",
                    ItemId = itemId,
                    WorldId = worldId,
                    WorldName = GetStringField(sale, "worldName"),
                    PricePerUnit = GetIntField(sale, "pricePerUnit"),
                    Quantity = GetIntField(sale, "quantity"),
                    IsHq = GetBoolField(sale, "hq"),
                    Total = GetIntField(sale, "total"),
                    BuyerName = GetStringField(sale, "buyerName")
                };

                AddToFeed(entry);
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[UniversalisWebSocket] Error processing sales add: {ex.Message}");
        }
    }

    private Dictionary<string, object?>? ParseBsonMessage(byte[] data)
    {
        // Simple BSON to Dictionary parser for our specific use case
        var result = new Dictionary<string, object?>();
        
        try
        {
            if (data.Length < 5) return null;
            
            var position = 4; // Skip document size
            while (position < data.Length - 1)
            {
                var elementType = data[position++];
                if (elementType == 0) break;

                // Read field name
                var nameStart = position;
                while (position < data.Length && data[position] != 0) position++;
                if (position >= data.Length) return result;
                
                var fieldName = System.Text.Encoding.UTF8.GetString(data, nameStart, position - nameStart);
                position++;

                var (value, newPos) = ParseBsonValue(data, position, elementType);
                result[fieldName] = value;
                position = newPos;
            }
        }
        catch (Exception ex)
        {
            _log.Debug($"[UniversalisWebSocket] BSON parse error: {ex.Message}");
        }

        return result;
    }

    private (object? value, int newPosition) ParseBsonValue(byte[] data, int position, byte elementType)
    {
        try
        {
            switch (elementType)
            {
                case 0x01: // Double
                    return (BitConverter.ToDouble(data, position), position + 8);
                
                case 0x02: // String
                    var strLen = BitConverter.ToInt32(data, position);
                    var str = System.Text.Encoding.UTF8.GetString(data, position + 4, strLen - 1);
                    return (str, position + 4 + strLen);
                
                case 0x03: // Document
                    var docLen = BitConverter.ToInt32(data, position);
                    var docData = new byte[docLen];
                    Array.Copy(data, position, docData, 0, docLen);
                    return (ParseBsonMessage(docData), position + docLen);
                
                case 0x04: // Array
                    var arrLen = BitConverter.ToInt32(data, position);
                    var arrData = new byte[arrLen];
                    Array.Copy(data, position, arrData, 0, arrLen);
                    var arrDict = ParseBsonMessage(arrData);
                    var list = arrDict?.Values.ToList();
                    return (list, position + arrLen);
                
                case 0x08: // Boolean
                    return (data[position] != 0, position + 1);
                
                case 0x10: // Int32
                    return (BitConverter.ToInt32(data, position), position + 4);
                
                case 0x12: // Int64
                    return (BitConverter.ToInt64(data, position), position + 8);
                
                case 0x0A: // Null
                    return (null, position);
                
                default:
                    var skipLen = SkipBsonElement(data, position, elementType);
                    return (null, skipLen > position ? skipLen : position + 1);
            }
        }
        catch
        {
            return (null, position + 1);
        }
    }

    private int GetIntField(Dictionary<string, object?>? dict, string field)
    {
        if (dict == null || !dict.TryGetValue(field, out var val)) return 0;
        return val switch
        {
            int i => i,
            long l => (int)l,
            double d => (int)d,
            _ => 0
        };
    }

    private string? GetStringField(Dictionary<string, object?>? dict, string field)
    {
        if (dict == null || !dict.TryGetValue(field, out var val)) return null;
        return val as string;
    }

    private bool GetBoolField(Dictionary<string, object?>? dict, string field)
    {
        if (dict == null || !dict.TryGetValue(field, out var val)) return false;
        return val is bool b && b;
    }

    private List<Dictionary<string, object?>>? GetArrayField(Dictionary<string, object?>? dict, string field)
    {
        if (dict == null || !dict.TryGetValue(field, out var val)) return null;
        if (val is List<object?> list)
        {
            return list.OfType<Dictionary<string, object?>>().ToList();
        }
        return null;
    }

    private void AddToFeed(PriceFeedEntry entry)
    {
        _liveFeed.Enqueue(entry);
        Interlocked.Increment(ref _feedCount);

        // Trim to max size
        while (_feedCount > MaxFeedEntries && _liveFeed.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _feedCount);
        }

        OnPriceUpdate?.Invoke(entry);
    }

    /// <summary>
    /// Subscribes to updates for all items on specified worlds/DCs.
    /// </summary>
    public async Task SubscribeToAllAsync()
    {
        if (_disposed) return;
        
        // Subscribe to all events for configured scope
        var settings = Settings;

        switch (settings.ScopeMode)
        {
            case PriceTrackingScopeMode.All:
                await SubscribeAsync("listings/add");
                await SubscribeAsync("listings/remove");
                await SubscribeAsync("sales/add");
                break;

            case PriceTrackingScopeMode.ByWorld:
                foreach (var worldId in settings.SelectedWorldIds)
                {
                    await SubscribeAsync($"listings/add{{world={worldId}}}");
                    await SubscribeAsync($"listings/remove{{world={worldId}}}");
                    await SubscribeAsync($"sales/add{{world={worldId}}}");
                }
                break;

            // For DC and Region, we'd need to resolve to world IDs
            // This would require fetching world data first
            default:
                await SubscribeAsync("listings/add");
                await SubscribeAsync("listings/remove");
                await SubscribeAsync("sales/add");
                break;
        }
    }

    /// <summary>
    /// Subscribes to a specific channel.
    /// </summary>
    public async Task SubscribeAsync(string channel)
    {
        if (_disposed) return;
        
        lock (_channelLock)
        {
            _subscribedChannels.Add(channel);
        }

        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            var message = CreateSubscribeMessage(channel);
            await SendMessageAsync(message);
            _log.Debug($"[UniversalisWebSocket] Subscribed to: {channel}");
        }
        catch (Exception ex)
        {
            _log.Warning($"[UniversalisWebSocket] Failed to subscribe to {channel}: {ex.Message}");
        }
    }

    /// <summary>
    /// Unsubscribes from a specific channel.
    /// </summary>
    public async Task UnsubscribeAsync(string channel)
    {
        lock (_channelLock)
        {
            _subscribedChannels.Remove(channel);
        }

        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
        {
            return;
        }

        try
        {
            var message = CreateUnsubscribeMessage(channel);
            await SendMessageAsync(message);
            _log.Debug($"[UniversalisWebSocket] Unsubscribed from: {channel}");
        }
        catch (Exception ex)
        {
            _log.Warning($"[UniversalisWebSocket] Failed to unsubscribe from {channel}: {ex.Message}");
        }
    }

    private async Task ResubscribeChannelsAsync()
    {
        List<string> channels;
        lock (_channelLock)
        {
            channels = _subscribedChannels.ToList();
        }

        foreach (var channel in channels)
        {
            try
            {
                var message = CreateSubscribeMessage(channel);
                await SendMessageAsync(message);
                _log.Debug($"[UniversalisWebSocket] Re-subscribed to: {channel}");
            }
            catch (Exception ex)
            {
                _log.Warning($"[UniversalisWebSocket] Failed to re-subscribe to {channel}: {ex.Message}");
            }
        }
    }

    private byte[] CreateSubscribeMessage(string channel)
    {
        // Create BSON message: { event: "subscribe", channel: "<channel>" }
        return CreateBsonMessage("subscribe", channel);
    }

    private byte[] CreateUnsubscribeMessage(string channel)
    {
        // Create BSON message: { event: "unsubscribe", channel: "<channel>" }
        return CreateBsonMessage("unsubscribe", channel);
    }

    private byte[] CreateBsonMessage(string eventType, string channel)
    {
        using var ms = new MemoryStream();
        using var writer = new BinaryWriter(ms);

        // We'll write the BSON document manually
        // Format: int32 (size) + elements + 0x00
        
        var eventBytes = System.Text.Encoding.UTF8.GetBytes(eventType);
        var channelBytes = System.Text.Encoding.UTF8.GetBytes(channel);

        // Calculate size: 
        // 4 (size) + 1 (type) + 6 (event\0) + 4 (strlen) + eventLen + 1 (\0)
        //          + 1 (type) + 8 (channel\0) + 4 (strlen) + channelLen + 1 (\0)
        //          + 1 (end marker)
        var size = 4 + 1 + 6 + 4 + eventBytes.Length + 1 + 1 + 8 + 4 + channelBytes.Length + 1 + 1;

        writer.Write(size);

        // event field
        writer.Write((byte)0x02); // String type
        writer.Write(System.Text.Encoding.UTF8.GetBytes("event"));
        writer.Write((byte)0); // null terminator
        writer.Write(eventBytes.Length + 1); // string length including null
        writer.Write(eventBytes);
        writer.Write((byte)0);

        // channel field
        writer.Write((byte)0x02); // String type
        writer.Write(System.Text.Encoding.UTF8.GetBytes("channel"));
        writer.Write((byte)0); // null terminator
        writer.Write(channelBytes.Length + 1); // string length including null
        writer.Write(channelBytes);
        writer.Write((byte)0);

        // End marker
        writer.Write((byte)0);

        return ms.ToArray();
    }

    private async Task SendMessageAsync(byte[] message)
    {
        if (_webSocket?.State != WebSocketState.Open)
        {
            throw new InvalidOperationException("WebSocket is not connected");
        }

        await _webSocket.SendAsync(
            new ArraySegment<byte>(message),
            WebSocketMessageType.Binary,
            true,
            _cts?.Token ?? CancellationToken.None);
    }

    /// <summary>
    /// Clears the live feed buffer.
    /// </summary>
    public void ClearFeed()
    {
        while (_liveFeed.TryDequeue(out _))
        {
            Interlocked.Decrement(ref _feedCount);
        }
        _feedCount = 0;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        
        _log.Debug("[UniversalisWebSocket] Disposing service");

        // Cancel the token to stop the receive loop
        try
        {
            _cts?.Cancel();
        }
        catch (ObjectDisposedException)
        {
            // Already disposed
        }

        // Don't wait for the receive task - just abort the WebSocket
        // The cancellation token will cause the receive loop to exit
        if (_webSocket != null)
        {
            try
            {
                // Abort immediately instead of graceful close to avoid blocking
                _webSocket.Abort();
            }
            catch (Exception ex)
            {
                _log.Debug($"[UniversalisWebSocket] Error aborting WebSocket: {ex.Message}");
            }
            finally
            {
                try
                {
                    _webSocket.Dispose();
                }
                catch (Exception ex)
                {
                    _log.Debug($"[UniversalisWebSocket] Error disposing WebSocket: {ex.Message}");
                }
                _webSocket = null;
            }
        }

        try
        {
            _cts?.Dispose();
        }
        catch (Exception ex)
        {
            _log.Debug($"[UniversalisWebSocket] Error disposing CTS: {ex.Message}");
        }
        _cts = null;
        _isConnected = false;

        _log.Debug("[UniversalisWebSocket] Disposed");
    }
}
