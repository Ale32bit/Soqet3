using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Soqet3.Models;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Security.Cryptography;

// For more information on enabling Web API for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Soqet3
{
    [Route("api")]
    [ApiController]
    public class PollingController : ControllerBase
    {
        private static readonly ConcurrentDictionary<string, SoqetClient> _clients = new();
        private readonly ClientManager _clientManager;
        private readonly IMemoryCache _cache;
        public PollingController(ClientManager clientManager, IMemoryCache cache)
        {
            _clientManager = clientManager;
            _cache = cache;
        }

        private string IssueToken()
        {
            string token;
            do
            {
                token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));
            } while (_clients.ContainsKey(token));

            var expiration = DateTime.UtcNow.AddMinutes(1);
            _cache.Set(token, expiration, TimeSpan.FromMinutes(1));

            return token;
        }

        private bool ValidateToken(string token, out SoqetClient? client)
        {
            client = null;
            if (!_cache.TryGetValue(token, out DateTime expiration))
                return false;

            client = _clients[token];

            if (DateTime.UtcNow <= expiration)
            {
                _cache.Set(token, expiration, TimeSpan.FromMinutes(1));
                return true;
            }

            _cache.Remove(token);
            DisposeClient(token);
            return false;
        }

        private void DisposeClient(string token)
        {
            _clients.Remove(token, out var client);
            if (client != null)
            {
                _clientManager.Delete(client);
            }
        }

        // GET: api/start
        [HttpGet("start")]
        public PollingResponse Start()
        {
            var client = _clientManager.Create(out _);
            var token = IssueToken();

            _clients[token] = client;

            var response = new PollingResponse
            {
                Name = client.Name,
                Token = token,
            };

            return response;
        }

        // GET api/<PollingController>/5
        [HttpPost("{token}")]
        public async Task<string> Post(string token, [FromBody] string body)
        {
            return "value";
        }

        // DELETE api/<PollingController>/5
        [HttpDelete("{token}")]
        public void Delete(string token)
        {
            if (ValidateToken(token, out _))
            {
                DisposeClient(token);
            }
        }
    }
}
