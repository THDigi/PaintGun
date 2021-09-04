using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Digi.NetworkLib
{
    public class Network : IDisposable
    {
        public readonly ushort ChannelID;

        private readonly List<IMyPlayer> players;

        /// <summary>
        /// <paramref name="channelId"/> must be unique for each mod!
        /// Convention is to use the last digits of your workshopID.
        /// </summary>
        public Network(ushort channelId)
        {
            ChannelID = channelId;

            players = new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);

            MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(ChannelID, ReceivedPacket);
        }

        public void Dispose()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ChannelID, ReceivedPacket);
        }

        /// <summary>
        /// Send a packet to the server.
        /// If already server side then the packet is directly handled and skips serialization.
        /// </summary>
        public void SendToServer(PacketBase packet, byte[] serialized = null)
        {
            if(MyAPIGateway.Multiplayer.IsServer)
            {
                HandlePacket(packet, serialized, MyAPIGateway.Multiplayer.MyId);
                return;
            }

            if(serialized == null)
                serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageToServer(ChannelID, serialized);
        }

        /// <summary>
        /// Send a packet to a specific player.
        /// NOTE: Only works server side.
        /// </summary>
        public void SendToPlayer(PacketBase packet, ulong steamId, byte[] serialized = null)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception($"SendToPlayer() can only be used from server!");

            if(serialized == null)
                serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageTo(ChannelID, serialized, steamId);
        }

        /// <summary>
        /// Sends packet (or supplied bytes) to all players except server player and supplied packet's sender.
        /// NOTE: Only works server side.
        /// </summary>
        public void SendToOthers(PacketBase packet, byte[] serialized = null)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                return;

            players.Clear();

            MyAPIGateway.Players.GetPlayers(players);

            foreach(IMyPlayer player in players)
            {
                if(player.SteamUserId == MyAPIGateway.Multiplayer.ServerId)
                    continue;

                if(player.SteamUserId == packet.OriginalSenderSteamId)
                    continue;

                if(serialized == null)
                    serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

                MyAPIGateway.Multiplayer.SendMessageTo(ChannelID, serialized, player.SteamUserId);
            }

            players.Clear();
        }

        void ReceivedPacket(ushort channelId, byte[] serialized, ulong senderSteamId, bool senderIsServer) // executed when a packet is received on this machine
        {
            try
            {
                PacketBase packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(serialized);
                HandlePacket(packet, serialized, senderSteamId);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        void HandlePacket(PacketBase packet, byte[] serialized, ulong senderSteamId)
        {
            // Server-side OriginalSender validation
            if(MyAPIGateway.Multiplayer.IsServer && senderSteamId != packet.OriginalSenderSteamId)
            {
                MyLog.Default.WriteLineAndConsole($"{GetType().FullName} WARNING: packet {packet.GetType().Name} from {senderSteamId.ToString()} has altered OriginalSenderSteamId to {packet.OriginalSenderSteamId.ToString()}. Replaced it with proper id, but if this triggers for everyone then it's a bug somewhere.");

                packet.OriginalSenderSteamId = senderSteamId;
                serialized = null; // force reserialize
            }

            bool relay = false;
            packet.Received(ref relay);

            if(relay)
                SendToOthers(packet, serialized);
        }
    }
}