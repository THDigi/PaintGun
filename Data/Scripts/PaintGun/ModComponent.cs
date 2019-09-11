using Digi.ComponentLib;
using Digi.PaintGun.Features;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Features.Tool;
using Digi.PaintGun.Systems;
using Digi.PaintGun.Utilities;

namespace Digi.PaintGun
{
    /// <summary>
    /// Pass-through component to assign aliases
    /// </summary>
    public abstract class ModComponent : ComponentBase<PaintGunMod>
    {
        protected Caches Caches => Main.Caches;
        protected TextAPI TextAPI => Main.TextAPI;
        protected Palette Palette => Main.Palette;
        protected Settings Settings => Main.Settings;
        protected DrawUtils DrawUtils => Main.DrawUtils;
        protected HUDSounds HUDSounds => Main.HUDSounds;
        protected PaletteHUD PaletteHUD => Main.PaletteHUD;
        protected NetworkLibHandler NetworkLibHandler => Main.NetworkLibHandler;
        protected GameConfig GameConfig => Main.GameConfig;
        protected ToolHandler ToolHandler => Main.ToolHandler;
        protected SelectionGUI SelectionGUI => Main.SelectionGUI;
        protected Notifications Notifications => Main.Notifications;
        protected CheckPlayerField CheckPlayerField => Main.CheckPlayerField;
        protected LocalToolHandler LocalToolHandler => Main.LocalToolHandler;
        protected PaletteInputHandler PaletteInputHandler => Main.PaletteInputHandler;

        protected bool TextAPIEnabled => Main.TextAPI.IsEnabled;

        public ModComponent(PaintGunMod main) : base(main)
        {
        }
    }
}