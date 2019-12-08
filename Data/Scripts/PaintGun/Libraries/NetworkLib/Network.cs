using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

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

            MyAPIGateway.Multiplayer.RegisterMessageHandler(ChannelID, ReceivedPacket);
        }

        public void Dispose()
        {
            MyAPIGateway.Multiplayer.UnregisterMessageHandler(ChannelID, ReceivedPacket);
        }

        /// <summary>
        /// Send a packet to the server.
        /// If already server side then the packet is directly handled and skips serialization.
        /// </summary>
        public void SendToServer(PacketBase packet, byte[] serialized = null)
        {
            if(MyAPIGateway.Multiplayer.IsServer)
            {
                HandlePacket(packet, serialized);
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
        public void RelayToClients(PacketBase packet, byte[] serialized = null)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                return;

            players.Clear();

            MyAPIGateway.Players.GetPlayers(players);

            foreach(var player in players)
            {
                if(player.IsBot)
                    continue;

                if(player.SteamUserId == MyAPIGateway.Multiplayer.ServerId)
                    continue;

                if(player.SteamUserId == packet.SteamId)
                    continue;

                if(serialized == null)
                    serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

                MyAPIGateway.Multiplayer.SendMessageTo(ChannelID, serialized, player.SteamUserId);
            }

            players.Clear();
        }

        private void ReceivedPacket(byte[] rawData) // executed when a packet is received on this machine
        {
            try
            {
                var packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(rawData);
                HandlePacket(packet, rawData);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private void HandlePacket(PacketBase packet, byte[] rawData = null)
        {
            bool relay = false;
            packet.Received(ref relay);

            if(relay)
                RelayToClients(packet, rawData);
        }
    }
}