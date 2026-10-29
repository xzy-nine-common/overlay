namespace OverlayHost;

using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NotifyRelay.Services.Overlay;

/// <summary>
/// 叠加层数据通道：TCP 回环监听，逐行读取 UTF-8 JSON 并转发给 <see cref="IOverlayDataSink"/>。
///
/// 消息族与字段对齐 Windows 端既有的本地中继形态：
/// <c>danmaku</c>、<c>media_update</c>、<c>media_remove</c>、<c>superisland_update</c>、
/// <c>superisland_remove</c>、<c>style_update</c>、<c>screenshot</c>。
/// </summary>
internal sealed class OverlayChannelServer : IDisposable
{
    private readonly IOverlayDataSink _sink;
    private readonly ILogger _logger;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<TcpClient> _clients = [];

    public OverlayChannelServer(int port, IOverlayDataSink sink, ILogger logger)
    {
        _sink = sink;
        _logger = logger;
        _listener = new TcpListener(IPAddress.Loopback, port);
    }

    public void Start()
    {
        _listener.Start();
        _logger.LogInformation("叠加层通道已监听 127.0.0.1:{Port}", ((IPEndPoint)_listener.LocalEndpoint).Port);
        _ = AcceptLoopAsync(_cts.Token);
    }

    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "接受叠加层连接失败");
                continue;
            }
            lock (_clients) _clients.Add(client);
            _ = HandleClientAsync(client, token);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken token)
    {
        using var _ = client;
        using var stream = client.GetStream();
        using var reader = new StreamReader(stream, Encoding.UTF8);
        while (!token.IsCancellationRequested)
        {
            string line;
            try
            {
                line = await reader.ReadLineAsync(token);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "读取叠加层通道失败");
                break;
            }
            if (line is null) break;
            if (line.Length == 0) continue;
            try
            {
                Dispatch(line);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "处理叠加层消息失败: {Line}", line);
            }
        }
        lock (_clients) _clients.Remove(client);
    }

    /// <summary>按 <c>type</c> 分派一条消息。</summary>
    private void Dispatch(string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            case "danmaku":
                _sink.ShowDanmaku(
                    Str(root, "appName"),
                    Str(root, "title"),
                    Str(root, "body"),
                    Bytes(root, "iconPng"),
                    Str(root, "deviceName"),
                    StrOrNull(root, "sourceKey"));
                break;
            case "media_update":
                _sink.ShowMediaCard(
                    Str(root, "deviceId"),
                    Str(root, "deviceName"),
                    Str(root, "title"),
                    Str(root, "artist"),
                    Bytes(root, "coverPng"),
                    Bool(root, "isPlaying"));
                break;
            case "media_remove":
                _sink.RemoveMediaCard(Str(root, "deviceId"));
                break;
            case "superisland_update":
                _logger.LogWarning("超级岛消息暂未接线（等待 :superislandui 渲染接入）");
                break;
            case "superisland_remove":
                _logger.LogWarning("超级岛移除消息暂未接线");
                break;
            case "style_update":
                ApplyStyle(root);
                break;
            default:
                _logger.LogDebug("忽略未知叠加层消息: {Type}", type);
                break;
        }
    }

    private void ApplyStyle(JsonElement root)
    {
        // 仅更新弹幕样式；其余设置项由宿主默认值提供
        if (root.TryGetProperty("danmaku", out var d) && d.ValueKind == JsonValueKind.Object)
        {
            var settings = new NotifyRelay.Models.Render.DanmakuStyleSettings();
            if (d.TryGetProperty("fontSizePercent", out var v)) settings.FontSizePercent = v.GetDouble();
            if (d.TryGetProperty("speed", out v)) settings.Speed = v.GetDouble();
            if (d.TryGetProperty("opacityPercent", out v)) settings.OpacityPercent = v.GetDouble();
            if (d.TryGetProperty("displayAreaPercent", out v)) settings.DisplayAreaPercent = v.GetDouble();
            if (d.TryGetProperty("density", out v)) settings.Density = v.GetInt32();
            if (d.TryGetProperty("fontFamilyName", out v)) settings.FontFamilyName = v.GetString() ?? settings.FontFamilyName;
            if (d.TryGetProperty("displayScreenMode", out v)) settings.DisplayScreenMode = v.GetInt32();
            if (d.TryGetProperty("performanceMode", out v)) settings.PerformanceMode = v.GetInt32();
            _sink.UpdateStyle(settings);
        }
    }

    private static string Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static string StrOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>读取 base64 编码的字节字段；缺失或非法时返回 null。</summary>
    private static byte[] Bytes(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return null;
        var s = v.GetString();
        if (string.IsNullOrEmpty(s)) return null;
        try { return Convert.FromBase64String(s); } catch { return null; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { }
        lock (_clients)
        {
            foreach (var c in _clients) { try { c.Close(); } catch { } }
            _clients.Clear();
        }
        _cts.Dispose();
    }
}
