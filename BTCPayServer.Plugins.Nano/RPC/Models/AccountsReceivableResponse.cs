using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace BTCPayServer.Plugins.Nano.RPC.Models
{
    public class AccountsReceivableResponse
    {
        [JsonProperty("blocks")]
        [JsonConverter(typeof(BlocksConverter))]
        public Dictionary<string, Dictionary<string, ReceivableBlock>> Blocks { get; set; }
    }

    public class ReceivableBlock
    {
        [JsonProperty("amount")]
        public string Amount { get; set; }

        [JsonProperty("source")]
        public string Source { get; set; }
    }

    public class BlocksConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(Dictionary<string, Dictionary<string, ReceivableBlock>>);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            // Case 1: "blocks" is a string
            if (reader.TokenType == JsonToken.String)
            {
                reader.Skip();
                return null;
            }

            // Case 2: "blocks" is null
            if (reader.TokenType == JsonToken.Null)
            {
                return null;
            }

            // Case 3: "blocks" is an object
            var dict = serializer.Deserialize<Dictionary<string, Dictionary<string, ReceivableBlock>>>(reader);
            return dict;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            serializer.Serialize(writer, value);
        }
    }
}