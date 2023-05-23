using Soqet3.Models;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;

namespace Soqet3;

public class WebSocketController
{
    public SoqetClient Client { get; init; }
    private readonly ClientManager _clientManager;
    private readonly WebSocket _webSocket;

    public WebSocketController(ClientManager clientManager, WebSocket webSocket)
    {
        _clientManager = clientManager;
        _webSocket = webSocket;

        Client = _clientManager.Create(out var hello);
        Client.SendAsync = Send;

        Send(hello);
    }

    public async Task ListenAsync()
    {


        var buffer = new byte[4096];
        var message = new StringBuilder();
        while (_webSocket.State == WebSocketState.Open)
        {
            var result = await _webSocket.ReceiveAsync(buffer, default);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, default);
                break;
            }

            message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

            if (result.EndOfMessage)
            {
                await _clientManager.ProcessRequestAsync(Client, message.ToString(), async data => await Send(data));
                message.Clear();
            }
        }

        _clientManager.Delete(Client);
    }

    public async Task Send(string data)
    {
        await _webSocket.SendAsync(Encoding.UTF8.GetBytes(data), WebSocketMessageType.Text, true, default);
    }
}
