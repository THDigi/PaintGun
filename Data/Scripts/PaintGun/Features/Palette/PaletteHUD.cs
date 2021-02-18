using System;
using System.Collections.Generic;
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
        bool drawSkinLabel = false;
        bool skinLabelVisible = false;
        bool skinLabelUpdate;

        HudAPIv2.HUDMessage skinTestStatus;

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
            Settings.SettingsChanged += UpdateUI;
        }

        protected override void UnregisterComponent()
        {
            if(!IsRegistered)
                return;

            Palette.LocalInfo.OnSkinIndexSelected -= SkinIndexSelected;
            Palette.LocalInfo.OnApplyColorChanged -= ApplyColorChanged;
            LocalToolHandler.LocalToolEquipped -= LocalToolEquipped;
            LocalToolHandler.LocalToolHolstered -= LocalToolHolstered;
            Settings.SettingsChanged -= UpdateUI;
        }

        void SkinIndexSelected(PlayerInfo pi, int prevIndex, int newIndex)
        {
            skinLabelUpdate = true;
        }

        void ApplyColorChanged(PlayerInfo pi)
        {
            skinLabelUpdate = true;
        }

        public void UpdateUI()
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
                MatrixD camMatrix = cam.WorldMatrix;
                double scaleFOV = Math.Tan(cam.FovWithZoom * 0.5);
                scaleFOV *= Settings.paletteScale;
                float scaleFOVf = (float)scaleFOV;

                var character = MyAPIGateway.Session.Player.Character;

                if(Utils.IsAimingDownSights(character))
                {
                    MyTransparentGeometry.AddPointBillboard(MATERIAL_DOT, Color.Lime, camMatrix.Translation + camMatrix.Forward * LocalToolHandler.PAINT_AIM_START_OFFSET, 0.005f, 0, blendType: AIM_DOT_BLEND_TYPE);
                }

                Vector3D worldPos = DrawUtils.HUDtoWorld(Settings.paletteScreenPos);
                float bgAlpha = (Settings.paletteBackgroundOpacity < 0 ? GameConfig.HudBackgroundOpacity : Settings.paletteBackgroundOpacity);

                DrawColorSelector(camMatrix, worldPos, scaleFOVf, bgAlpha);
                DrawSkinSelector(camMatrix, worldPos, scaleFOVf, bgAlpha);

                if(TextAPIEnabled && Palette.OwnedSkinsCount <= 0)
                {
                    var labelPos = Settings.paletteScreenPos + new Vector2D(0, 0.08);

                    if(skinTestStatus == null)
                    {
                        skinTestStatus = new HudAPIv2.HUDMessage(Main.OwnershipTestPlayer.Status, labelPos, Scale: 0.8, HideHud: true, Shadowing: true, Blend: BlendTypeEnum.PostPP);
                        skinTestStatus.TimeToLive = Constants.TICKS_PER_SECOND * 60 * 2; // delete after this time in case nothing else hides it
                    }

                    skinTestStatus.Origin = labelPos;
                    skinTestStatus.Visible = true;

                    var textLen = skinTestStatus.GetTextLength();
                    skinTestStatus.Offset = new Vector2D(textLen.X * -0.5, 0); // centered
                }
            }
        }

        void DrawColorSelector(MatrixD camMatrix, Vector3D worldPos, float scaleFOV, float bgAlpha)
        {
            var localInfo = Palette.LocalInfo;
            if(!localInfo.ApplyColor)
                return;

            float squareWidth = 0.0014f * scaleFOV;
            float squareHeight = 0.0010f * scaleFOV;
            float selectedWidth = (squareWidth + (squareWidth / 3f));
            float selectedHeight = (squareHeight + (squareHeight / 3f));
            double spacingAdd = 0.0006 * scaleFOV;
            double spacingWidth = (squareWidth * 2) + spacingAdd;
            double spacingHeight = (squareHeight * 2) + spacingAdd;
            const int MIDDLE_INDEX = 7;
            const float BG_WIDTH_MUL = 3.85f;
            const float BG_HEIGHT_MUL = 1.3f;

            var bgColor = Utils.HUDColorAlpha(PALETTE_BG_COLOR, bgAlpha);
            MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_BACKGROUND, bgColor, worldPos, camMatrix.Left, camMatrix.Up, (float)(spacingWidth * BG_WIDTH_MUL), (float)(spacingHeight * BG_HEIGHT_MUL), Vector2.Zero, GUI_BG_BLENDTYPE);

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

        void DrawSkinSelector(MatrixD camMatrix, Vector3D worldPos, float scaleFOV, float bgAlpha)
        {
            var localInfo = Palette.LocalInfo;
            if(!localInfo.ApplySkin || Palette.OwnedSkinsCount <= 0)
                return;

            List<SkinInfo> skins = Palette.SkinsForHUD;
            if(skins == null)
                return;

            int skinsCount = skins.Count;
            if(skinsCount <= 0)
                return;

            if(skinTestStatus != null)
            {
                skinTestStatus.Visible = false;
                skinTestStatus.DeleteMessage();
                skinTestStatus = null;
            }

            float iconSize = 0.0024f * scaleFOV;
            float selectedIconSize = 0.003f * scaleFOV;
            var selectedSkinIndex = localInfo.SelectedSkinIndex;
            double iconSpacingAdd = (selectedIconSize - iconSize); // 0.0012 * scaleFOV;
            double iconSpacingWidth = (iconSize * 2) + iconSpacingAdd;
            float iconBgSpacingAddWidth = 0.0004f * scaleFOV;
            float iconBgSpacingAddHeight = 0.0006f * scaleFOV;
            //float iconBgSpacingAddWidth = 0.0006f * scaleFOV;
            //float iconBgSpacingAddHeight = 0.0008f * scaleFOV;

            var pos = worldPos;

            if(localInfo.ApplyColor)
                pos += camMatrix.Up * (0.0075f * scaleFOV);

            DrawSkinNameText(selectedSkinIndex);

            const int MAX_VIEW_SKINS = 7; // must be an odd number.
            const int MAX_VIEW_SKINS_HALF = ((MAX_VIEW_SKINS - 1) / 2);
            const double MAX_VIEW_SKINS_HALF_D = (MAX_VIEW_SKINS / 2d);
            //const double MAX_VIEW_SKINS_HALF_D_BG = ((MAX_VIEW_SKINS - 4) / 2d);

            if(skinsCount >= MAX_VIEW_SKINS)
            {
                //var bgPos = pos + camMatrix.Right * ((iconSpacingWidth * 0.5) - (iconSpacingWidth * 0.5));
                //MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_BACKGROUND, PALETTE_COLOR_BG * bgAlpha, bgPos, camMatrix.Left, camMatrix.Up, (float)(iconSpacingWidth * MAX_VIEW_SKINS_HALF_D_BG) + iconBgSpacingAddWidth, iconSize + iconBgSpacingAddHeight, Vector2.Zero, UI_BG_BLENDTYPE);

                pos += camMatrix.Left * ((iconSpacingWidth * MAX_VIEW_SKINS_HALF_D) - (iconSpacingWidth * 0.5));

                int skinIndex = 0;

                for(int i = 0; i < skinsCount; ++i)
                {
                    var skin = skins[i];

                    if(skin.Index == selectedSkinIndex)
                    {
                        skinIndex = i;
                        break;
                    }
                }

                const float MIN_ALPHA = 0.5f; // alpha of the skin icon on the edge of the scrollable bar
                float alphaSubtractStep = ((1f - MIN_ALPHA) / MAX_VIEW_SKINS_HALF);

                int index = skinIndex - MAX_VIEW_SKINS_HALF;

                for(int a = -MAX_VIEW_SKINS_HALF; a <= MAX_VIEW_SKINS_HALF; ++a)
                {
                    if(index < 0)
                        index = skinsCount + index;
                    if(index >= skinsCount)
                        index %= skinsCount;

                    var skin = skins[index];

                    float alpha = 1f - (Math.Abs(a) * alphaSubtractStep);

                    if(selectedSkinIndex == skin.Index)
                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, Color.White, pos, camMatrix.Left, camMatrix.Up, selectedIconSize, selectedIconSize, Vector2.Zero, GUI_FG_BLENDTYPE);

                    MyTransparentGeometry.AddBillboardOriented(skin.Icon, Color.White * alpha, pos, camMatrix.Left, camMatrix.Up, iconSize, iconSize, Vector2.Zero, GUI_FG_BLENDTYPE);

                    pos += camMatrix.Right * iconSpacingWidth;

                    index++;
                }
            }
            else
            {
                double halfOwnedSkins = skinsCount * 0.5;

                //MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_BACKGROUND, PALETTE_COLOR_BG * bgAlpha, pos, camMatrix.Left, camMatrix.Up, (float)(iconSpacingWidth * halfOwnedSkins) + iconBgSpacingAddWidth, iconSize + iconBgSpacingAddHeight, Vector2.Zero, UI_BG_BLENDTYPE);

                pos += camMatrix.Left * ((iconSpacingWidth * halfOwnedSkins) - (iconSpacingWidth * 0.5));

                foreach(var skin in skins)
                {
                    if(selectedSkinIndex == skin.Index)
                    {
                        MyTransparentGeometry.AddBillboardOriented(MATERIAL_PALETTE_COLOR, Color.White, pos, camMatrix.Left, camMatrix.Up, selectedIconSize, selectedIconSize, Vector2.Zero, GUI_FG_BLENDTYPE);
                    }

                    MyTransparentGeometry.AddBillboardOriented(skin.Icon, Color.White, pos, camMatrix.Left, camMatrix.Up, iconSize, iconSize, Vector2.Zero, GUI_FG_BLENDTYPE);

                    pos += camMatrix.Right * iconSpacingWidth;
                }
            }
        }

        void DrawSkinNameText(int selectedSkinIndex)
        {
            if(!TextAPIEnabled)
                return;

            var localInfo = Palette.LocalInfo;

            float scale = Settings.paletteScale;
            var labelPos = Settings.paletteScreenPos + new Vector2D(0, 0.06 * 2 * scale); // TODO FIX: needs to move relatively with the scale of the elements below it, but it doesn't...

            if(localInfo.ApplyColor)
                labelPos += new Vector2D(0, 0.08); // this needs fixing too

            var skin = Palette.GetSkinInfo(selectedSkinIndex);
            var text = skin.Name;

            const double TextScaleOffset = 1.8;

            if(skinLabel == null)
            {
                skinLabelSB = new StringBuilder(64).Append(text);

                skinLabelShadow = new HudAPIv2.HUDMessage(skinLabelSB, labelPos, Scale: TextScaleOffset, HideHud: true, Blend: BlendTypeEnum.PostPP);
                skinLabel = new HudAPIv2.HUDMessage(skinLabelSB, labelPos, Scale: TextScaleOffset, HideHud: true, Blend: BlendTypeEnum.PostPP);

                skinLabelShadow.InitialColor = Color.Black;
                skinLabel.InitialColor = Color.White;

                skinLabelUpdate = true;
            }

            if(skinLabelUpdate)
            {
                skinLabelUpdate = false;

                skinLabelSB.Clear().Append(text);

                skinLabel.Origin = labelPos;
                skinLabelShadow.Origin = labelPos;

                skinLabel.Scale = scale * TextScaleOffset;
                skinLabelShadow.Scale = scale * TextScaleOffset;

                var textLen = skinLabel.GetTextLength();
                skinLabel.Offset = new Vector2D(textLen.X * -0.5, 0); // centered
                skinLabelShadow.Offset = skinLabel.Offset + new Vector2D(0.0015, -0.0015); // centered + small offset
            }

            if(!skinLabelVisible)
            {
                skinLabelVisible = true;
                skinLabel.Visible = true;
                skinLabelShadow.Visible = true;
            }

            drawSkinLabel = true;
        }
    }
}