using Digi.ComponentLib;

namespace Digi.PaintGun
{
    /// <summary>
    /// Pass-through component to assign aliases
    /// </summary>
    public abstract class ModComponent : ComponentBase<PaintGunMod>
    {
        public ModComponent(PaintGunMod main) : base(main)
        {
        }
    }
}