namespace OverlayHost;

using Microsoft.Extensions.Logging;
using NotifyRelay.Services.Overlay;



/// <summary>
/// 叠加层伴生进程入口。
///
/// 由 JVM 桌面端在启动时拉起、退出时终止；两者经 TCP 回环 + UTF-8 JSON 行通信，
/// 进程隔离保证 D2D 渲染异常不会影响主程序。
///
/// 用法：<c>OverlayHost.exe [端口]</c>，默认端口 45678。
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

        using var loggerFactory = LoggerFactory.Create(b => b
            .AddSimpleConsole(o => { o.SingleLine = true; o.TimestampFormat = "HH:mm:ss "; })
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
            // Start/Stop 为显式接口实现，必须经接口调用
            sink = service;
            sink.Start();
            logger.LogInformation("叠加层渲染已启动");

            channel = new OverlayChannelServer(port, sink, logger);
            channel.Start();

            // 无窗口消息循环：渲染线程由 OverlayRenderService 自带，主线程仅保持存活
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
}
