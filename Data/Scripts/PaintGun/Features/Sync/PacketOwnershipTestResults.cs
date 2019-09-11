using System.Collections.Generic;
using Digi.NetworkLib;
using ProtoBuf;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketOwnershipTestResults : PacketBase
    {
        [ProtoMember(1)]
        internal List<int> OwnedSkinIndexes;

        public PacketOwnershipTestResults() { } // Empty constructor required for deserialization

        public void Send(ulong sendTo)
        {
            Network.SendToPlayer(this, sendTo);
        }

        public override void Received(ref bool relay)
        {
            Main.OwnershipTestPlayer.GotResults(OwnedSkinIndexes);
        }
    }
}