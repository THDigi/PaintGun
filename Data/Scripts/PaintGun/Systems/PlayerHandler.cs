using System;
using System.Collections.Generic;
using Digi.PaintGun.Utilities;
using Sandbox.Game;
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

        private readonly Dictionary<ulong, IMyPlayer> ConnectedPlayers;
        private readonly List<ulong> RemoveConnected;
        private readonly List<IMyPlayer> Players;

        private const int SKIP_TICKS = Constants.TICKS_PER_SECOND * 1;

        public PlayerHandler(PaintGunMod main) : base(main)
        {
            int maxPlayers = MyAPIGateway.Session.SessionSettings.MaxPlayers;
            ConnectedPlayers = new Dictionary<ulong, IMyPlayer>(maxPlayers);
            RemoveConnected = new List<ulong>(maxPlayers);
            Players = new List<IMyPlayer>(maxPlayers);

            UpdateMethods = ComponentLib.UpdateFlags.UPDATE_AFTER_SIM;

            MyVisualScriptLogicProvider.PlayerConnected += ViScPlayerConnected;
            MyVisualScriptLogicProvider.PlayerDisconnected += ViScPlayerDisconnected;
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
            MyVisualScriptLogicProvider.PlayerConnected -= ViScPlayerConnected;
            MyVisualScriptLogicProvider.PlayerDisconnected -= ViScPlayerDisconnected;
        }

        void ViScPlayerConnected(long identityId)
        {
            ulong steamId = MyAPIGateway.Players.TryGetSteamId(identityId);
            Log.Info($"DEBUG: PlayerHandler :: ViSc triggered join: '{Utils.PrintPlayerName(Utils.GetPlayerByIdentityId(identityId))}' (steamId={steamId.ToString()})");
        }

        void ViScPlayerDisconnected(long identityId)
        {
            ulong steamId = MyAPIGateway.Players.TryGetSteamId(identityId);
            Log.Info($"DEBUG: PlayerHandler :: ViSc triggered disconnect: '{Utils.PrintPlayerName(Utils.GetPlayerByIdentityId(identityId))}' (steamId={steamId.ToString()})");
        }

        protected override void UpdateAfterSim(int tick)
        {
            if(tick % SKIP_TICKS != 0)
                return;

            Players.Clear();
            MyAPIGateway.Players.GetPlayers(Players);

            // DEBUG player handler

            foreach(IMyPlayer player in ConnectedPlayers.Values)
            {
                if(!Players.Contains(player))
                {
                    RemoveConnected.Add(player.SteamUserId);

                    Log.Info($"DEBUG: PlayerHandler :: {Utils.PrintPlayerName(player)} disconnected");

                    try
                    {
                        PlayerDisconnected?.Invoke(player);
                    }
                    catch(Exception e)
                    {
                        Log.Error(e);
                    }
                }
            }

            if(RemoveConnected.Count != 0)
            {
                foreach(ulong key in RemoveConnected)
                {
                    ConnectedPlayers.Remove(key);
                }

                RemoveConnected.Clear();
            }

            foreach(IMyPlayer player in Players)
            {
                if(!ConnectedPlayers.ContainsKey(player.SteamUserId))
                {
                    ConnectedPlayers.Add(player.SteamUserId, player);

                    Log.Info($"DEBUG: PlayerHandler :: {Utils.PrintPlayerName(player)} joined");

                    try
                    {
                        PlayerConnected?.Invoke(player);
                    }
                    catch(Exception e)
                    {
                        Log.Error(e);
                    }
                }
            }

            Players.Clear();
        }
    }
}
