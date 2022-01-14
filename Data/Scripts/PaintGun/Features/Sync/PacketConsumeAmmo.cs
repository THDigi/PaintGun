using Digi.NetworkLib;
using Digi.PaintGun.Utilities;
using ProtoBuf;
using VRage.Game.ModAPI;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketConsumeAmmo : PacketBase
    {
        public PacketConsumeAmmo() { } // Empty constructor required for deserialization

        public void Send()
        {
            Network.SendToServer(this);
        }

        public override void Received(ref RelayMode relay)
        {
            if(Main.IsServer && !Main.IgnoreAmmoConsumption) // ammo consumption, only needed server side
            {
                IMyInventory inv = Utils.GetCharacterInventoryOrError(this, Utils.GetCharacterOrError(this, Utils.GetPlayerOrError(this, OriginalSenderSteamId)));
                if(inv != null)
                    inv.RemoveItemsOfType(1, Main.Constants.PAINT_MAG_ITEM, false);
            }
        }
    }
}