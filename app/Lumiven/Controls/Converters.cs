using Microsoft.UI.Xaml.Data;
using Microsoft.UI.Xaml.Media.Imaging;
using Lumiven.Services;

namespace Lumiven.Controls;

public sealed class StatusConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => Localizer.T(value?.ToString() switch
    {
        "queued" => "等待下载", "downloading" => "正在下载", "uploading" => "接收视频",
        "saving" => "保存文件", "processing" => "处理中", "completed" => "已完成",
        "failed" => "下载失败", "interrupted" => "下载中断", "parsing" => "解析链接", "cancelled" => "已取消", _ => "准备中"
    });
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class LinkCancelVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is string id && Lumiven.Services.LinkDownloadService.IsLinkTask(id)
            ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class ActiveTaskConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) =>
        value is "queued" or "parsing" or "downloading" or "saving";
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class FileSizeConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language)
    {
        var size = System.Convert.ToDouble(value ?? 0);
        return size switch { >= 1073741824 => $"{size / 1073741824:0.00} GB", >= 1048576 => $"{size / 1048576:0.0} MB", >= 1024 => $"{size / 1024:0.0} KB", _ => $"{size:0} B" };
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class DateConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is DateTimeOffset date ? date.LocalDateTime.ToString("yyyy-MM-dd") : "";
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class NonEmptyVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => string.IsNullOrWhiteSpace(value?.ToString()) ? Microsoft.UI.Xaml.Visibility.Collapsed : Microsoft.UI.Xaml.Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class MissingVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => value is true ? Microsoft.UI.Xaml.Visibility.Visible : Microsoft.UI.Xaml.Visibility.Collapsed;
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class ThumbnailConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, string language)
    {
        if (value is not string path || string.IsNullOrWhiteSpace(path)) return null;
        try { return new BitmapImage(new Uri(path)); } catch (UriFormatException) { return null; }
    }
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class MissingConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => Localizer.T(value is true ? "文件已移走 · 重新扫描目录可更新" : "点击播放");
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}

public sealed class ErrorLocalizationConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, string language) => Localizer.TranslateExisting(value?.ToString() ?? "");
    public object ConvertBack(object value, Type targetType, object parameter, string language) => throw new NotSupportedException();
}
