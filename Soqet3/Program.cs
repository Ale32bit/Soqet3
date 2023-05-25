using Soqet3;
using Soqet3.Models;
using System.Net.WebSockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var clientManager = new ClientManager(builder.Configuration);

app.UseWebSockets();

app.Use(async (context, next) =>
{
    Console.WriteLine(context.Request.Path);
    Console.WriteLine(context.WebSockets.IsWebSocketRequest);
    await next();
});

app.Map("/ws/{nonce?}", async (HttpContext context) =>
{
    if (!context.WebSockets.IsWebSocketRequest)
    {
        context.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
        await context.Response.WriteAsJsonAsync(new ErrorResponse
        {
            Error = "websocket_upgrade_required",
            Message = "A WebSocket connection is required",
        });
        return;
    }

    using var webSocket = await context.WebSockets.AcceptWebSocketAsync();
    var wsClient = new WebSocketController(clientManager, webSocket);
    await wsClient.ListenAsync();
});


app.Run();