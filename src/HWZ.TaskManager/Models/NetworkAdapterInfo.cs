using HWZ.TaskManager.Helpers;

namespace HWZ.TaskManager.Models;

public sealed class NetworkAdapterInfo
{
    public NetworkAdapterInfo(
        string name,
        string description,
        string typeText,
        string address,
        long linkSpeedBitsPerSecond,
        double downMbps,
        double upMbps)
    {
        Name = name;
        Description = description;
        TypeText = typeText;
        Address = address;
        LinkSpeedBitsPerSecond = linkSpeedBitsPerSecond;
        DownMbps = downMbps;
        UpMbps = upMbps;
    }

    public string Name { get; }
    public string Description { get; }
    public string TypeText { get; }
    public string Address { get; }
    public long LinkSpeedBitsPerSecond { get; }
    public double DownMbps { get; }
    public double UpMbps { get; }

    public string DownText => Formatters.NetworkSpeed(DownMbps);
    public string UpText => Formatters.NetworkSpeed(UpMbps);
    public string LinkSpeedText => LinkSpeedBitsPerSecond <= 0 ? "—" : $"{LinkSpeedBitsPerSecond / 1_000_000d:0.#} Mbps";
    public string DisplayName => string.IsNullOrWhiteSpace(Description) ? Name : Description;
}
