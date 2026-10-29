namespace NotifyRelay.Services.Overlay;

using System.Text.Json;
using NotifyRelay.Models.Render;

/// <summary>
/// 叠加层消息分派：把一行 UTF-8 JSON 转成 <see cref="IOverlayDataSink"/> 调用。
///
/// 通道服务与 C ABI 导出共用本类，保证两条入口的消息族与字段语义完全一致。
/// </summary>
public static class MessageDispatcher
{
    /// <summary>按 <c>type</c> 分派一条消息。</summary>
    public static void Dispatch(IOverlayDataSink sink, string line)
    {
        using var doc = JsonDocument.Parse(line);
        var root = doc.RootElement;
        var type = root.TryGetProperty("type", out var t) ? t.GetString() : null;
        switch (type)
        {
            case "danmaku":
                sink.ShowDanmaku(
                    Str(root, "appName"),
                    Str(root, "title"),
                    Str(root, "body"),
                    Bytes(root, "iconPng"),
                    Str(root, "deviceName"),
                    StrOrNull(root, "sourceKey"));
                break;
            case "media_update":
                sink.ShowMediaCard(
                    Str(root, "deviceId"),
                    Str(root, "deviceName"),
                    Str(root, "title"),
                    Str(root, "artist"),
                    Bytes(root, "coverPng"),
                    Bool(root, "isPlaying"));
                break;
            case "media_remove":
                sink.RemoveMediaCard(Str(root, "deviceId"));
                break;
            case "style_update":
                ApplyStyle(sink, root);
                break;
            case "superisland_update":
            case "superisland_remove":
                // 超级岛：当前由本库自带模板绘制，消息体解析待 :superislandui 接入后补齐
                break;
            default:
                break;
        }
    }

    private static void ApplyStyle(IOverlayDataSink sink, JsonElement root)
    {
        if (!root.TryGetProperty("danmaku", out var d) || d.ValueKind != JsonValueKind.Object) return;
        var settings = new DanmakuStyleSettings();
        if (d.TryGetProperty("fontSizePercent", out var v)) settings.FontSizePercent = v.GetDouble();
        if (d.TryGetProperty("speed", out v)) settings.Speed = v.GetDouble();
        if (d.TryGetProperty("opacityPercent", out v)) settings.OpacityPercent = v.GetDouble();
        if (d.TryGetProperty("displayAreaPercent", out v)) settings.DisplayAreaPercent = v.GetDouble();
        if (d.TryGetProperty("density", out v)) settings.Density = v.GetInt32();
        if (d.TryGetProperty("fontFamilyName", out v)) settings.FontFamilyName = v.GetString() ?? settings.FontFamilyName;
        if (d.TryGetProperty("displayScreenMode", out v)) settings.DisplayScreenMode = v.GetInt32();
        if (d.TryGetProperty("performanceMode", out v)) settings.PerformanceMode = v.GetInt32();
        sink.UpdateStyle(settings);
    }

    private static string Str(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    private static string? StrOrNull(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

    private static bool Bool(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;

    /// <summary>读取 base64 编码的字节字段；缺失或非法时返回 null。</summary>
    private static byte[]? Bytes(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out var v) || v.ValueKind != JsonValueKind.String) return null;
        var s = v.GetString();
        if (string.IsNullOrEmpty(s)) return null;
        try { return Convert.FromBase64String(s); } catch { return null; }
    }
}
