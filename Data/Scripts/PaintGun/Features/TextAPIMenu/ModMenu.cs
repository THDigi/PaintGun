using System;
using System.Text;
using Digi.ComponentLib;
using Sandbox.ModAPI;
using VRageMath;
using static Draygo.API.HudAPIv2;
using static Draygo.API.HudAPIv2.MenuRootCategory;

namespace Digi.PaintGun.Features.TextAPIMenu
{
    /// <summary>
    /// The mod menu invoked by TextAPI
    /// </summary>
    public class ModMenu : ModComponent
    {
        private MenuRootCategory Category_Mod;
        private MenuCategoryBase Category_Tool;
        private MenuCategoryBase Category_Palette;
        private MenuCategoryBase Category_HideSkins;
        private MenuCategoryBase Category_AimInfo;
        private MenuCategoryBase Category_Hotkeys;

        private readonly ItemGroup groupAll = new ItemGroup(); // for mass-updating titles
        private readonly ItemGroup groupSkins = new ItemGroup();

        public ModMenu(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
            TextAPI.Detected += TextAPI_Detected;
        }

        protected override void UnregisterComponent()
        {
            TextAPI.Detected -= TextAPI_Detected;
            Settings.SettingsLoaded -= SettingsLoaded;
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
                    getter: () => Settings.sprayParticles,
                    setter: (v) =>
                    {
                        Settings.sprayParticles = v;
                        Settings.ChangedByModConfig();
                    },
                    defaultValue: true));

            groupAll.Add(new ItemSlider(Category_Tool, "Spray Sound Volume", min: 0, max: 1, defaultValue: 0.8f, rounding: 2,
                getter: () => Settings.spraySoundVolume,
                setter: (val) =>
                {
                    Settings.spraySoundVolume = val;
                    Settings.ChangedByModConfig();
                },
                sliding: (val) =>
                {
                    Settings.spraySoundVolume = val;
                },
                cancelled: (orig) =>
                {
                    Settings.spraySoundVolume = orig;
                }));

            groupAll.Add(new ItemToggle(Category_Tool, "Extra Sounds",
                getter: () => Settings.extraSounds,
                setter: (v) =>
                {
                    Settings.extraSounds = v;
                    Settings.ChangedByModConfig();
                },
                defaultValue: true));
            #endregion Tool

            #region Palette
            Category_Palette = AddCategory("HUD Palette", Category_Mod);

            groupAll.Add(new ItemToggle(Category_Palette, "Select Color in Zig-Zag",
                getter: () => Settings.selectColorZigZag,
                setter: (v) =>
                {
                    Settings.selectColorZigZag = v;
                    Settings.ChangedByModConfig();
                },
                defaultValue: false));

            groupAll.Add(new ItemToggle(Category_Palette, "Require CTRL for Color Cycle",
                getter: () => Settings.requireCtrlForColorCycle,
                setter: (v) =>
                {
                    Settings.requireCtrlForColorCycle = v;
                    Settings.ChangedByModConfig();
                },
                defaultValue: false));

            groupAll.Add(new ItemToggle(Category_Palette, "Hide Palette with HUD",
                getter: () => Settings.hidePaletteWithHUD,
                setter: (v) =>
                {
                    Settings.hidePaletteWithHUD = v;
                    Settings.ChangedByModConfig();
                },
                defaultValue: true));

            groupAll.Add(new ItemSlider(Category_Palette, "Background Opacity", min: -0.1f, max: 1f, defaultValue: -0.1f, rounding: 2,
                getter: () => Settings.paletteBackgroundOpacity,
                setter: (val) =>
                {
                    Settings.paletteBackgroundOpacity = (val < 0 ? -1 : val);
                    Settings.ChangedByModConfig();
                },
                sliding: (val) =>
                {
                    Settings.paletteBackgroundOpacity = (val < 0 ? -1 : val);
                    PaletteHUD.UpdateUI();
                },
                cancelled: (orig) =>
                {
                    Settings.paletteBackgroundOpacity = (orig < 0 ? -1 : orig);
                    PaletteHUD.UpdateUI();
                },
                format: (val) =>
                {
                    return (val < 0 ? "HUD" : val.ToString("0.00"));
                }));

            groupAll.Add(new ItemBoxMove(Category_Palette, "Screen Position", min: -Vector2D.One, max: Vector2D.One, defaultValue: Settings.paletteScreenPosDefault, rounding: 3,
                getter: () => Settings.paletteScreenPos,
                setter: (pos) =>
                {
                    Settings.paletteScreenPos = pos;
                    Settings.ChangedByModConfig();
                },
                selected: (pos) =>
                {
                    Settings.paletteScreenPos = pos;
                    PaletteHUD.UpdateUI();
                },
                moving: (pos) =>
                {
                    Settings.paletteScreenPos = pos;
                    PaletteHUD.UpdateUI();
                },
                cancelled: (origPos) =>
                {
                    Settings.paletteScreenPos = origPos;
                    PaletteHUD.UpdateUI();
                }));

            groupAll.Add(new ItemSlider(Category_Palette, "Scale", min: 0.1f, max: 2f, defaultValue: Settings.paletteScaleDefault, rounding: 2,
                getter: () => Settings.paletteScale,
                setter: (val) =>
                {
                    Settings.paletteScale = val;
                    Settings.ChangedByModConfig();
                },
                sliding: (val) =>
                {
                    Settings.paletteScale = val;
                    PaletteHUD.UpdateUI();
                },
                cancelled: (orig) =>
                {
                    Settings.paletteScale = orig;
                    PaletteHUD.UpdateUI();
                }));
            #endregion Palette

            #region Palette >>> SkinsShown
            Category_HideSkins = AddCategory("Skins Shown in Palette", Category_Palette);

            groupAll.Add(new ItemToggle(Category_HideSkins, "Toggle ALL",
                getter: () => Settings.hideSkinsFromPalette.Count == 0,
                setter: (v) =>
                {
                    Settings.hideSkinsFromPalette.Clear();

                    if(!v)
                    {
                        for(int i = 1; i < Palette.BlockSkins.Count; i++) // intentionally skipping 0
                        {
                            var skin = Palette.BlockSkins[i];
                            Settings.hideSkinsFromPalette.Add(skin.SubtypeId.String);
                        }
                    }

                    Settings.ChangedByModConfig();
                    groupSkins.Update();
                },
                defaultValue: true));

            var skins = Palette.BlockSkins;
            for(int i = 1; i < skins.Count; i++) // intentionally skipping 0
            {
                var skin = skins[i];
                var item = new ItemToggle(Category_HideSkins, skin.Name,
                    getter: () => !Settings.hideSkinsFromPalette.Contains(skin.SubtypeId.String),
                    setter: (v) =>
                    {
                        if(!v)
                            Settings.hideSkinsFromPalette.Add(skin.SubtypeId.String);
                        else
                            Settings.hideSkinsFromPalette.Remove(skin.SubtypeId.String);
                        Settings.ChangedByModConfig();
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
                getter: () => Settings.aimInfoBackgroundOpacity,
                setter: (val) =>
                {
                    Settings.aimInfoBackgroundOpacity = (val < 0 ? -1 : val);
                    Settings.ChangedByModConfig();
                },
                sliding: (val) =>
                {
                    Settings.aimInfoBackgroundOpacity = (val < 0 ? -1 : val);
                    SelectionGUI.UpdateUISettings();
                },
                cancelled: (orig) =>
                {
                    Settings.aimInfoBackgroundOpacity = (orig < 0 ? -1 : orig);
                    SelectionGUI.UpdateUISettings();
                },
                format: (val) =>
                {
                    return (val < 0 ? "HUD" : val.ToString("0.00"));
                }));

            groupAll.Add(new ItemBoxMove(Category_AimInfo, "Screen Position", min: -Vector2D.One, max: Vector2D.One, defaultValue: Settings.aimInfoScreenPosDefault, rounding: 3,
                getter: () => Settings.aimInfoScreenPos,
                setter: (pos) =>
                {
                    Settings.aimInfoScreenPos = pos;
                    Settings.ChangedByModConfig();
                },
                selected: (pos) =>
                {
                    Settings.aimInfoScreenPos = pos;
                    SelectionGUI.UpdateUISettings();
                },
                moving: (pos) =>
                {
                    Settings.aimInfoScreenPos = pos;
                    SelectionGUI.UpdateUISettings();
                },
                cancelled: (origPos) =>
                {
                    Settings.aimInfoScreenPos = origPos;
                    SelectionGUI.UpdateUISettings();
                }));
            #endregion AimInfo

            #region Hotkeys
            Category_Hotkeys = AddCategory("Hotkeys", Category_Mod);

            groupAll.Add(new ItemInput(Category_Hotkeys, "Color Pick Mode (Bind 1)",
                getter: () => Settings.colorPickMode1,
                setter: (combination) =>
                    {
                        Settings.colorPickMode1 = combination;
                        Settings.ChangedByModConfig();
                    },
                    defaultValue: Settings.default_colorPickMode1));

            groupAll.Add(new ItemInput(Category_Hotkeys, "Color Pick Mode (Bind 2)",
                getter: () => Settings.colorPickMode2,
                setter: (combination) =>
                {
                    Settings.colorPickMode2 = combination;
                    Settings.ChangedByModConfig();
                },
                defaultValue: Settings.default_colorPickMode2));

            groupAll.Add(new ItemInput(Category_Hotkeys, "Instant Color Pick (Bind 1)",
                getter: () => Settings.instantColorPick1,
                setter: (combination) =>
                {
                    Settings.instantColorPick1 = combination;
                    Settings.ChangedByModConfig();
                },
                defaultValue: Settings.default_instantColorPick1));

            groupAll.Add(new ItemInput(Category_Hotkeys, "Instant Color Pick (Bind 2)",
                getter: () => Settings.instantColorPick2,
                setter: (combination) =>
                {
                    Settings.instantColorPick2 = combination;
                    Settings.ChangedByModConfig();
                },
                defaultValue: Settings.default_instantColorPick2));

            groupAll.Add(new ItemInput(Category_Hotkeys, "Replace Color Mode (Bind 1)",
                getter: () => Settings.replaceColorMode1,
                setter: (combination) =>
                {
                    Settings.replaceColorMode1 = combination;
                    Settings.ChangedByModConfig();
                },
                defaultValue: Settings.default_replaceColorMode1));

            groupAll.Add(new ItemInput(Category_Hotkeys, "Replace Color Mode (Bind 2)",
                getter: () => Settings.replaceColorMode2,
                setter: (combination) =>
                {
                    Settings.replaceColorMode2 = combination;
                    Settings.ChangedByModConfig();
                },
                defaultValue: Settings.default_replaceColorMode2));
            #endregion Hotkeys

            Settings.SettingsLoaded += SettingsLoaded;
        }

        #region Helper methods
        private MenuCategoryBase AddCategory(string name, MenuCategoryBase parent, string header = null, ItemGroup group = null)
        {
            var item = new ItemSubMenu(parent, name, header);
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
