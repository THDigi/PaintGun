using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.PaintGun.Utilities
{
    public class Caches : ModComponent
    {
        public List<IMyPlayer> Players;
        public List<Vector3I> AlreadyMirrored;
        public List<uint> PackedColors;

        public Caches(PaintGunMod main) : base(main)
        {
            Players = new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers);
            PackedColors = new List<uint>(Constants.COLOR_PALETTE_SIZE);

            if(Main.IsPlayer)
            {
                AlreadyMirrored = new List<Vector3I>(8);
            }
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }
    }
}