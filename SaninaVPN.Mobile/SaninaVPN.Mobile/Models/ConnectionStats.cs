namespace Hivox.Mobile.Models;

public class ConnectionStats
{
    public DateTime? StartTime { get; set; } = null;
    public long BytesSent { get; set; } = 0;
    public long BytesReceived { get; set; } = 0;
    public string CurrentIp { get; set; } = "";
    public string ServerName { get; set; } = "";
    public double MaxSpeed { get; set; } = 0.0;
    public int TotalSessions { get; set; } = 0;
    public int ConnectionErrors { get; set; } = 0;
}
