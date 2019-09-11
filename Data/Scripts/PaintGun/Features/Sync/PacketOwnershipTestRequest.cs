using Digi.NetworkLib;
using ProtoBuf;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketOwnershipTestRequest : PacketBase
    {
        public PacketOwnershipTestRequest() { } // Empty constructor required for deserialization

        public void Send()
        {
            Network.SendToServer(this);
        }

        public override void Received(ref bool relay)
        {
            Main.OwnershipTestServer.SpawnGrid(SteamId);
        }
    }
}