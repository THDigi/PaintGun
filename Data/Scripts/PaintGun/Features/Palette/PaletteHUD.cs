using System;
using System.Text;
using Digi.ComponentLib;
using Digi.PaintGun.Features.Tool;
using Digi.PaintGun.Systems;
using Digi.PaintGun.Utilities;
using Draygo.API;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace Digi.PaintGun.Features.Palette
{
    public class PaletteHUD : ModComponent
    {
        HudAPIv2.HUDMessage skinLabel;
        HudAPIv2.HUDMessage skinLabelShadow;
        StringBuilder skinLabelSB;
        StringBuilder skinLabelShadowSB;
        bool drawSkinLabel = false;
        bool skinLabelVisible = false;
        bool skinLabelUpdate;

        public readonly MyStringId MATERIAL_DOT = MyStringId.GetOrCompute("WhiteDot");
        public const BlendTypeEnum AIM_DOT_BLEND_TYPE = BlendTypeEnum.SDR;

        public readonly MyStringId MATERIAL_PALETTE_COLOR = MyStringId.GetOrCompute("PaintGunPalette_Color");
        public readonly MyStringId MATERIAL_PALETTE_SELECTED = MyStringId.GetOrCompute("PaintGunPalette_Selected");
        public readonly MyStringId MATERIAL_PALETTE_BACKGROUND = MyStringId.GetOrCompute("PaintGunPalette_Background");
        public readonly Color PALETTE_BG_COLOR = new Color(41, 54, 62);
        public const BlendTypeEnum GUI_FG_BLENDTYPE = BlendTypeEnum.PostPP;
        public const BlendTypeEnum GUI_BG_BLENDTYPE = BlendTypeEnum.PostPP;

        public PaletteHUD(PaintGunMod main) : base(main)
        {
        }

        protected override void RegisterComponent()
        {
            Palette.LocalInfo.OnSkinIndexSelected += SkinIndexSelected;
            Palette.LocalInfo.OnApplyColorChanged += ApplyColorChanged;
            LocalToolHandler.LocalToolEquipped += LocalToolEquipped;
            LocalToolHandler.LocalToolHolstered += LocalToolHolstered;
        }

        protected override void UnregisterComponent()
        {
            Palette.LocalInfo.OnSkinIndexSelected -= SkinIndexSelected;
            Palette.LocalInfo.OnApplyColorChanged -= ApplyColorChanged;
            LocalToolHandler.LocalToolEquipped -= LocalToolEquipped;
            LocalToolHandler.LocalToolHolstered -= LocalToolHolstered;
        }

        void SkinIndexSelected(PlayerInfo pi, int prevIndex, int newIndex)
        {
            skinLabelUpdate = true;
        }

        void ApplyColorChanged(PlayerInfo pi)
        {
            skinLabelUpdate = true;
        }

        void LocalToolEquipped(PaintGunItem item)
        {
            SetUpdateMethods(UpdateFlags.UPDATE_DRAW, true);
        }

        void LocalToolHolstered(PaintGunItem item)
        {
            SetUpdateMethods(UpdateFlags.UPDATE_DRAW, false);
        }

        protected override void UpdateDraw()
        {
            if(!CheckPlayerField.Ready)
                return;

            drawSkinLabel = false;

            DrawPaletteHUD();

            if(!drawSkinLabel && skinLabel != null)
            {
                skinLabelVisible = false;
                skinLabel.Visible = false;
                skinLabelShadow.Visible = false;
            }
        }

        void DrawPaletteHUD()
        {
            var localInfo = Palette.LocalInfo;
            if(localInfo == null)
                return;

            if(LocalToolHandler.LocalTool != null && !MyAPIGateway.Gui.IsCursorVisible && !(Settings.hidePaletteWithHUD && GameConfig.HudState == HudState.OFF))
            {
                var cam = MyAPIGateway.Session.Camera;
                var camMatrix = cam.WorldMatrix;
                var scaleFOV = Math.Tan(cam.FovWithZoom * 0.5);
                scaleFOV *= Settings.paletteScale;
                float scaleFOVf = (float)scaleFOV;

                var character = MyAPIGateway.Session.Player.Character;

                if(Utils.IsAimingDownSights(character))
                {
                    MyTransparentGeometry.AddPointBillboard(MATERIAL_DOT, Color.Lime, camMatrix.Translation + camMatrix.Forward * LocalToolHandler.PAINT_AIM_START_OFFSET, 0.005f, 0, blendType: AIM_DOT_BLEND_TYPE);
                }

                var worldPos = DrawUtils.HUDtoWorld(Settings.paletteScreenPos);
                var bgAlpha = (Settings.paletteBackgroundOpacity < 0 ? GameConfig.HudBackgroundOpacity : Settings.paletteBackgroundOpacity);

                #region Color selector
                if(localInfo.ApplyColor)
                {
                    float squareWidth = 0.0014f * scaleFOVf;
                    float squareHeight = 0.0010f * scaleFOVf;
                    float selectedWidth = (squareWidth + (squareWidth / 3f));
                    float selectedHeight = (squareHeight + (squareHeight / 3f));
                    double spacingAdd = 0.0006 * scaleFOV;
                    double spacingWidth = (squareWidth * 2) + spacingAdd;
                    double spacingHeight = (squareHeight * 2) + spacingAdd;
                    const int MIDDLE_INDEX = 7;
                    const float BG_WIDTH_MUL = 3.85f;
                    const float BG_HEIGHT_MUL = 1.3f;

                    MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_BACKGROUND, PALETTE_BG_COLOR * bgAlpha, worldPos, camMatrix.Left, camMatrix.Up, (float)(spacingWidth * BG_WIDTH_MUL), (float)(spacingHeight * BG_HEIGHT_MUL), Vector2.Zero, GUI_BG_BLENDTYPE);

                    var pos = worldPos + camMatrix.Left * (spacingWidth * (MIDDLE_INDEX / 2)) + camMatrix.Up * (spacingHeight / 2);

                    for(int i = 0; i < localInfo.ColorsMasks.Count; i++)
                    {
                        var colorMask = localInfo.ColorsMasks[i];
                        var rgb = Utils.ColorMaskToRGB(colorMask);

                        if(i == MIDDLE_INDEX)
                            pos += camMatrix.Left * (spacingWidth * MIDDLE_INDEX) + camMatrix.Down * spacingHeight;

                        if(i == localInfo.SelectedColorIndex)
                            MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, Color.White, pos, camMatrix.Left, camMatrix.Up, selectedWidth, selectedHeight, Vector2.Zero, GUI_FG_BLENDTYPE);

                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, rgb, pos, camMatrix.Left, camMatrix.Up, squareWidth, squareHeight, Vector2.Zero, GUI_FG_BLENDTYPE);

                        pos += camMatrix.Right * spacingWidth;
                    }
                }
                #endregion Color selector

                #region Skin selector
                if(localInfo.ApplySkin && Palette.OwnedSkins > 0)
                {
                    float iconSize = 0.003f * scaleFOVf;
                    float selectedIconSize = iconSize; // 0.0034f * scaleFOV;
                    var selectedSkinIndex = localInfo.SelectedSkinIndex;
                    double iconSpacingAdd = 0; // 0.0012 * scaleFOV;
                    double iconSpacingWidth = (iconSize * 2) + iconSpacingAdd;
                    float iconBgSpacingAddWidth = 0.0004f * scaleFOVf;
                    float iconBgSpacingAddHeight = 0.0006f * scaleFOVf;
                    //float iconBgSpacingAddWidth = 0.0006f * scaleFOVf;
                    //float iconBgSpacingAddHeight = 0.0008f * scaleFOVf;

                    var pos = worldPos;

                    if(localInfo.ApplyColor)
                        pos += camMatrix.Up * (0.0075f * scaleFOV);

                    if(TextAPIEnabled) // skin name text
                    {
                        var labelPos = Settings.paletteScreenPos + new Vector2D(0, 0.07);

                        if(localInfo.ApplyColor)
                            labelPos += new Vector2D(0, 0.08);

                        var skin = Palette.GetSkinInfo(selectedSkinIndex);
                        var text = skin.Name;

                        const string SKINLABEL_COLOR = "<color=255,255,255>";
                        const string SKINLABEL_SHADOW_COLOR = "<color=0,0,0>";
                        const double SCALE = 0.8;

                        if(skinLabel == null)
                        {
                            skinLabelSB = new StringBuilder(64).Append(SKINLABEL_COLOR).Append(text);
                            skinLabelShadowSB = new StringBuilder(64).Append(SKINLABEL_SHADOW_COLOR).Append(text);

                            skinLabel = new HudAPIv2.HUDMessage(skinLabelSB, labelPos, Scale: SCALE, HideHud: true, Blend: BlendTypeEnum.PostPP);
                            skinLabelShadow = new HudAPIv2.HUDMessage(skinLabelShadowSB, labelPos, Scale: SCALE, HideHud: true, Blend: BlendTypeEnum.SDR);

                            skinLabelUpdate = true;
                        }

                        if(skinLabelUpdate)
                        {
                            skinLabelUpdate = false;

                            skinLabelSB.Clear();
                            skinLabelSB.Append(SKINLABEL_COLOR).Append(text);

                            skinLabelShadowSB.Clear();
                            skinLabelShadowSB.Append(SKINLABEL_SHADOW_COLOR).Append(text);

                            skinLabel.Origin = labelPos;
                            skinLabelShadow.Origin = labelPos;

                            var textLen = skinLabel.GetTextLength();
                            skinLabel.Offset = new Vector2D(textLen.X * -0.5, 0);
                            skinLabelShadow.Offset = skinLabel.Offset + new Vector2D(0.0015, -0.0015);
                        }

                        if(!skinLabelVisible)
                        {
                            skinLabelVisible = true;
                            skinLabel.Visible = true;
                            skinLabelShadow.Visible = true;
                        }

                        drawSkinLabel = true;
                    }

                    const int MAX_VIEW_SKINS = 7; // must be an odd number.
                    const int MAX_VIEW_SKINS_HALF = ((MAX_VIEW_SKINS - 1) / 2);
                    const double MAX_VIEW_SKINS_HALF_D = (MAX_VIEW_SKINS / 2d);
                    //const double MAX_VIEW_SKINS_HALF_D_BG = ((MAX_VIEW_SKINS - 4) / 2d);

                    if(Palette.OwnedSkins > MAX_VIEW_SKINS)
                    {
                        //var bgPos = pos + camMatrix.Right * ((iconSpacingWidth * 0.5) - (iconSpacingWidth * 0.5));
                        //MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_BACKGROUND, PALETTE_COLOR_BG * bgAlpha, bgPos, camMatrix.Left, camMatrix.Up, (float)(iconSpacingWidth * MAX_VIEW_SKINS_HALF_D_BG) + iconBgSpacingAddWidth, iconSize + iconBgSpacingAddHeight, Vector2.Zero, UI_BG_BLENDTYPE);

                        pos += camMatrix.Left * ((iconSpacingWidth * MAX_VIEW_SKINS_HALF_D) - (iconSpacingWidth * 0.5));

                        int index = selectedSkinIndex - MAX_VIEW_SKINS_HALF;
                        bool ignoreDistance = false;

                        if(index < 0)
                        {
                            index += Palette.BlockSkins.Count;
                            ignoreDistance = true;
                        }

                        for(int a = 0; a < MAX_VIEW_SKINS; ++a)
                        {
                            // cycle through nearest owned skins
                            while(!Palette.GetSkinInfo(index).LocallyOwned || (!ignoreDistance && Math.Abs(selectedSkinIndex - index) > MAX_VIEW_SKINS_HALF))
                            {
                                index++;

                                if(index >= Palette.BlockSkins.Count)
                                {
                                    ignoreDistance = true;
                                    index = 0;
                                }
                            }

                            var skin = Palette.GetSkinInfo(index);

                            float alpha = 1f;

                            if(a == 0 || a == (MAX_VIEW_SKINS - 1))
                                alpha = 0.1f;
                            else if(a == 1 || a == (MAX_VIEW_SKINS - 2))
                                alpha = 0.5f;
                            //else if(a == 2 || a == (MAX_VIEW_SKINS - 3))
                            //    alpha = 0.75f;

                            if(selectedSkinIndex == index)
                                MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, Color.White, pos, camMatrix.Left, camMatrix.Up, selectedIconSize, selectedIconSize, Vector2.Zero, GUI_FG_BLENDTYPE);

                            MyTransparentGeometry.AddBillboardOriented(skin.Icon, Color.White * alpha, pos, camMatrix.Left, camMatrix.Up, iconSize, iconSize, Vector2.Zero, GUI_FG_BLENDTYPE);

                            pos += camMatrix.Right * iconSpacingWidth;

                            index++;

                            if(index >= Palette.BlockSkins.Count)
                            {
                                ignoreDistance = true;
                                index = 0;
                            }
                        }
                    }
                    else
                    {
                        double halfOwnedSkins = Palette.OwnedSkins * 0.5;

                        //MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_BACKGROUND, PALETTE_COLOR_BG * bgAlpha, pos, camMatrix.Left, camMatrix.Up, (float)(iconSpacingWidth * halfOwnedSkins) + iconBgSpacingAddWidth, iconSize + iconBgSpacingAddHeight, Vector2.Zero, UI_BG_BLENDTYPE);

                        pos += camMatrix.Left * ((iconSpacingWidth * halfOwnedSkins) - (iconSpacingWidth * 0.5));

                        for(int i = 0; i < Palette.BlockSkins.Count; ++i)
                        {
                            var skin = Palette.GetSkinInfo(i);

                            if(!skin.LocallyOwned)
                                continue;

                            if(selectedSkinIndex == i)
                            {
                                MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, Color.White, pos, camMatrix.Left, camMatrix.Up, selectedIconSize, selectedIconSize, Vector2.Zero, GUI_FG_BLENDTYPE);
                            }

                            MyTransparentGeometry.AddBillboardOriented(skin.Icon, Color.White, pos, camMatrix.Left, camMatrix.Up, iconSize, iconSize, Vector2.Zero, GUI_FG_BLENDTYPE);

                            pos += camMatrix.Right * iconSpacingWidth;
                        }
                    }
                }
                #endregion Skin selector
            }
        }
    }
}