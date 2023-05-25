using Soqet3.Models;
using System.Net.WebSockets;
using System.Text.Json;
using System.Text;
using System.Collections.Concurrent;
using System.Data;
using System.Security.Cryptography;
using System.Collections;
using System.Text.RegularExpressions;
using Soqet3.Structs;

namespace Soqet3;
public class ClientManager
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    public static readonly Regex ChannelNameBlacklistRegex = new("@[^a-z0-9-_]", RegexOptions.NonBacktracking);
    public const string PrivateChannelPrefix = "$";

    public delegate void ResponseHandler(string message);

    public readonly HashSet<SoqetClient> Clients = new();
    public readonly ConcurrentDictionary<Channel, HashSet<SoqetClient>> Channels = new();

    private readonly string _secretSalt;

    public ClientManager(IConfiguration configuration)
    {
        _secretSalt = configuration["SecretSalt"] ?? "";
    }

    public SoqetClient Create(out string hello)
    {
        var client = new SoqetClient();
        client.Name = GenerateClientName(client.SessionId.ToString());

        hello = JsonSerializer.Serialize(new Hello
        {
            Name = client.Name,
        }, JsonOptions);

        Clients.Add(client);
        return client;
    }

    public void Delete(SoqetClient client)
    {
        foreach (var channel in client.Channels)
        {
            if (Channels.TryGetValue(channel, out var ch))
            {
                ch.Remove(client);
                if (ch.Count == 0)
                {
                    Channels.Remove(channel, out _);
                }
            }
        }
        Clients.Remove(client);
    }

    public string GenerateClientName(string key)
    {
        key += _secretSalt;
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(key));
        return Convert.ToHexString(digest);
    }

    public Channel GetChannelName(string channelName, string clientName = "")
    {
        var channel = new Channel();
        var isPrivate = channelName.StartsWith(PrivateChannelPrefix);
        channelName = channelName
            .ToLower()
            .Trim()
            [..Math.Min(128, channelName.Length)];

        channelName = ChannelNameBlacklistRegex.Replace(channelName, "_");
        channel.Name = channelName;
        if (isPrivate)
            channel.Address = string.Format("${0}:{1}", clientName, channelName);
        else
            channel.Address = channelName;

        return channel;
    }

    public async Task ProcessRequestAsync(SoqetClient client, string message, ResponseHandler handler)
    {
        Request? data;
        try
        {
            data = JsonSerializer.Deserialize<Request>(message, JsonOptions);
        }
        catch
        {
            var errorResponse = new ErrorResponse
            {
                Id = -1,
                Error = "invalid_json",
                Message = "Request body structure is invalid",
                Name = client.Name,
            };
            handler(JsonSerializer.Serialize(errorResponse, JsonOptions));
            return;
        }

        if (data == null || string.IsNullOrWhiteSpace(data.Type))
        {
            var errorResponse = new ErrorResponse
            {
                Id = data.Id,
                Error = "invalid_type",
                Message = "Type field is missing",
                Name = client.Name,
            };
            handler(JsonSerializer.Serialize(errorResponse, JsonOptions));
            return;
        }

        try
        {
            var response = data.Type switch
            {
                "open" => OpenChannels(client, JsonSerializer.Deserialize<ChannelRequest>(message, JsonOptions)),
                "close" => CloseChannels(client, JsonSerializer.Deserialize<ChannelRequest>(message, JsonOptions)),
                "send" => Send(client, JsonSerializer.Deserialize<Send>(message, JsonOptions)),
                "authenticate" => Authenticate(client, JsonSerializer.Deserialize<Authenticate>(message, JsonOptions)),

                _ => JsonSerializer.Serialize(new ErrorResponse
                {
                    Id = data.Id,
                    Error = "unknown_type",
                    Message = "The request type provided in unknown",
                    Name = client.Name,

                }, JsonOptions),
            };

            handler(response);
        }
        catch (JsonException ex)
        {
            var errorResponse = new ErrorResponse
            {
                Id = data?.Id ?? -1,
                Error = "json_error",
                Message = ex.Message,
                Name = client.Name,
            };
            handler(JsonSerializer.Serialize(errorResponse, JsonOptions));
            return;
        }
    }

    private string OpenChannels(SoqetClient client, ChannelRequest request)
    {
        var channels = request.GetChannels();

        foreach (var channel in channels)
        {
            if (client.Channels.Count < client.MaxOpenChannels)
            {
                var chName = GetChannelName(channel, client.Name);
                client.Channels.Add(chName);
                var ch = Channels.GetOrAdd(chName, new HashSet<SoqetClient>());
                ch.Add(client);
            }
            else
            {
                return JsonSerializer.Serialize(new ErrorResponse
                {
                    Id = request.Id,
                    Error = "open_channels_exceeded",
                    Message = "Reached limit of open channels",
                    Name = client.Name,
                }, JsonOptions);
            }
        }

        return JsonSerializer.Serialize(new Response
        {
            Id = request.Id,
            Name = client.Name,
        }, JsonOptions);
    }

    private string CloseChannels(SoqetClient client, ChannelRequest request)
    {
        var channels = request.GetChannels();
        if (channels.Count() > 0xFF)
            return JsonSerializer.Serialize(new ErrorResponse
            {
                Id = request.Id,
                Error = "request_too_large",
                Message = "The request body is too large",
                Name = client.Name,
            }, JsonOptions);
        foreach (var channel in channels)
        {
            var chName = GetChannelName(channel, client.Name);
            client.Channels.Remove(chName);
            if (Channels.TryGetValue(chName, out var ch))
                ch.Remove(client);
        }

        return JsonSerializer.Serialize(new Response
        {
            Id = request.Id,
            Name = client.Name,
        }, JsonOptions);
    }

    private string Send(SoqetClient client, Send request)
    {
        var chAddr = GetChannelName(request.Channel, client.Name);
        if (!client.Channels.Contains(chAddr))
        {
            return JsonSerializer.Serialize(new ErrorResponse
            {
                Id = request.Id,
                Error = "channel_closed",
                Message = "Channel is closed",
                Name = client.Name,
            }, JsonOptions);
        }

        var payload = new Message
        {
            Data = request.Data,
            Channel = chAddr.Name,
            Metadata = new()
            {
                Channel = chAddr.Name,
                Address = chAddr.Address,
                DateTime = DateTime.UtcNow,
                Guest = client.Guest,
                Sender = client.Name,
            },
        };

        var channel = Channels[chAddr];
        foreach (var cl in channel)
        {
            if (cl.SessionId == client.SessionId)
                continue;

            cl.SendAsync(JsonSerializer.Serialize(payload, JsonOptions));
        }

        return JsonSerializer.Serialize(new Response
        {
            Id = request.Id,
            Name = client.Name,
        }, JsonOptions);
    }

    private string Authenticate(SoqetClient client, Authenticate request)
    {
        if (string.IsNullOrWhiteSpace(request.Key))
        {
            return JsonSerializer.Serialize(new ErrorResponse
            {
                Id = request.Id,
                Error = "invalid_key",
                Message = "The authentication key provided is not valid",
                Name = client.Name,
            }, JsonOptions);
        }

        client.Name = GenerateClientName(request.Key);
        client.Guest = false;

        return JsonSerializer.Serialize(new Response
        {
            Id = request.Id,
            Name = client.Name,
        }, JsonOptions);
    }
}
