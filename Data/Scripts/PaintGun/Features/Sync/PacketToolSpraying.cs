using System.Collections.Generic;
using Digi.NetworkLib;
using Digi.PaintGun.Features.Tool;
using Digi.PaintGun.Utilities;
using ProtoBuf;
using VRage.Game.ModAPI;

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

        public override void Received(ref RelayMode relay)
        {
            relay = RelayMode.RelayOriginal;

            if(Main.IsPlayer)
            {
                IMyPlayer player = Utils.GetPlayerBySteamId(OriginalSenderSteamId);
                if(player == null)
                    return;

                List<PaintGunItem> tools = Main.ToolHandler.Tools;

                foreach(PaintGunItem tool in tools)
                {
                    if(tool.OwnerSteamId == OriginalSenderSteamId)
                    {
                        tool.Spraying = Spraying;
                        break;
                    }
                }
            }
        }
    }
}