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
            case "image_overlay_update":
                sink.ShowImageOverlay(
                    Str(root, "id"),
                    Float(root, "x"),
                    Float(root, "y"),
                    Float(root, "width"),
                    Float(root, "height"),
                    Bytes(root, "png"),
                    FloatOr(root, "opacity", 1f));
                break;
            case "image_overlay_remove":
                sink.RemoveImageOverlay(Str(root, "id"));
                break;
            case "style_update":
                ApplyStyle(sink, root);
                break;
            case "superisland_update":
                DispatchSuperIsland(sink, root);
                break;
            case "superisland_remove":
                sink.RemoveSuperIsland(Str(root, "sourceId"));
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// 分派超级岛更新。
    ///
    /// JVM 侧只透传原始字段：<c>paramV2Raw</c> 由本库的 ParamV2 解析器处理，
    /// 无需在 JVM 侧重复实现超级岛布局解析。
    /// </summary>
    private static void DispatchSuperIsland(IOverlayDataSink sink, JsonElement root)
    {
        var state = new SuperIslandState
        {
            Title = StrOrNull(root, "title"),
            Subtitle = StrOrNull(root, "subtitle"),
            AdditionalText = StrOrNull(root, "text"),
            Extra = StrOrNull(root, "extra"),
            ParamV2Raw = StrOrNull(root, "paramV2Raw"),
            ChangesJson = StrOrNull(root, "changes"),
            IconPng = Bytes(root, "iconPng"),
            Pics = Pics(root),
            Progress = Int(root, "progress"),
            TimerType = (TimerType)Int(root, "timerType"),
            TimerValue = Long(root, "timerValue"),
            TimerStartTime = Long(root, "timerStartTime"),
            PausedSeconds = Long(root, "pausedSeconds"),
            LastUpdateTime = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };
        // paramV2Raw 需在此解析一次，渲染侧依赖 ParamV2 派生属性
        SuperIslandParamV2Parser.ApplyToState(state, state.ParamV2Raw);
        sink.ShowSuperIsland(Str(root, "sourceId"), Str(root, "deviceName"), state);
    }

    private static int Int(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt32(out var i) ? i : 0;

    private static float FloatOr(JsonElement root, string name, float fallback) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f) ? f : fallback;

    private static float Float(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetSingle(out var f) ? f : 0f;

    private static long Long(JsonElement root, string name) =>
        root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var l) ? l : 0;

    /// <summary>读取 pics 映射；非对象或空时返回 null。</summary>
    private static Dictionary<string, string>? Pics(JsonElement root)
    {
        if (!root.TryGetProperty("pics", out var v) || v.ValueKind != JsonValueKind.Object) return null;
        var map = new Dictionary<string, string>();
        foreach (var p in v.EnumerateObject())
        {
            if (p.Value.ValueKind == JsonValueKind.String)
            {
                var s = p.Value.GetString();
                if (!string.IsNullOrEmpty(s)) map[p.Name] = s;
            }
        }
        return map.Count == 0 ? null : map;
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
