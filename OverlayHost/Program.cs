namespace OverlayHost;

using Microsoft.Extensions.Logging;
using NotifyRelay.Services.Overlay;

/// <summary>
/// 叠加层伴生进程入口，由 JVM 桌面端在启动时拉起、退出时终止。
///
/// 与主程序的两条连接：
/// 1. TCP 回环 + UTF-8 JSON 行（数据面）；
/// 2. 日志经 <see cref="LogBridge"/> 的 C ABI 回调交给 JVM（与 Rust `log_bridge` 同构）。
///
/// 用法：<c>OverlayHost.exe [端口]</c>，默认 45678。
/// 进程为 WinExe，无控制台窗口。
/// </summary>
internal static class Program
{
    private const int DefaultPort = 45678;

    [STAThread]
    private static int Main(string[] args)
    {
        var port = DefaultPort;
        if (args.Length > 0 && int.TryParse(args[0], out var parsed) && parsed is > 0 and < 65536)
        {
            port = parsed;
        }

        // 日志一律经 LogBridge 转发给 JVM；未安装回调时静默丢弃，不写控制台或文件
        using var loggerFactory = LoggerFactory.Create(b => b
            .ClearProviders()
            .AddProvider(new BridgeLoggerProvider())
            .SetMinimumLevel(LogLevel.Information));
        var logger = loggerFactory.CreateLogger("OverlayHost");

        OverlayRenderService service = null;
        IOverlayDataSink sink = null;
        OverlayChannelServer channel = null;
        try
        {
            service = new OverlayRenderService(
                loggerFactory.CreateLogger<OverlayRenderService>(),
                new DefaultOverlaySettings());
            sink = service;
            sink.Start();
            logger.LogInformation("叠加层渲染已启动");

            channel = new OverlayChannelServer(port, sink, logger);
            channel.Start();

            using var exit = new ManualResetEventSlim(false);
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; exit.Set(); };
            AppDomain.CurrentDomain.ProcessExit += (_, _) => exit.Set();
            exit.Wait();
            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "叠加层宿主启动失败");
            return 1;
        }
        finally
        {
            channel?.Dispose();
            try { sink?.Stop(); } catch { }
            try { service?.Dispose(); } catch { }
        }
    }

    /// <summary>把 ILogger 输出接到 <see cref="LogBridge"/> 的日志提供者。</summary>
    private sealed class BridgeLoggerProvider : ILoggerProvider
    {
        public ILogger CreateLogger(string categoryName) => LogBridge.CreateLogger(categoryName);

        public void Dispose() { }
    }
}
