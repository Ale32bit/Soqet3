namespace Soqet3.Models;

public class Message
{
    public string Event { get; set; } = "message";
    public object Data { get; set; }
    public Metadata Metadata { get; set; }
    public string Channel { get; set; }
}
