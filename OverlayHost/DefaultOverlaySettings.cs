namespace OverlayHost;

using NotifyRelay.Services.Overlay;

/// <summary>
/// 叠加层设置默认值：仅启用弹幕、媒体卡与超级岛所需的最小项。
/// 小工具（心率、键盘、罗技电池、时间、DeepSeek 余额）默认关闭，
/// 对应能力由 JSON 行通道按需开启。
/// </summary>
internal sealed class DefaultOverlaySettings : IOverlaySettings
{
    // ======== 弹幕样式 ========
    public int DanmakuFontSizePercent { get; set; } = 50;
    // 与 Windows GeneralSettingsService.Danmaku.cs 一致：默认 3，界面范围 1~20。
    // 该值直接作为 DanmakuStyleSettings.Speed，单位为 ×60 px/s（3 ≈ 180 px/s）。
    public int DanmakuSpeed { get; set; } = 3;
    public int DanmakuOpacityPercent { get; set; } = 100;
    public int DanmakuDisplayAreaPercent { get; set; } = 100;
    public int DanmakuDensity { get; set; } = 0;
    public string DanmakuFontFamily { get; set; } = "Microsoft YaHei";
    public bool DanmakuBold { get; set; } = true;
    public string DanmakuColor { get; set; } = "#FFFFFF";
    public bool DanmakuBorderEnabled { get; set; } = true;
    public int DanmakuBorderThickness { get; set; } = 2;
    public string DanmakuBorderColor { get; set; } = "#000000";
    public bool DanmakuShadowEnabled { get; set; } = true;
    public int DanmakuShadowDepth { get; set; } = 2;
    public int DanmakuShadowOpacity { get; set; } = 100;
    public string DanmakuShadowColor { get; set; } = "#000000";
    public int DanmakuDisplayScreenMode { get; set; } = 0;
    public int DanmakuPerformanceMode { get; set; } = 0;

    // ======== 心率（默认关） ========
    public bool HeartRateOverlayEnabled { get; set; } = false;
    public int HeartRateStyle { get; set; } = 0;
    public string HeartRateTargetScreen { get; set; } = "";
    public int HeartRateXPercent { get; set; } = 50;
    public int HeartRateYPercent { get; set; } = 50;
    public string HeartRateColor { get; set; } = "#FF3B30";
    public float HeartRateTextOutlineWidth { get; set; } = 3f;
    public bool HeartRateAlertEnabled { get; set; } = false;
    public int HeartRateLowAlert { get; set; } = 50;
    public int HeartRateHighAlert { get; set; } = 120;
    public int HeartRateSpikeDelta { get; set; } = 30;
    public float HeartRateScale { get; set; } = 1f;
    public bool HeartRateHideWhenDisconnected { get; set; } = true;

    // ======== 键盘（默认关） ========
    public bool KeyboardOverlayEnabled { get; set; } = false;

    // ======== 罗技电池（默认关，且不提供 DLL） ========
    public bool LogiBatteryEnabled { get; set; } = false;
    public string LogiBatteryTargetScreen { get; set; } = "";
    public int LogiBatteryXPercent { get; set; } = 90;
    public int LogiBatteryYPercent { get; set; } = 10;
    public float LogiBatteryScale { get; set; } = 1f;
    public bool LogiBatteryHideWhenDisconnected { get; set; } = true;
    public Dictionary<string, string> LogiBatteryDeviceNameOverrides { get; set; } = new();

    // ======== 时间浮窗（默认关） ========
    public bool ClockOverlayEnabled { get; set; } = false;
    public string ClockTargetScreen { get; set; } = "";
    public int ClockXPercent { get; set; } = 50;
    public int ClockYPercent { get; set; } = 5;
    public string ClockColor { get; set; } = "#FFFFFF";
    public float ClockTextOutlineWidth { get; set; } = 2f;
    public float ClockScale { get; set; } = 1f;
    public bool ClockShowSeconds { get; set; } = false;
    public bool ClockUse24Hour { get; set; } = true;

    // ======== DeepSeek 余额（默认关） ========
    public bool EnableDeepSeekBalanceMonitor { get; set; } = false;
    public string DeepSeekBalanceTargetScreen { get; set; } = "";
    public int DeepSeekBalanceXPercent { get; set; } = 10;
    public int DeepSeekBalanceYPercent { get; set; } = 90;
    public float DeepSeekBalanceScale { get; set; } = 1f;
}
