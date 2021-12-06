using Sandbox.Definitions;
using VRage.Utils;

namespace Digi.PaintGun.Features.Palette
{
    public class SkinInfo
    {
        public readonly MyAssetModifierDefinition Definition;
        public readonly MyStringHash SubtypeId;
        public readonly string Name;
        public readonly MyStringId Icon;
        public readonly bool AlwaysOwned;
        public bool LocallyOwned;

        /// <summary>
        /// Configurable for local machine.
        /// </summary>
        public bool ShowOnPalette { get; private set; }

        /// <summary>
        /// Owned and not turned off in config.
        /// </summary>
        public bool Selectable { get; private set; }

        public SkinInfo(MyAssetModifierDefinition definition, string name, string icon)
        {
            // definition is null for no-skin one.
            Definition = definition;
            SubtypeId = (definition == null ? MyStringHash.NullOrEmpty : definition.Id.SubtypeId);

            Name = name;
            Icon = MyStringId.GetOrCompute(icon);

            // mod DLC-less skins are always owned
            // TODO: check if it's a vanilla definition that originally has DLC requirement and ignore those, but no way to get original vanilla definitions...
            AlwaysOwned = Definition == null || (!Definition.Context.IsBaseGame && (Definition.DLCs == null || Definition.DLCs.Length == 0));

            // HACK: just using sync'd paint API to let it decide on its own
            LocallyOwned = true;
        }

        /// <summary>
        /// Re-computes <see cref="ShowOnPalette"/> and <see cref="Selectable"/>
        /// </summary>
        public void Refresh()
        {
            ShowOnPalette = !PaintGunMod.Instance.Settings.hideSkinsFromPalette.Contains(SubtypeId.String);
            Selectable = ShowOnPalette && LocallyOwned;
        }
    }
}