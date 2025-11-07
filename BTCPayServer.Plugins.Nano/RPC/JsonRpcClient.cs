using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Nano.RPC
{
    public class JsonRpcClient
    {
        private readonly Uri _address;
        // private readonly string _username;
        // private readonly string _password;
        private readonly HttpClient _httpClient;

        public JsonRpcClient(Uri address, HttpClient client = null)
        {
            _address = address;
            // _username = username;
            // _password = password;
            _httpClient = client ?? new HttpClient();
        }


        public async Task<TResponse> SendCommandAsync<TRequest, TResponse>(string action, TRequest data,
            CancellationToken cts = default)
        {
            var jsonSerializer = new JsonSerializerSettings
            {
                ContractResolver = new CamelCasePropertyNamesContractResolver()
            };

            var payload = data != null ? JObject.FromObject(data) : new JObject();
            payload["action"] = action;

            var httpRequest = new HttpRequestMessage()
            {
                Method = HttpMethod.Post,
                // RequestUri = new Uri(_address),
                RequestUri = _address,
                Content = new StringContent(
                    payload.ToString(Formatting.None),
                    Encoding.UTF8, "application/json")
            };
            httpRequest.Headers.Accept.Clear();
            httpRequest.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
            // httpRequest.Headers.Authorization = new AuthenticationHeaderValue("Basic",
            //     Convert.ToBase64String(Encoding.Default.GetBytes($"{_username}:{_password}")));

            HttpResponseMessage rawResult = await _httpClient.SendAsync(httpRequest, cts);
            rawResult.EnsureSuccessStatusCode();
            var rawJson = await rawResult.Content.ReadAsStringAsync();

            var token = JToken.Parse(rawJson);

            var error = (token.Type == JTokenType.Object) ? token["error"]?.ToString() : null;

            if (!string.IsNullOrEmpty(error))
            {
                Console.WriteLine("throwing json api exception");
                Console.WriteLine(error);
                throw new JsonRpcApiException(error);
            }

            TResponse response;
            try
            {
                response = token.ToObject<TResponse>();
            }
            catch (Exception e)
            {
                Console.WriteLine(e.Message);
                Console.WriteLine(rawJson);
                throw;
            }

            return response;
        }

        public class NoRequestModel
        {
            public static readonly NoRequestModel Instance = new();
        }

        public class JsonRpcApiException : Exception
        {
            [JsonProperty("error")] public string Error { get; set; }

            public JsonRpcApiException(string error)
            {
                Error = error;
            }
        }
    }
}