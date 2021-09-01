using Sandbox.Definitions;
using VRage.Utils;

namespace Digi.PaintGun.Features.Palette
{
    public class SkinInfo
    {
        public int Index;
        public readonly MyAssetModifierDefinition Definition;
        public readonly MyStringHash SubtypeId;
        public readonly string Name;
        public readonly MyStringId Icon;
        public readonly bool AlwaysOwned;
        public bool LocallyOwned;

        /// <summary>
        /// Configurable for local machine.
        /// </summary>
        public bool ShowOnPalette;

        /// <summary>
        /// Owned and not turned off in config.
        /// </summary>
        public bool Selectable => LocallyOwned && ShowOnPalette;

        public SkinInfo(MyAssetModifierDefinition definition, string name, string icon)
        {
            // definition is null for no-skin one.
            Definition = definition;
            SubtypeId = (definition == null ? MyStringHash.NullOrEmpty : definition.Id.SubtypeId);

            Name = name;
            Icon = MyStringId.GetOrCompute(icon);

            // mod-added DLC-less skins are always owned
            AlwaysOwned = Definition == null || (!Definition.Context.IsBaseGame && (Definition.DLCs == null || Definition.DLCs.Length == 0));
            LocallyOwned = true; // HACK: just using sync'd paint API to let it decide on its own
        }
    }
}