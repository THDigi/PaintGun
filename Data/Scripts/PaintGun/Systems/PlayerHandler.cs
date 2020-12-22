using System.Collections.Generic;
using Digi.PaintGun.Utilities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace Digi.PaintGun.Systems
{
    /// <summary>
    /// Because the game has nothing reliable for player connecting/disconnecting.
    /// </summary>
    public class PlayerHandler : ModComponent
    {
        public event PlayerEventDel PlayerConnected;
        public event PlayerEventDel PlayerDisconnected;
        public delegate void PlayerEventDel(IMyPlayer player);

        private readonly Dictionary<ulong, IMyPlayer> connectedPlayers;
        private readonly List<ulong> removeKeys;

        private const int SKIP_TICKS = Constants.TICKS_PER_SECOND * 1;

        public PlayerHandler(PaintGunMod main) : base(main)
        {
            connectedPlayers = new Dictionary<ulong, IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);
            removeKeys = new List<ulong>(MyAPIGateway.Session.SessionSettings.MaxPlayers);

            UpdateMethods = ComponentLib.UpdateFlags.UPDATE_AFTER_SIM;
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }

        protected override void UpdateAfterSim(int tick)
        {
            if(tick % SKIP_TICKS != 0)
                return;

            var players = PaintGunMod.Instance.Caches.Players;
            players.Clear();
            MyAPIGateway.Players.GetPlayers(players);

            removeKeys.Clear();

            // DEBUG player handler

            foreach(var player in connectedPlayers.Values)
            {
                if(!players.Contains(player))
                {
                    removeKeys.Add(player.SteamUserId);
                    PlayerDisconnected?.Invoke(player);

                    Log.Info($"DEBUG: PlayerHandler :: {Utils.PrintPlayerName(player.SteamUserId)} disconnected");
                }
            }

            if(removeKeys.Count != 0)
            {
                foreach(var key in removeKeys)
                {
                    connectedPlayers.Remove(key);
                }

                removeKeys.Clear();
            }

            foreach(var player in players)
            {
                if(!connectedPlayers.ContainsKey(player.SteamUserId))
                {
                    connectedPlayers.Add(player.SteamUserId, player);
                    PlayerConnected?.Invoke(player);

                    Log.Info($"DEBUG: PlayerHandler :: {Utils.PrintPlayerName(player.SteamUserId)} joined");
                }
            }

            players.Clear();
        }
    }
}
