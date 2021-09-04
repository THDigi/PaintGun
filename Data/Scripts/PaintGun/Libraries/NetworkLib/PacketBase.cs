using ProtoBuf;
using Sandbox.ModAPI;

namespace Digi.NetworkLib
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public abstract partial class PacketBase
    {
        /// <summary>
        /// Do not edit, automatically assigned to original sender's SteamId, validated when it reaches server.
        /// </summary>
        [ProtoMember(1)]
        public ulong OriginalSenderSteamId;

        public PacketBase()
        {
            OriginalSenderSteamId = MyAPIGateway.Multiplayer.MyId;
        }

        /// <summary>
        /// Called when this packet is received on this machine.
        /// </summary>
        /// <param name="relay">Set to true if the packet should be automatically relayed to clients if the server receives it.</param>
        public abstract void Received(ref bool relay);
    }
}