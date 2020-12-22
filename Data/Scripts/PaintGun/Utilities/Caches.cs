using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.ModAPI;
using VRageMath;

namespace Digi.PaintGun.Utilities
{
    public class Caches : ModComponent
    {
        public MyConcurrentPool<List<IMyPlayer>> Players;
        public List<Vector3I> AlreadyMirrored;
        public List<uint> PackedColors;

        public Caches(PaintGunMod main) : base(main)
        {
            Players = new MyConcurrentPool<List<IMyPlayer>>(
                clear: (l) => l.Clear(),
                activator: () => new List<IMyPlayer>(MyAPIGateway.Session.SessionSettings.MaxPlayers),
                expectedAllocations: 5,
                defaultCapacity: 1);

            AlreadyMirrored = new List<Vector3I>(8);
            PackedColors = new List<uint>(Constants.COLOR_PALETTE_SIZE);
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }
    }
}