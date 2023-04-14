using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;

namespace Digi.NetworkLib
{
    /// <summary>
    /// Simple network communication.
    /// 
    /// Always send to server as clients can't send to eachother directly.
    /// Then decide in the packet if it should be relayed to everyone else (except sender and server of course).
    /// </summary>
    public class Network : IDisposable
    {
        public readonly ushort ChannelId;

        /// <summary>
        /// Callback for errors in the receive packet event.
        /// Default/null will log to SE log, DS console and client HUD.
        /// NOTE: Another mod using the same channelId will cause exceptions that are not either of your faults, not recommended to crash nor to ignore the error, just let players find collisions and work it out with the other author(s).
        /// </summary>
        public Action<Exception> CustomExceptionHandler;

        /// <summary>
        /// Additional callback when exceptions occurs on <see cref="ReceivedPacket(ushort, byte[], ulong, bool)"/>.
        /// </summary>
        public Action<ulong, byte[]> ReceiveExceptionHandler;

        private readonly string ModName;
        private List<IMyPlayer> TempPlayers = null;

        /// <summary>
        /// <paramref name="channelId"/> must be unique from all other mods that also use network packets.
        /// </summary>
        /// <param name="channelId">must be unique from all other mods that also use network packets.</param>
        /// <param name="modName">just an identifier for errors/warnings.</param>
        /// <param name="registerListener">you can turn off message listening if you don't want this machine to receive them.</param>
        /// <param name="customExceptionHandler"></param>
        public Network(ushort channelId, string modName, bool registerListener = true, Action<Exception> customExceptionHandler = null)
        {
            ChannelId = channelId;
            ModName = modName;
            CustomExceptionHandler = customExceptionHandler;

            if(registerListener)
                MyAPIGateway.Multiplayer.RegisterSecureMessageHandler(ChannelId, ReceivedPacket);
        }

        /// <summary>
        /// This must be called on world unload.
        /// </summary>
        public void Dispose()
        {
            MyAPIGateway.Multiplayer.UnregisterSecureMessageHandler(ChannelId, ReceivedPacket);
        }

        /// <summary>
        /// Send a packet to the server.
        /// Works from clients and server.
        /// <para><paramref name="serialized"/> = input pre-serialized data if you have it or leave null otherwise.</para>
        /// </summary>
        public void SendToServer(PacketBase packet, byte[] serialized = null)
        {
            if(MyAPIGateway.Multiplayer.IsServer) // short-circuit local call to avoid unnecessary serialization
            {
                HandlePacket(packet, MyAPIGateway.Multiplayer.MyId, serialized);
                return;
            }

            if(serialized == null)
                serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageToServer(ChannelId, serialized);
        }

        /// <summary>
        /// Send a packet to a specific player.
        /// Only works server side.
        /// <para><paramref name="serialized"/> = input pre-serialized data if you have it or leave null otherwise.</para>
        /// </summary>
        public void SendToPlayer(PacketBase packet, ulong steamId, byte[] serialized = null)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception($"{ModName}: Clients can't send packets to other clients directly!");

            if(serialized == null)
                serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

            MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, serialized, steamId);
        }

        /// <summary>
        /// Sends packet (or supplied bytes) to all players except server player and supplied packet's sender.
        /// Only works server side.
        ///  </summary>
        public void SendToOthers(PacketBase packet, byte[] serialized = null)
        {
            RelayToClients(packet, 0, serialized);
        }

        void RelayToClients(PacketBase packet, ulong senderSteamId = 0, byte[] serialized = null)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                throw new Exception($"{ModName}: Clients can't relay packets!");

            if(TempPlayers == null)
                TempPlayers = new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);
            else
                TempPlayers.Clear();

            MyAPIGateway.Players.GetPlayers(TempPlayers);

            foreach(IMyPlayer p in TempPlayers)
            {
                // skip sending to self (server player) or back to sender
                if(p.SteamUserId == MyAPIGateway.Multiplayer.ServerId || p.SteamUserId == senderSteamId)
                    continue;

                if(serialized == null) // only serialize if necessary, and only once.
                    serialized = MyAPIGateway.Utilities.SerializeToBinary(packet);

                MyAPIGateway.Multiplayer.SendMessageTo(ChannelId, serialized, p.SteamUserId);
            }

            TempPlayers.Clear();
        }

        void ReceivedPacket(ushort channelId, byte[] serialized, ulong senderSteamId, bool isSenderServer) // executed when a packet is received on this machine
        {
            try
            {
                PacketBase packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketBase>(serialized);
                HandlePacket(packet, senderSteamId, serialized);
            }
            catch(Exception e)
            {
                if(CustomExceptionHandler != null)
                    CustomExceptionHandler.Invoke(e);
                else
                    DefaultExceptionHandler(e);

                if(ReceiveExceptionHandler != null)
                    ReceiveExceptionHandler.Invoke(senderSteamId, serialized);
                else
                    MyLog.Default.WriteLineAndConsole($"^-- Additional info: sender={senderSteamId}; bytes={string.Join(",", serialized)}");
            }
        }

        void HandlePacket(PacketBase packet, ulong senderSteamId, byte[] serialized = null)
        {
            // Server-side OriginalSender validation
            if(MyAPIGateway.Multiplayer.IsServer)
            {
                if(senderSteamId != packet.OriginalSenderSteamId)
                {
                    string text = $"WARNING: packet {packet.GetType().Name} from {senderSteamId.ToString()} has altered OriginalSenderSteamId to {packet.OriginalSenderSteamId.ToString()}. Replaced it with proper id, but if this triggers for everyone then it's a bug somewhere.";
                    MyLog.Default.WriteLineAndConsole($"{ModName} {text}");
                    Log.Error(text);

                    packet.OriginalSenderSteamId = senderSteamId;
                    serialized = null; // force reserialize
                }
            }

            RelayMode relay = RelayMode.NoRelay;
            packet.Received(ref relay);

            if(MyAPIGateway.Multiplayer.IsServer && relay != RelayMode.NoRelay)
            {
                if(relay == RelayMode.RelayOriginal)
                    RelayToClients(packet, senderSteamId, serialized);
                else if(relay == RelayMode.RelayWithChanges)
                    RelayToClients(packet, senderSteamId, null);
                else
                    throw new Exception($"{ModName}: Unknown relay mode: {relay.ToString()}");
            }
        }

        void DefaultExceptionHandler(Exception e)
        {
            MyLog.Default.WriteLineAndConsole($"{ModName} ERROR: {e.Message}\n{e.StackTrace}");

            if(MyAPIGateway.Session?.Player != null)
                MyAPIGateway.Utilities.ShowNotification($"[ERROR: {ModName}: {e.Message} | Send SpaceEngineers.Log to mod author]", 10000, MyFontEnum.Red);
        }
    }

    public enum RelayMode
    {
        /// <summary>
        /// No relaying of this packet.
        /// </summary>
        NoRelay = 0,

        /// <summary>
        /// Relay the received bytes, any changes to the class will NOT be sent.
        /// </summary>
        RelayOriginal,

        /// <summary>
        /// Re-serializes and relays packet, useful if you make changes to packet serverside before relaying.
        /// </summary>
        RelayWithChanges,
    }
}