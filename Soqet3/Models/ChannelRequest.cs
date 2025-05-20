namespace Soqet3.Models;

public class ChannelRequest : Request
{
    public string? Channel { get; set; }
    public string[]? Channels { get; set; }

    public IEnumerable<string> GetChannels()
    {
        if(!string.IsNullOrWhiteSpace(Channel))
            return [Channel];

        return Channels != null 
            ? Channels.Distinct() 
            : [];
    }
}
