using BlackWhale.Shared;
using UnityEngine;

namespace BlackWhale.Network
{
    public sealed class UnityWireCodec : IWireCodec
    {
        public string EncodeClient(ClientMessage message) { return JsonUtility.ToJson(message); }
        public ClientMessage DecodeClient(string json)
        { RequestJsonValidator.Validate(json); return JsonUtility.FromJson<ClientMessage>(json); }
        public string EncodeServer(ServerMessage message) { return JsonUtility.ToJson(message); }
        public ServerMessage DecodeServer(string json) { return JsonUtility.FromJson<ServerMessage>(json); }
    }

}
