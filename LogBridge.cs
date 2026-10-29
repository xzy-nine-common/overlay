namespace NotifyRelay.Services.Overlay;

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Extensions.Logging;

/// <summary>
/// 平台日志桥。
///
/// 与 Rust `notify-relay-core` 的 `log_bridge` 同构：库内持有唯一安装点，
/// 平台侧（JVM）注册回调后由本桥转发日志；未安装回调时静默丢弃，
/// 库自身既不写控制台也不写文件。
///
/// 回调签名 `(level, messageUtf8)`：level 为 <see cref="LogLevel"/> 的整数值，
/// message 为以 NUL 结尾的 UTF-8 串，指针仅在下一次回调前有效。
/// </summary>
public static class LogBridge
{
    /// <summary>回调指针：`void (*)(int level, const char* message)`，stdcall。</summary>
    private static nint _callback;

    /// <summary>安装/清除日志回调；传 0 表示停止转发。</summary>
    [UnmanagedCallersOnly(EntryPoint = "nr_overlay_set_log_callback", CallConvs = [typeof(System.Runtime.CompilerServices.CallConvStdcall)])]
    public static int SetCallback(nint cb)
    {
        _callback = cb;
        return 0;
    }

    /// <summary>当前是否已安装回调。</summary>
    public static bool HasCallback => _callback != 0;

    /// <summary>日志出口：由宿主装配（当前为通道回传）。</summary>
    public static Action<int, string>? Sink;

    /// <summary>转发一条日志；无出口时丢弃。</summary>
    public static void Write(int level, string message)
    {
        Sink?.Invoke(level, message);
    }

    /// <summary>经 C ABI 回调转发（保留面；当前宿主改用通道回传）。</summary>
    public static unsafe void WriteNative(int level, string message)
    {
        var cb = _callback;
        if (cb == 0 || message == null) return;
        var maxBytes = Encoding.UTF8.GetMaxByteCount(message.Length) + 1;
        var buffer = stackalloc byte[maxBytes];
        fixed (char* src = message)
        {
            var written = Encoding.UTF8.GetBytes(src, message.Length, buffer, maxBytes - 1);
            buffer[written] = 0;
            var fn = (delegate* unmanaged[Stdcall]<int, byte*, void>)cb;
            fn(level, buffer);
        }
    }

    /// <summary>创建经本桥转发的 ILogger。</summary>
    public static ILogger CreateLogger(string category) => new BridgeLogger(category);

    private sealed class BridgeLogger(string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;

        public bool IsEnabled(LogLevel logLevel) => Sink != null && logLevel != LogLevel.None;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (!IsEnabled(logLevel)) return;
            var msg = formatter(state, exception);
            if (exception != null) msg = msg + " | " + exception.GetType().Name + ": " + exception.Message;
            Write((int)logLevel, category + ": " + msg);
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }
}
