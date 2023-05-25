using System.Net.WebSockets;

namespace Soqet3.Models;

public class SoqetClient
{
    public Guid SessionId { get; set; } = Guid.NewGuid();
    public string Name { get; set; }
    public bool Guest { get; set; } = true;
    public HashSet<Channel> Channels { get; set; } = new();
    public Func<string, Task> SendAsync { get; set; }
    public int MaxOpenChannels => Guest ? 8 : 128;
}
