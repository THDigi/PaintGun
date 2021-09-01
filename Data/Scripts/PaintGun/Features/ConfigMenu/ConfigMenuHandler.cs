using System.Collections.Generic;
using Digi.ComponentLib;
using Digi.PaintGun.Features.Palette;
using Sandbox.ModAPI;
using VRageMath;
using static Draygo.API.HudAPIv2;
using static Draygo.API.HudAPIv2.MenuRootCategory;

namespace Digi.PaintGun.Features.ConfigMenu
{
    /// <summary>
    /// The mod menu invoked by TextAPI
    /// </summary>
    public class ConfigMenuHandler : ModComponent
    {
        private MenuRootCategory Category_Mod;
        private MenuCategoryBase Category_Tool;
        private MenuCategoryBase Category_Palette;
        private MenuCategoryBase Category_HideSkins;
        private MenuCategoryBase Category_AimInfo;
        private MenuCategoryBase Category_Hotkeys;

        private readonly ItemGroup groupAll = new ItemGroup(); // for mass-updating titles
        private readonly ItemGroup groupSkins = new ItemGroup();

        public ConfigMenuHandler(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
            Main.TextAPI.Detected += TextAPI_Detected;
        }

        protected override void UnregisterComponent()
        {
            Main.TextAPI.Detected -= TextAPI_Detected;
            Main.Settings.SettingsLoaded -= SettingsLoaded;
        }

        protected override void UpdateAfterSim(int tick)
        {
            if(!MyAPIGateway.Gui.ChatEntryVisible)
            {
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);
                Main.ChatCommands.HelpCommand.Execute();
            }
            else
            {
                MyAPIGateway.Utilities.ShowNotification("[Close chat to see help window]", 16);
            }
        }

        void SettingsLoaded()
        {
            groupAll.Update();
        }

        private void TextAPI_Detected()
        {
            Category_Mod = new MenuRootCategory(Log.ModName, MenuFlag.PlayerMenu, Log.ModName + " Settings");

            new ItemButton(Category_Mod, "Help Window", () =>
            {
                // HACK: schedule to be shown after chat is closed, due to a soft lock bug with ShowMissionScreen() when chat is opened.
                SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);
            });

            #region Tool
            Category_Tool = AddCategory("Tool", Category_Mod);

            groupAll.Add(new ItemToggle(Category_Tool, "Spray Particles",
                    getter: () => Main.Settings.sprayParticles,
                    setter: (v) =>
                    {
                        Main.Settings.sprayParticles = v;
                        Main.Settings.ChangedByModConfig();
                    },
                    defaultValue: true));

            groupAll.Add(new ItemSlider(Category_Tool, "Spray Sound Volume", min: 0, max: 1, defaultValue: 0.8f, rounding: 2,
                getter: () => Main.Settings.spraySoundVolume,
                setter: (val) =>
                {
                    Main.Settings.spraySoundVolume = val;
                    Main.Settings.ChangedByModConfig();
                },
                sliding: (val) =>
                {
                    Main.Settings.spraySoundVolume = val;
                },
                cancelled: (orig) =>
                {
                    Main.Settings.spraySoundVolume = orig;
                }));

            groupAll.Add(new ItemToggle(Category_Tool, "Extra Sounds",
                getter: () => Main.Settings.extraSounds,
                setter: (v) =>
                {
                    Main.Settings.extraSounds = v;
                    Main.Settings.ChangedByModConfig();
                },
                defaultValue: true));
            #endregion Tool

            #region Palette
            Category_Palette = AddCategory("HUD Palette", Category_Mod);

            groupAll.Add(new ItemToggle(Category_Palette, "Select Color in Zig-Zag",
                getter: () => Main.Settings.selectColorZigZag,
                setter: (v) =>
                {
                    Main.Settings.selectColorZigZag = v;
                    Main.Settings.ChangedByModConfig();
                },
                defaultValue: false));

            groupAll.Add(new ItemToggle(Category_Palette, "Require CTRL for Color Cycle",
                getter: () => Main.Settings.requireCtrlForColorCycle,
                setter: (v) =>
                {
                    Main.Settings.requireCtrlForColorCycle = v;
                    Main.Settings.ChangedByModConfig();
                },
                defaultValue: false));

            groupAll.Add(new ItemToggle(Category_Palette, "Hide Palette with HUD",
                getter: () => Main.Settings.hidePaletteWithHUD,
                setter: (v) =>
                {
                    Main.Settings.hidePaletteWithHUD = v;
                    Main.Settings.ChangedByModConfig();
                },
                defaultValue: true));

            groupAll.Add(new ItemSlider(Category_Palette, "Background Opacity", min: -0.1f, max: 1f, defaultValue: -0.1f, rounding: 2,
                getter: () => Main.Settings.paletteBackgroundOpacity,
                setter: (val) =>
                {
                    Main.Settings.paletteBackgroundOpacity = (val < 0 ? -1 : val);
                    Main.Settings.ChangedByModConfig();
                },
                sliding: (val) =>
                {
                    Main.Settings.paletteBackgroundOpacity = (val < 0 ? -1 : val);
                    Main.PaletteHUD.UpdateUI();
                },
                cancelled: (orig) =>
                {
                    Main.Settings.paletteBackgroundOpacity = (orig < 0 ? -1 : orig);
                    Main.PaletteHUD.UpdateUI();
                },
                format: (val) =>
                {
                    return (val < 0 ? "HUD" : val.ToString("0.00"));
                }));

            groupAll.Add(new ItemBoxMove(Category_Palette, "Screen Position", min: -Vector2D.One, max: Vector2D.One, defaultValue: Settings.paletteScreenPosDefault, rounding: 3,
                getter: () => Main.Settings.paletteScreenPos,
                setter: (pos) =>
                {
                    Main.Settings.paletteScreenPos = pos;
                    Main.Settings.ChangedByModConfig();
                },
                selected: (pos) =>
                {
                    Main.Settings.paletteScreenPos = pos;
                    Main.PaletteHUD.UpdateUI();
                },
                moving: (pos) =>
                {
                    Main.Settings.paletteScreenPos = pos;
                    Main.PaletteHUD.UpdateUI();
                },
                cancelled: (origPos) =>
                {
                    Main.Settings.paletteScreenPos = origPos;
                    Main.PaletteHUD.UpdateUI();
                }));

            groupAll.Add(new ItemSlider(Category_Palette, "Scale", min: 0.1f, max: 2f, defaultValue: Settings.paletteScaleDefault, rounding: 2,
                getter: () => Main.Settings.paletteScale,
                setter: (val) =>
                {
                    Main.Settings.paletteScale = val;
                    Main.Settings.ChangedByModConfig();
                },
                sliding: (val) =>
                {
                    Main.Settings.paletteScale = val;
                    Main.PaletteHUD.UpdateUI();
                },
                cancelled: (orig) =>
                {
                    Main.Settings.paletteScale = orig;
                    Main.PaletteHUD.UpdateUI();
                }));
            #endregion Palette

            #region Palette >>> SkinsShown
            Category_HideSkins = AddCategory("Skins Shown in Palette", Category_Palette);

            groupAll.Add(new ItemToggle(Category_HideSkins, "Toggle ALL",
                getter: () => Main.Settings.hideSkinsFromPalette.Count == 0,
                setter: (v) =>
                {
                    Main.Settings.hideSkinsFromPalette.Clear();

                    if(!v)
                    {
                        for(int i = 1; i < Main.Palette.BlockSkins.Count; i++) // intentionally skipping 0
                        {
                            SkinInfo skin = Main.Palette.BlockSkins[i];
                            Main.Settings.hideSkinsFromPalette.Add(skin.SubtypeId.String);
                        }
                    }

                    Main.Settings.ChangedByModConfig();
                    groupSkins.Update();
                },
                defaultValue: true));

            List<SkinInfo> skins = Main.Palette.BlockSkins;
            for(int i = 1; i < skins.Count; i++) // intentionally skipping 0
            {
                SkinInfo skin = skins[i];
                ItemToggle item = new ItemToggle(Category_HideSkins, skin.Name,
                    getter: () => !Main.Settings.hideSkinsFromPalette.Contains(skin.SubtypeId.String),
                    setter: (v) =>
                    {
                        if(!v)
                            Main.Settings.hideSkinsFromPalette.Add(skin.SubtypeId.String);
                        else
                            Main.Settings.hideSkinsFromPalette.Remove(skin.SubtypeId.String);
                        Main.Settings.ChangedByModConfig();
                    },
                    defaultValue: true);

                groupAll.Add(item);
                groupSkins.Add(item);
            }
            #endregion Palette >>> SkinsShown

            #region AimInfo
            Category_AimInfo = AddCategory("Aimed Object Info", Category_Mod);

            // TODO: make it work with scale?
            //groupAll.Add(new ItemSlider(Category_AimInfo, "Scale", min: 0.1f, max: 2f, defaultValue: Settings.aimInfoScaleDefault, rounding: 2,
            //    getter: () => Settings.aimInfoScale,
            //    setter: (val) =>
            //    {
            //        Settings.aimInfoScale = val;
            //        Settings.ChangedByModConfig();
            //    },
            //    sliding: (val) =>
            //    {
            //        Settings.aimInfoScale = val;
            //        SelectionGUI.UpdateUISettings();
            //    },
            //    cancelled: (orig) =>
            //    {
            //        Settings.aimInfoScale = orig;
            //        SelectionGUI.UpdateUISettings();
            //    }));

            groupAll.Add(new ItemSlider(Category_AimInfo, "Background Opacity", min: -0.1f, max: 1f, defaultValue: -0.1f, rounding: 2,
                getter: () => Main.Settings.aimInfoBackgroundOpacity,
                setter: (val) =>
                {
                    Main.Settings.aimInfoBackgroundOpacity = (val < 0 ? -1 : val);
                    Main.Settings.ChangedByModConfig();
                },
                sliding: (val) =>
                {
                    Main.Settings.aimInfoBackgroundOpacity = (val < 0 ? -1 : val);
                    Main.SelectionGUI.UpdateUISettings();
                },
                cancelled: (orig) =>
                {
                    Main.Settings.aimInfoBackgroundOpacity = (orig < 0 ? -1 : orig);
                    Main.SelectionGUI.UpdateUISettings();
                },
                format: (val) =>
                {
                    return (val < 0 ? "HUD" : val.ToString("0.00"));
                }));

            groupAll.Add(new ItemBoxMove(Category_AimInfo, "Screen Position", min: -Vector2D.One, max: Vector2D.One, defaultValue: Settings.aimInfoScreenPosDefault, rounding: 3,
                getter: () => Main.Settings.aimInfoScreenPos,
                setter: (pos) =>
                {
                    Main.Settings.aimInfoScreenPos = pos;
                    Main.Settings.ChangedByModConfig();
                },
                selected: (pos) =>
                {
                    Main.Settings.aimInfoScreenPos = pos;
                    Main.SelectionGUI.UpdateUISettings();
                },
                moving: (pos) =>
                {
                    Main.Settings.aimInfoScreenPos = pos;
                    Main.SelectionGUI.UpdateUISettings();
                },
                cancelled: (origPos) =>
                {
                    Main.Settings.aimInfoScreenPos = origPos;
                    Main.SelectionGUI.UpdateUISettings();
                }));
            #endregion AimInfo

            #region Hotkeys
            Category_Hotkeys = AddCategory("Hotkeys", Category_Mod);

            groupAll.Add(new ItemInput(Category_Hotkeys, "Color Pick Mode (Bind 1)",
                getter: () => Main.Settings.colorPickMode1,
                setter: (combination) =>
                    {
                        Main.Settings.colorPickMode1 = combination;
                        Main.Settings.ChangedByModConfig();
                    },
                    defaultValue: Main.Settings.default_colorPickMode1));

            groupAll.Add(new ItemInput(Category_Hotkeys, "Color Pick Mode (Bind 2)",
                getter: () => Main.Settings.colorPickMode2,
                setter: (combination) =>
                {
                    Main.Settings.colorPickMode2 = combination;
                    Main.Settings.ChangedByModConfig();
                },
                defaultValue: Main.Settings.default_colorPickMode2));

            groupAll.Add(new ItemInput(Category_Hotkeys, "Instant Color Pick (Bind 1)",
                getter: () => Main.Settings.instantColorPick1,
                setter: (combination) =>
                {
                    Main.Settings.instantColorPick1 = combination;
                    Main.Settings.ChangedByModConfig();
                },
                defaultValue: Main.Settings.default_instantColorPick1));

            groupAll.Add(new ItemInput(Category_Hotkeys, "Instant Color Pick (Bind 2)",
                getter: () => Main.Settings.instantColorPick2,
                setter: (combination) =>
                {
                    Main.Settings.instantColorPick2 = combination;
                    Main.Settings.ChangedByModConfig();
                },
                defaultValue: Main.Settings.default_instantColorPick2));

            groupAll.Add(new ItemInput(Category_Hotkeys, "Replace Color Mode (Bind 1)",
                getter: () => Main.Settings.replaceColorMode1,
                setter: (combination) =>
                {
                    Main.Settings.replaceColorMode1 = combination;
                    Main.Settings.ChangedByModConfig();
                },
                defaultValue: Main.Settings.default_replaceColorMode1));

            groupAll.Add(new ItemInput(Category_Hotkeys, "Replace Color Mode (Bind 2)",
                getter: () => Main.Settings.replaceColorMode2,
                setter: (combination) =>
                {
                    Main.Settings.replaceColorMode2 = combination;
                    Main.Settings.ChangedByModConfig();
                },
                defaultValue: Main.Settings.default_replaceColorMode2));
            #endregion Hotkeys

            Main.Settings.SettingsLoaded += SettingsLoaded;
        }

        #region Helper methods
        private MenuCategoryBase AddCategory(string name, MenuCategoryBase parent, string header = null, ItemGroup group = null)
        {
            ItemSubMenu item = new ItemSubMenu(parent, name, header);
            group?.Add(item);
            return item.Item;
        }

        private void AddSpacer(MenuCategoryBase category, string label = null)
        {
            new MenuItem($"<color=0,55,0>{(label == null ? new string('=', 10) : $"=== {label} ===")}", category);
        }
        #endregion Helper methods
    }
}
