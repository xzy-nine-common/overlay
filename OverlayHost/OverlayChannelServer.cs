namespace OverlayHost;

using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using NotifyRelay.Services.Overlay;

/// <summary>
/// 叠加层数据通道：命名管道，逐行读写 UTF-8 JSON。
///
/// 选用命名管道而非 TCP：不占端口、不经网络栈，仅本机同一会话可连；
/// 协议仍为逐行 JSON，消息族与之前一致（
/// <c>danmaku</c>、<c>media_update</c>、<c>media_remove</c>、<c>style_update</c>、<c>log</c>）。
/// </summary>
internal sealed class OverlayChannelServer : IDisposable
{
    /// <summary>管道名；JVM 侧据此连接。</summary>
    public const string DefaultPipeName = "notifyrelay-overlay";

    private IOverlayDataSink _sink;
    private readonly ILogger _logger;
    private readonly string _pipeName;
    private readonly CancellationTokenSource _cts = new();
    private readonly object _writeLock = new();

    /// <summary>当前已连接的宿主流；命名管道单客户端语义。</summary>
    private NamedPipeServerStream _pipe;

    /// <summary>待发日志缓冲：宿主尚未连接时暂存，连上后补发。</summary>
    private readonly List<string> _pendingLogs = [];
    private const int MaxPendingLogs = 200;

    public OverlayChannelServer(string pipeName, IOverlayDataSink sink, ILogger logger)
    {
        _sink = sink;
        _logger = logger;
        _pipeName = pipeName;
    }

    /// <summary>绑定渲染接收方；允许先建通道（挂日志出口）后绑渲染。</summary>
    public void BindSink(IOverlayDataSink sink) => _sink = sink;

    public void Start()
    {
        // 日志一律经本通道回传宿主（JVM），不在本进程落盘
        LogBridge.Sink = PushLog;
        _ = AcceptLoopAsync(_cts.Token);
    }

    /// <summary>等待宿主连接。命名管道每次连接后需重建实例才能再次接受。</summary>
    private async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            NamedPipeServerStream pipe;
            try
            {
                pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "创建叠加层管道失败");
                return;
            }

            try
            {
                await pipe.WaitForConnectionAsync(token);
            }
            catch (OperationCanceledException)
            {
                pipe.Dispose();
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "等待叠加层连接失败");
                pipe.Dispose();
                continue;
            }

            _pipe = pipe;
            _logger.LogInformation("叠加层管道已连接\\\\{Pipe}", _pipeName);
            FlushPendingLogs(pipe);

            await HandlePipeAsync(pipe, token);

            _pipe = null;
            try { pipe.Dispose(); } catch { }
        }
    }

    /// <summary>把一条日志写入宿主；无连接时缓冲。</summary>
    private void PushLog(int level, string message)
    {
        if (message == null) return;
        var line = JsonSerializer.Serialize(new { type = "log", level, message });
        var pipe = _pipe;
        if (pipe is not { IsConnected: true })
        {
            lock (_pendingLogs)
            {
                if (_pendingLogs.Count >= MaxPendingLogs) _pendingLogs.RemoveAt(0);
                _pendingLogs.Add(line);
            }
            return;
        }
        WriteLine(pipe, line);
    }

    /// <summary>宿主连上后补发积压日志。</summary>
    private void FlushPendingLogs(NamedPipeServerStream pipe)
    {
        List<string> pending;
        lock (_pendingLogs)
        {
            if (_pendingLogs.Count == 0) return;
            pending = [.. _pendingLogs];
            _pendingLogs.Clear();
        }
        foreach (var line in pending) WriteLine(pipe, line);
    }

    /// <summary>向管道写一行；异常时静默（连接可能已断）。</summary>
    private void WriteLine(NamedPipeServerStream pipe, string line)
    {
        var bytes = Encoding.UTF8.GetBytes(line + "\n");
        lock (_writeLock)
        {
            try
            {
                pipe.Write(bytes, 0, bytes.Length);
                pipe.Flush();
            }
            catch
            {
                // 连接已断：读取侧会结束循环
            }
        }
    }

    /// <summary>读取宿主推送的消息并分派。</summary>
    private async Task HandlePipeAsync(NamedPipeServerStream pipe, CancellationToken token)
    {
        using var reader = new StreamReader(pipe, Encoding.UTF8, false, 8192, leaveOpen: true);
        while (!token.IsCancellationRequested && pipe.IsConnected)
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
                _logger.LogWarning(ex, "读取叠加层管道失败");
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
    }

    /// <summary>按 <c>type</c> 分派一条消息。</summary>
    private void Dispatch(string line)
    {
        var sink = _sink;
        if (sink == null) return;
        MessageDispatcher.Dispatch(sink, line);
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _pipe?.Dispose(); } catch { }
        _cts.Dispose();
    }
}
