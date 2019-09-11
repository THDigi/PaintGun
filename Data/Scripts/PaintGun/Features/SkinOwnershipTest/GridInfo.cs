using VRage.Game.ModAPI;

namespace Digi.PaintGun.Features.SkinOwnershipTest
{
    struct GridInfo
    {
        public readonly IMyCubeGrid Grid;
        public readonly int ExpiresAtTick;

        public GridInfo(IMyCubeGrid grid, int expiresAtTick)
        {
            Grid = grid;
            ExpiresAtTick = expiresAtTick;
        }
    }
}