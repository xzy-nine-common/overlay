using NotifyRelay.Models.Render;
using Vortice.Direct2D1;
using Vortice.Mathematics;

namespace NotifyRelay.Services.Overlay;

/// <summary>
/// 宿主渲染位图的合成：界面由宿主绘制后以 PNG 推送，本库按屏幕坐标贴图。
/// </summary>
public partial class OverlayRenderService
{
    /// <summary>合成全部位图叠加项。</summary>
    private void RenderImageOverlays(ID2D1DCRenderTarget rt)
    {
        List<ImageOverlayItem> items;
        if (!Monitor.TryEnter(_lock, 2000)) return;
        try
        {
            if (_imageOverlays.Count == 0) return;
            items = [.. _imageOverlays.Where(m => m.Active)];
        }
        finally
        {
            Monitor.Exit(_lock);
        }

        foreach (var item in items)
        {
            if (item.Png == null || item.Width <= 0 || item.Height <= 0) continue;
            if (item.Bitmap == null || item.Dirty)
            {
                try
                {
                    var fresh = LoadBitmapFromPng(item.Png, rt);
                    if (item.Bitmap != null) DeferDispose(item.Bitmap);
                    item.Bitmap = fresh;
                    item.Dirty = false;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "位图叠加加载失败, id={Id}", item.Id);
                    continue;
                }
            }

            var bitmap = item.Bitmap;
            if (bitmap == null) continue;
            var destination = new Rect(item.X, item.Y, item.X + item.Width, item.Y + item.Height);
            var source = new Rect(0, 0, bitmap.Size.Width, bitmap.Size.Height);
            // 与弹幕同一重载：目标矩形 + 不透明度 + 插值 + 源矩形
            rt.DrawBitmap(bitmap, destination, item.Opacity, BitmapInterpolationMode.Linear, source);
        }
    }
}
