using VRage.Game;
using VRage.Utils;

namespace Digi.PaintGun.Features.Palette
{
    public class SkinInfo
    {
        public int Index;
        public readonly MyStringHash SubtypeId;
        public readonly string Name;
        public readonly MyStringId Icon;
        public bool LocallyOwned;
        public MyModContext Mod;

        public SkinInfo(MyStringHash subtypeId, string name, string icon, bool locallyOwned = false)
        {
            SubtypeId = subtypeId;
            Name = name;
            Icon = MyStringId.GetOrCompute(icon);
            LocallyOwned = locallyOwned;
        }
    }
}