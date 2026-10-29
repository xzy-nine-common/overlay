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
        // 日志一律经本通道回传宿主（JVM），不在本进程落盘
        LogBridge.Sink = PushLog;
        _ = AcceptLoopAsync(_cts.Token);
    }

    /// <summary>把一条日志写入所有已连接宿主。</summary>
    private void PushLog(int level, string message)
    {
        if (message == null) return;
        WriteLineToAll(JsonSerializer.Serialize(new { type = "log", level, message }));
    }

    /// <summary>向所有已连接客户端写一行；无连接时丢弃。</summary>
    private void WriteLineToAll(string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        lock (_clients)
        {
            foreach (var c in _clients)
            {
                try
                {
                    var s = c.GetStream();
                    s.Write(bytes, 0, bytes.Length);
                    s.Flush();
                }
                catch
                {
                    // 连接已断：读取侧会移除
                }
            }
        }
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

    /// <summary>按 <c>type</c> 分派一条消息（与 C ABI 入口共用同一实现）。</summary>
    private void Dispatch(string line) => MessageDispatcher.Dispatch(_sink, line);

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