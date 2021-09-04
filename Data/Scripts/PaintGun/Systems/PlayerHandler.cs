using System;
using System.Collections.Generic;
using Digi.ComponentLib;
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
        const bool DebugLog = false;

        public event PlayerEventDel PlayerConnected;
        public event PlayerEventDel PlayerDisconnected;
        public delegate void PlayerEventDel(IMyPlayer player);

        readonly List<long> JoinedIdentities = new List<long>(2);
        readonly List<IMyPlayer> OnlinePlayers;

        public PlayerHandler(PaintGunMod main) : base(main)
        {
            int maxPlayers = MyAPIGateway.Session.SessionSettings.MaxPlayers;
            OnlinePlayers = new List<IMyPlayer>(maxPlayers);

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
            try
            {
                // offload to next tick to be able to get IMyPlayer instance
                JoinedIdentities.Add(identityId);
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        protected override void UpdateAfterSim(int tick)
        {
            for(int i = (JoinedIdentities.Count - 1); i >= 0; i--)
            {
                long identityId = JoinedIdentities[i];

                IMyPlayer player = Utils.GetPlayerByIdentityId(identityId);
                if(player == null)
                {
                    ulong steamId = MyAPIGateway.Players.TryGetSteamId(identityId);
                    Log.Info($"Unknown player connected, identityId={identityId.ToString()}; alternate identifier: '{Utils.PrintPlayerName(Utils.GetPlayerByIdentityId(identityId))}' (steamId={steamId.ToString()}); trying again next tick...");
                    continue;
                }

                JoinedIdentities.RemoveAtFast(i);
                OnlinePlayers.Add(player);

                if(DebugLog)
                    Log.Info($"DEBUG: PlayerHandler :: {Utils.PrintPlayerName(player)} joined.");

                try
                {
                    PlayerConnected?.Invoke(player);
                }
                catch(Exception e)
                {
                    Log.Error(e);
                }
            }

            if(JoinedIdentities.Count <= 0)
            {
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);
            }
        }

        void ViScPlayerDisconnected(long identityId)
        {
            try
            {
                IMyPlayer player = null;
                int playerIdx = -1;

                // don't use Utils.GetPlayerByIdentityId() because we need the safety of the local OnlinePlayers cache list.
                for(int i = 0; i < OnlinePlayers.Count; i++)
                {
                    IMyPlayer p = OnlinePlayers[i];
                    if(p.IdentityId == identityId)
                    {
                        player = p;
                        playerIdx = i;
                        break;
                    }
                }

                if(player == null)
                {
                    ulong steamId = MyAPIGateway.Players.TryGetSteamId(identityId);
                    Log.Info($"Unknown player disconnected, identityId={identityId.ToString()}; alternate identifier: '{Utils.PrintPlayerName(Utils.GetPlayerByIdentityId(identityId))}' (steamId={steamId.ToString()})");
                    return;
                }

                OnlinePlayers.RemoveAt(playerIdx);

                if(DebugLog)
                    Log.Info($"DEBUG: PlayerHandler :: {Utils.PrintPlayerName(player)} disconnected.");

                try
                {
                    PlayerDisconnected?.Invoke(player);
                }
                catch(Exception e)
                {
                    Log.Error(e);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}
