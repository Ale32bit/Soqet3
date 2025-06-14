﻿using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Razor.TagHelpers;
using Soqet3.Models;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Timers;
using Timer = System.Timers.Timer;

namespace Soqet3
{
    [ApiController]
    public class WebSocketController : ControllerBase
    {
        private readonly ClientManager _clientManager;
        private WebSocket _webSocket;
        private SoqetClient _client;
        private readonly Timer _pingTimer;
        private readonly ILogger<WebSocketController> _logger;

        public WebSocketController(ClientManager clientManager, ILogger<WebSocketController> logger)
        {
            _logger = logger;
            _clientManager = clientManager;
            _pingTimer = new(TimeSpan.FromSeconds(10))
            {
                Enabled = false,
                AutoReset = true,
            };
            _pingTimer.Elapsed += PingElapsed;
        }

        [Route("/ws/{nonce?}")]
        public async Task Get(string nonce = "")
        {
            if (!HttpContext.WebSockets.IsWebSocketRequest)
            {
                HttpContext.Response.StatusCode = StatusCodes.Status426UpgradeRequired;
                await HttpContext.Response.WriteAsJsonAsync(new ErrorResponse
                {
                    Error = "websocket_upgrade_required",
                    Message = "A WebSocket connection is required",
                });
                return;
            }

            var clientIp = HttpContext.Connection.RemoteIpAddress;
            _logger.LogDebug("New WebSocket connection from {ClientIp}", clientIp);

            using var webSocket = await HttpContext.WebSockets.AcceptWebSocketAsync();
            _webSocket = webSocket;
            _client = _clientManager.Create(out var hello);
            _client.SendAsync = Send;

            await Send(hello);
            _pingTimer.Start();
            await ListenAsync();
        }

        private async Task ListenAsync()
        {
            var buffer = new byte[4096];
            var message = new StringBuilder();

            while (_webSocket.State == WebSocketState.Open)
            {
                try
                {
                    var result = await _webSocket.ReceiveAsync(buffer, default);

                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        _pingTimer.Stop();
                        await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, null, default);
                        break;
                    }

                    message.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                    if (result.EndOfMessage)
                    {
                        await _clientManager.ProcessRequestAsync(_client, message.ToString(),
                            async data => await Send(data));
                        message.Clear();
                    }
                }
                catch (WebSocketException e)
                {
                    if (e.WebSocketErrorCode == WebSocketError.ConnectionClosedPrematurely)
                    {
                        break;
                    }

                    _logger.LogError(e, "Error while listening to WebSocket connection");
                }
            }

            _pingTimer.Stop();
            _clientManager.Delete(_client);
        }

        private async Task Send(string data)
        {
            await _webSocket.SendAsync(Encoding.UTF8.GetBytes(data), WebSocketMessageType.Text, true, default);
        }

        private void PingElapsed(object? sender, ElapsedEventArgs e)
        {
            Send(JsonSerializer.Serialize(new WebSocketPing(), ClientManager.JsonOptions))
                .ContinueWith(t =>
                {
                    if (t.Exception is not null)
                    {
                        _logger.LogError(t.Exception, "Error while sending ping");
                    }
                }, TaskContinuationOptions.OnlyOnFaulted);
        }
    }
}