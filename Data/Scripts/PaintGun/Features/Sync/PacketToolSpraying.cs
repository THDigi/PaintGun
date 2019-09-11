using Digi.NetworkLib;
using Digi.PaintGun.Utilities;
using ProtoBuf;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketToolSpraying : PacketBase
    {
        [ProtoMember(1)]
        bool Spraying;

        public PacketToolSpraying() { } // Empty constructor required for deserialization

        public void Send(bool spraying)
        {
            Spraying = spraying;
            Network.SendToServer(this);
        }

        public override void Received(ref bool relay)
        {
            relay = true;

            if(Main.IsPlayer)
            {
                var player = Utils.GetPlayerBySteamId(SteamId);

                if(player == null)
                    return;

                var tools = Main.ToolHandler.Tools;

                foreach(var tool in tools)
                {
                    if(tool.OwnerSteamId == SteamId)
                    {
                        tool.Spraying = Spraying;
                        break;
                    }
                }
            }
        }
    }
}