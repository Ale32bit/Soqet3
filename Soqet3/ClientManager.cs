using Soqet3.Models;
using System.Text.Json;
using System.Text;
using System.Collections.Concurrent;
using System.Security.Cryptography;
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
    private readonly ILogger<ClientManager> _logger;


    public ClientManager(IConfiguration configuration, ILogger<ClientManager> logger)
    {
        _secretSalt = configuration["SecretSalt"] ?? "";
        _logger = logger;
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
        
        _logger.LogDebug("New client connected {Name}", client.Name);
        _logger.LogDebug("There are {Count} clients connected", Clients.Count);
        
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
        
        _logger.LogDebug("Client disconnected {Name}", client.Name);
        _logger.LogDebug("There are {Count} clients connected", Clients.Count);
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

        if (data is null)
        {
            var errorResponse = new ErrorResponse
            {
                Id = -1,
                Error = "invalid_request",
                Message = "Request is not in a valid format",
                Name = client.Name,
            };
            handler(JsonSerializer.Serialize(errorResponse, JsonOptions));
            return;
        }

        if (string.IsNullOrWhiteSpace(data.Type))
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
                "open" => OpenChannels(client, JsonSerializer.Deserialize<ChannelRequest>(message, JsonOptions), data),
                "close" => CloseChannels(client, JsonSerializer.Deserialize<ChannelRequest>(message, JsonOptions), data),
                "send" => await Send(client, JsonSerializer.Deserialize<Send>(message, JsonOptions), data),
                "authenticate" => Authenticate(client, JsonSerializer.Deserialize<Authenticate>(message, JsonOptions), data),

                _ => JsonSerializer.Serialize(new ErrorResponse
                {
                    Id = data.Id,
                    Error = "unknown_type",
                    Message = "The request type provided is unknown",
                    Name = client.Name,

                }, JsonOptions),
            };

            handler(response);
        }
        catch (JsonException ex)
        {
            var errorResponse = new ErrorResponse
            {
                Id = data.Id,
                Error = "json_error",
                Message = ex.Message,
                Name = client.Name,
            };
            handler(JsonSerializer.Serialize(errorResponse, JsonOptions));
        }
    }

    private string OpenChannels(SoqetClient client, ChannelRequest? request, Request data)
    {
        if (request is null)
        {
            return JsonSerializer.Serialize(new ErrorResponse
            {
                Id = data.Id,
                Error = "invalid_request",
                Message = "Request is not in a valid format",
                Name = client.Name,
            }, JsonOptions);
        }
        
        var channels = request.GetChannels().ToArray();

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
        
        _logger.LogTrace("Client {Name} opened the following channels: {Channels}", client.Name, string.Join(", ", channels));

        return JsonSerializer.Serialize(new Response
        {
            Id = request.Id,
            Name = client.Name,
        }, JsonOptions);
    }

    private string CloseChannels(SoqetClient client, ChannelRequest? request, Request data)
    {
        if (request is null)
        {
            return JsonSerializer.Serialize(new ErrorResponse
            {
                Id = data.Id,
                Error = "invalid_request",
                Message = "Request is not in a valid format",
                Name = client.Name,
            }, JsonOptions);
        }
        
        var channels = request.GetChannels().ToArray();
        
        if (channels.Length > 0xFF)
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
        
        _logger.LogTrace("Client {Name} closed the following channels: {Channels}", client.Name, string.Join(", ", channels));

        return JsonSerializer.Serialize(new Response
        {
            Id = request.Id,
            Name = client.Name,
        }, JsonOptions);
    }

    private async Task<string> Send(SoqetClient client, Send? request, Request data)
    {
        if (request is null)
        {
            return JsonSerializer.Serialize(new ErrorResponse
            {
                Id = data.Id,
                Error = "invalid_request",
                Message = "Request is not in a valid format",
                Name = client.Name,
            }, JsonOptions);
        }

        if (string.IsNullOrWhiteSpace(request.Channel))
        {
            return JsonSerializer.Serialize(new ErrorResponse
            {
                Id = data.Id,
                Error = "invalid_channel",
                Message = "The channel name provided is invalid",
                Name = client.Name,
            }, JsonOptions);
        }
        
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
            Metadata = new Metadata
            {
                Channel = chAddr.Name,
                Address = chAddr.Address,
                DateTime = DateTime.UtcNow,
                Guest = client.Guest,
                Sender = client.Name,
            },
        };

        var eventMessage = JsonSerializer.Serialize(payload, JsonOptions);
        var tasks = Channels[chAddr]
            .Where(cl => cl.SessionId != client.SessionId)
            .Select(cl => cl.SendAsync(eventMessage));
        await Task.WhenAll(tasks);
        
        _logger.LogTrace("Client {Name} sent a message in {Channel}", client.Name, chAddr.Name);

        return JsonSerializer.Serialize(new Response
        {
            Id = request.Id,
            Name = client.Name,
        }, JsonOptions);
    }

    private string Authenticate(SoqetClient client, Authenticate? request, Request data)
    {
        if (request is null)
        {
            return JsonSerializer.Serialize(new ErrorResponse
            {
                Id = data.Id,
                Error = "invalid_request",
                Message = "Request is not in a valid format",
                Name = client.Name,
            }, JsonOptions);
        }
        
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

        var oldName = client.Name;
        client.Name = GenerateClientName(request.Key);
        client.Guest = false;
        
        _logger.LogDebug("Client authenticated from {OldName} to {NewName}", oldName, client.Name);

        return JsonSerializer.Serialize(new Response
        {
            Id = request.Id,
            Name = client.Name,
        }, JsonOptions);
    }
}
