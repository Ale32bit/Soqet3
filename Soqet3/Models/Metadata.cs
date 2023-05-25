namespace Soqet3.Models;

public class Metadata
{
    public string Channel { get; set; }
    public string Address { get; internal set; }
    public DateTime DateTime { get; set; }
    public string Sender { get; set; }
    public bool Guest { get; set; }
}
