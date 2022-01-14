using System;
using ProtoBuf;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

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
            if(MyAPIGateway.Multiplayer == null || MyAPIGateway.Utilities == null)
            {
                string name = GetType().Name;

                // Throwing exceptions early causes them to kick player to menu which has bad side effects.
                // So instead, I'm gonna delay this exception throw until game loads so that it crashes the game.
                ((IMyUtilities)MyAPIUtilities.Static).InvokeOnGameThread(() =>
                {
                    throw new Exception($"Cannot instantiate packets in fields ({name}), too early! Do it in one of the methods where MyAPIGateway.Multiplayer is not null.");
                });
            }
            else
            {
                OriginalSenderSteamId = MyAPIGateway.Multiplayer.MyId;
            }
        }

        /// <summary>
        /// Called when this packet is received on this machine.
        /// Assign <paramref name="relay"/> serverside if you want to auto-relay the received packet to other clients.
        /// </summary>
        public abstract void Received(ref RelayMode relay);
    }
}