using Digi.NetworkLib;
using Digi.PaintGun.Utilities;
using ProtoBuf;
using VRage.Utils;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketWarningMessage : PacketBase
    {
        [ProtoMember(1)]
        string Message;

        public PacketWarningMessage() { } // Empty constructor required for deserialization

        public void Send(ulong sendTo, string message)
        {
            Message = message;

            if(sendTo == 0)
                Network.SendToServer(this);
            else
                Network.SendToPlayer(this, sendTo);
        }

        public override void Received(ref RelayMode relay)
        {
            if(Main.IsServer)
                MyLog.Default.WriteLineAndConsole($"{PaintGunMod.MOD_NAME} :: warning message from {Utils.PrintPlayerName(OriginalSenderSteamId)} saying: {Message}");

            Log.Info(Message, Log.PRINT_MESSAGE, 5000);
        }
    }
}