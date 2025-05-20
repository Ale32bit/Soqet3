namespace Soqet3.Structs;

public struct Message(object data, string channel, Metadata metadata)
{
    public string Event { get; set; } = "message";
    public object Data { get; set; } = data;
    public Metadata Metadata { get; set; } = metadata;
    public string Channel { get; set; } = channel;
}
