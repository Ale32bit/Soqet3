namespace Soqet3.Models;

public class Send : Request
{
    public string Channel { get; set; }
    public object Data { get; set; }
}
