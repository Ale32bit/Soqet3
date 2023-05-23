namespace Soqet3.Models;

public class ErrorResponse : Response
{
    public new bool Ok { get; set; } = false;
    public string Error { get; set; }
    public string Message { get; set; }
}
