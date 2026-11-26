using Hivox.Mobile.Services;

namespace Hivox.Mobile.Models;

public class SubscriptionMetadata
{
    public string ProfileTitle { get; set; } = "";
    public long UploadBytes { get; set; }
    public long DownloadBytes { get; set; }
    public long TotalBytes { get; set; }
    public long ExpireUnix { get; set; }
    public int UpdateIntervalHours { get; set; }
    public long LastUpdatedUnix { get; set; }
    public string SupportUrl { get; set; } = "";
    public string WebPageUrl { get; set; } = "";
    public string CustomName { get; set; } = "";
    public bool ShowOnHome { get; set; } = true;
    public bool AlwaysExpanded { get; set; }
    public bool UseCustomInterval { get; set; }
    public int CustomIntervalMinutes { get; set; } = 120;
    public string CustomDns { get; set; } = "";

    public bool HasTrafficInfo => TotalBytes > 0 || UploadBytes > 0 || DownloadBytes > 0;

    public double TrafficRatio => TotalBytes > 0
        ? Math.Clamp((double)(UploadBytes + DownloadBytes) / TotalBytes, 0.0, 1.0)
        : 0.0;

    public string FormattedTraffic
    {
        get
        {
            long used = UploadBytes + DownloadBytes;
            if (TotalBytes > 0)
                return $"{NetworkUtils.FormatBytes(used)} / {NetworkUtils.FormatBytes(TotalBytes)}";
            if (used > 0)
                return $"{NetworkUtils.FormatBytes(used)} / ∞";
            return "";
        }
    }

    public string FormattedExpire
    {
        get
        {
            if (ExpireUnix <= 0) return "";
            try
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(ExpireUnix).LocalDateTime;
                var days = (dt.Date - DateTime.Now.Date).Days;
                if (days < 0) return $"Истекла ({dt:dd.MM.yyyy})";
                if (days == 0) return "Истекает сегодня";
                return $"До {dt:dd.MM.yyyy} ({days} дн.)";
            }
            catch { return ""; }
        }
    }

    public string ShortExpireText
    {
        get
        {
            if (ExpireUnix <= 0) return "";
            try
            {
                var dt = DateTimeOffset.FromUnixTimeSeconds(ExpireUnix).LocalDateTime;
                var days = (dt.Date - DateTime.Now.Date).Days;
                if (days < 0) return "Истекла";
                if (days == 0) return "Сегодня";
                return $"Осталось {days} дн.";
            }
            catch { return ""; }
        }
    }

    public string LastUpdatedText
    {
        get
        {
            if (LastUpdatedUnix <= 0) return "";
            try
            {
                return DateTimeOffset.FromUnixTimeSeconds(LastUpdatedUnix).LocalDateTime.ToString("dd.MM.yy, HH:mm");
            }
            catch { return ""; }
        }
    }

    public string UpdateIntervalText => UpdateIntervalHours > 0 ? $"{UpdateIntervalHours}ч" : "";
}
