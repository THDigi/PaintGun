using System;
using System.Collections.Generic;
using System.Text;
using Digi.ComponentLib;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Systems;
using Digi.PaintGun.Utilities;
using Draygo.API;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace Digi.PaintGun.Features.Tool
{
    public class SelectionGUI : ModComponent
    {
        public string SymmetryStatusText;

        int mirroredValid;
        int mirroredValidTotal;

        string[] blockInfoStatus = new string[3];

        bool guiVisible = true;
        Vector2D uiPosition => Settings.aimInfoScreenPos;
        Vector2D uiTextBgPosition = new Vector2D(0, -0.071);
        Vector2D uiProgressBarPosition = new Vector2D(0.005, -0.079);

        const float UI_BOX_WIDTH = 0.337f * (16f / 9f);

        const float UI_TITLE_SCALE = 1f;
        const float UI_TITLE_BG_HEIGHT = 0.071f;
        readonly Color UI_TITLE_BG_COLOR = new Vector4(53f / 255f, 4f / 15f, 76f / 255f, 0.9f); // from MyGuiScreenHudSpace.RecreateControls() @ MyGuiControlBlockInfo

        const float UI_TEXT_SCALE = 0.8f;
        const float UI_TEXT_BG_HEIGHT = 0.4f;
        readonly Color UI_TEXT_BG_COLOR = new Vector4(13f / 85f, 52f / 255f, 59f / 255f, 0.9f);

        const BlendTypeEnum UI_FG_BLENDTYPE = BlendTypeEnum.PostPP;
        const BlendTypeEnum UI_BG_BLENDTYPE = BlendTypeEnum.PostPP;

        const float UI_COLORBOX_WIDTH = 0.07f;
        const float UI_COLORBOX_HEIGHT = 0.07f;

        readonly Color UI_PROGRESSBAR_COLOR = new Vector4(0.478431374f, 0.549019635f, 0.6039216f, 1f);
        readonly Color UI_PROGRESSBAR_BG_COLOR = new Vector4(0.266666681f, 0.3019608f, 0.3372549f, 0.9f);
        const float UI_PROGRESSBAR_WIDTH = 0.02f * (16f / 9f);
        const float UI_PROGRESSBAR_HEIGHT = 0.384f;

        HudAPIv2.HUDMessage uiTitle;
        HudAPIv2.BillBoardHUDMessage uiTitleBg;
        HudAPIv2.HUDMessage uiText;
        HudAPIv2.BillBoardHUDMessage uiTextBg;
        HudAPIv2.BillBoardHUDMessage uiTargetColor;
        HudAPIv2.BillBoardHUDMessage uiPaintColor;
        HudAPIv2.BillBoardHUDMessage uiProgressBar;
        HudAPIv2.BillBoardHUDMessage uiProgressBarBg;
        readonly HudAPIv2.MessageBase[] ui = new HudAPIv2.MessageBase[8];

        const int GUI_UPDATE_TICKS = LocalToolHandler.PAINT_UPDATE_TICKS;
        const int GUI_TITLE_MAX_CHARS = 32;

        readonly MyStringId MATERIAL_ICON_GENERIC_BLOCK = MyStringId.GetOrCompute("PaintGunIcon_GenericBlock");
        readonly MyStringId MATERIAL_ICON_GENERIC_CHARACTER = MyStringId.GetOrCompute("PaintGunIcon_GenericCharacter");
        readonly MyStringId MATERIAL_ICON_PAINT_AMMO = MyStringId.GetOrCompute("PaintGunBlockIcon_PaintAmmo");

        readonly MyStringId SYMMETRY_PLANES_MATERIAL = MyStringId.GetOrCompute("Square");
        const float SYMMETRY_PLANES_ALPHA = 0.4f;
        const BlendTypeEnum SYMMETRY_PLANES_BLENDTYPE = BlendTypeEnum.SDR;

        readonly MyStringId BLOCK_SELECTION_LINE_MATERIAL = MyStringId.GetOrCompute("Square");
        const BlendTypeEnum BLOCK_SELECTION_LINE_BLENDTYPE = BlendTypeEnum.SDR;

        readonly MyStringId CHARACTER_SELECTION_LINE_MATERIAL = MyStringId.GetOrCompute("Square");
        const BlendTypeEnum CHARACTER_SELECTION_LINE_BLENDTYPE = BlendTypeEnum.SDR;

        public SelectionGUI(PaintGunMod main) : base(main)
        {
            UpdateMethods = UpdateFlags.UPDATE_DRAW;
        }

        protected override void RegisterComponent()
        {
            Settings.SettingsLoaded += UpdateUISettings;
            GameConfig.ClosedOptionsMenu += UpdateUISettings;
            LocalToolHandler.LocalToolHolstered += LocalToolHolstered;
        }

        protected override void UnregisterComponent()
        {
            Settings.SettingsLoaded -= UpdateUISettings;
            GameConfig.ClosedOptionsMenu -= UpdateUISettings;
            LocalToolHandler.LocalToolHolstered -= LocalToolHolstered;
        }

        public void SetGUIStatus(int line, string text, string colorNameOrRGB = null)
        {
            if(line < 0 || line >= blockInfoStatus.Length)
                throw new ArgumentException($"Given line={line} is negative or above {blockInfoStatus.Length - 1}");

            blockInfoStatus[line] = (colorNameOrRGB != null ? $"<color={colorNameOrRGB}>{text}" : text);
        }

        public void UpdateSymmetryStatus(IMySlimBlock block)
        {
            if(block == null)
                return;

            SymmetryStatusText = null;

            if(!Palette.ReplaceMode && Main.SymmetryAccess)
            {
                var grid = block.CubeGrid;

                if(grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue)
                {
                    bool inputReadable = (InputHandler.IsInputReadable() && !MyAPIGateway.Session.IsCameraUserControlledSpectator);
                    var assigned = InputHandler.GetFriendlyStringForControl(MyAPIGateway.Input.GetGameControl(MyControlsSpace.USE_SYMMETRY));

                    if(inputReadable)
                    {
                        LocalToolHandler.SymmetryInputAvailable = true;

                        if(MyAPIGateway.CubeBuilder.UseSymmetry)
                            SymmetryStatusText = (TextAPIEnabled ? $"[{assigned}] <color=yellow>Symmetry: ON" : $"([{assigned}]) Symmetry: ON");
                        else
                            SymmetryStatusText = (TextAPIEnabled ? $"[{assigned}] Symmetry: OFF" : $"([{assigned}]) Symmetry: OFF");
                    }
                    else
                    {
                        if(MyAPIGateway.CubeBuilder.UseSymmetry)
                            SymmetryStatusText = (TextAPIEnabled ? "<color=yellow>Symmetry: ON" : "Symmetry: ON");
                        else
                            SymmetryStatusText = (TextAPIEnabled ? "Symmetry: OFF" : "Symmetry: OFF");
                    }
                }
                else
                {
                    SymmetryStatusText = (TextAPIEnabled ? "<color=gray>Symetry: not set-up\nUse block placer to do so." : "Symetry: not set-up, use block placer to do so.");
                }
            }
        }

        protected override void UpdateDraw()
        {
            if(Palette.LocalInfo == null || !CheckPlayerField.Ready)
                return;

            if(LocalToolHandler.LocalTool == null || MyAPIGateway.Session.ControlledObject != MyAPIGateway.Session.Player?.Character)
                return;

            if(LocalToolHandler.AimedPlayer != null)
                DrawCharacterSelection();

            DrawSymmetry();
            DrawBlockSelection();

            if(Main.Tick % GUI_UPDATE_TICKS == 0)
                UpdateGUI();

            //bool visible = false;

            //if(LocalToolHandler.LocalTool != null && !MyAPIGateway.Gui.IsCursorVisible && !(Settings.hidePaletteWithHUD && GameConfig.HudState == HudState.OFF))
            //{
            //    bool targetCharacter = (Palette.ColorPickMode && LocalToolHandler.AimedPlayer != null);
            //    visible = (!MyAPIGateway.Gui.IsCursorVisible && Palette.LocalInfo != null && (targetCharacter || LocalToolHandler.AimedBlock != null));
            //}

            //SetUIVisibility(visible);
        }

        #region Selection draw
        void DrawCharacterSelection()
        {
            var aimedCharacter = LocalToolHandler.AimedPlayer.Character;

            if(aimedCharacter == null || aimedCharacter.MarkedForClose || aimedCharacter.Closed || !aimedCharacter.Visible)
            {
                LocalToolHandler.AimedPlayer = null;
                return;
            }

            DrawCharacterSelection(aimedCharacter);
        }

        void DrawCharacterSelection(IMyCharacter character)
        {
            var color = Color.Lime;
            var material = CHARACTER_SELECTION_LINE_MATERIAL;

            var matrix = character.WorldMatrix;
            var localBox = (BoundingBoxD)character.LocalAABB;
            var worldToLocal = character.WorldMatrixInvScaled;

            MySimpleObjectDraw.DrawAttachedTransparentBox(ref matrix, ref localBox, ref color, character.Render.GetRenderObjectID(), ref worldToLocal, MySimpleObjectRasterizer.Wireframe, Vector3I.One, 0.005f, null, CHARACTER_SELECTION_LINE_MATERIAL, false, blendType: CHARACTER_SELECTION_LINE_BLENDTYPE);
        }

        void DrawSymmetry()
        {
            if(!LocalToolHandler.SymmetryInputAvailable || !MyAPIGateway.CubeBuilder.UseSymmetry || LocalToolHandler.AimedBlock == null)
                return;

            var selectedGrid = LocalToolHandler.AimedBlock.CubeGrid;

            if(!selectedGrid.XSymmetryPlane.HasValue && !selectedGrid.YSymmetryPlane.HasValue && !selectedGrid.ZSymmetryPlane.HasValue)
                return;

            var matrix = selectedGrid.WorldMatrix;
            var quad = new MyQuadD();
            var gridSizeHalf = selectedGrid.GridSize * 0.5f;
            Vector3D gridSize = (Vector3I.One + (selectedGrid.Max - selectedGrid.Min)) * gridSizeHalf;

            if(selectedGrid.XSymmetryPlane.HasValue)
            {
                var center = matrix.Translation + matrix.Right * ((selectedGrid.XSymmetryPlane.Value.X * selectedGrid.GridSize) - (selectedGrid.XSymmetryOdd ? gridSizeHalf : 0));

                var minY = matrix.Up * ((selectedGrid.Min.Y - 1.5f) * selectedGrid.GridSize);
                var maxY = matrix.Up * ((selectedGrid.Max.Y + 1.5f) * selectedGrid.GridSize);
                var minZ = matrix.Backward * ((selectedGrid.Min.Z - 1.5f) * selectedGrid.GridSize);
                var maxZ = matrix.Backward * ((selectedGrid.Max.Z + 1.5f) * selectedGrid.GridSize);

                quad.Point0 = center + maxY + maxZ;
                quad.Point1 = center + maxY + minZ;
                quad.Point2 = center + minY + minZ;
                quad.Point3 = center + minY + maxZ;

                MyTransparentGeometry.AddQuad(SYMMETRY_PLANES_MATERIAL, ref quad, Color.Red * SYMMETRY_PLANES_ALPHA, ref center, blendType: SYMMETRY_PLANES_BLENDTYPE);
            }

            if(selectedGrid.YSymmetryPlane.HasValue)
            {
                var center = matrix.Translation + matrix.Up * ((selectedGrid.YSymmetryPlane.Value.Y * selectedGrid.GridSize) - (selectedGrid.YSymmetryOdd ? gridSizeHalf : 0));

                var minZ = matrix.Backward * ((selectedGrid.Min.Z - 1.5f) * selectedGrid.GridSize);
                var maxZ = matrix.Backward * ((selectedGrid.Max.Z + 1.5f) * selectedGrid.GridSize);
                var minX = matrix.Right * ((selectedGrid.Min.X - 1.5f) * selectedGrid.GridSize);
                var maxX = matrix.Right * ((selectedGrid.Max.X + 1.5f) * selectedGrid.GridSize);

                quad.Point0 = center + maxZ + maxX;
                quad.Point1 = center + maxZ + minX;
                quad.Point2 = center + minZ + minX;
                quad.Point3 = center + minZ + maxX;

                MyTransparentGeometry.AddQuad(SYMMETRY_PLANES_MATERIAL, ref quad, Color.Green * SYMMETRY_PLANES_ALPHA, ref center, blendType: SYMMETRY_PLANES_BLENDTYPE);
            }

            if(selectedGrid.ZSymmetryPlane.HasValue)
            {
                var center = matrix.Translation + matrix.Backward * ((selectedGrid.ZSymmetryPlane.Value.Z * selectedGrid.GridSize) + (selectedGrid.ZSymmetryOdd ? gridSizeHalf : 0));

                var minY = matrix.Up * ((selectedGrid.Min.Y - 1.5f) * selectedGrid.GridSize);
                var maxY = matrix.Up * ((selectedGrid.Max.Y + 1.5f) * selectedGrid.GridSize);
                var minX = matrix.Right * ((selectedGrid.Min.X - 1.5f) * selectedGrid.GridSize);
                var maxX = matrix.Right * ((selectedGrid.Max.X + 1.5f) * selectedGrid.GridSize);

                quad.Point0 = center + maxY + maxX;
                quad.Point1 = center + maxY + minX;
                quad.Point2 = center + minY + minX;
                quad.Point3 = center + minY + maxX;

                MyTransparentGeometry.AddQuad(SYMMETRY_PLANES_MATERIAL, ref quad, Color.Blue * SYMMETRY_PLANES_ALPHA, ref center, blendType: SYMMETRY_PLANES_BLENDTYPE);
            }
        }

        void DrawBlockSelection()
        {
            var block = LocalToolHandler.AimedBlock;

            if(block == null)
                return;

            if(block.IsDestroyed || block.IsFullyDismounted)
            {
                LocalToolHandler.AimedBlock = null;
                return;
            }

            DrawBlockSelection(block, LocalToolHandler.AimedState);

            var grid = LocalToolHandler.AimedBlock.CubeGrid;

            // symmetry highlight
            if(Main.SymmetryAccess && !Palette.ReplaceMode && !Palette.ColorPickMode && MyCubeBuilder.Static.UseSymmetry && (grid.XSymmetryPlane.HasValue || grid.YSymmetryPlane.HasValue || grid.ZSymmetryPlane.HasValue))
            {
                var alreadyMirrored = Caches.AlreadyMirrored;
                alreadyMirrored.Clear();

                mirroredValid = (LocalToolHandler.AimedState == SelectionState.Valid ? 1 : 0);
                mirroredValidTotal = 1;

                var mirrorX = MirrorHighlight(grid, 0, block.Position, alreadyMirrored); // X
                var mirrorY = MirrorHighlight(grid, 1, block.Position, alreadyMirrored); // Y
                var mirrorZ = MirrorHighlight(grid, 2, block.Position, alreadyMirrored); // Z
                Vector3I? mirrorYZ = null;

                if(mirrorX.HasValue && grid.YSymmetryPlane.HasValue) // XY
                    MirrorHighlight(grid, 1, mirrorX.Value, alreadyMirrored);

                if(mirrorX.HasValue && grid.ZSymmetryPlane.HasValue) // XZ
                    MirrorHighlight(grid, 2, mirrorX.Value, alreadyMirrored);

                if(mirrorY.HasValue && grid.ZSymmetryPlane.HasValue) // YZ
                    mirrorYZ = MirrorHighlight(grid, 2, mirrorY.Value, alreadyMirrored);

                if(grid.XSymmetryPlane.HasValue && mirrorYZ.HasValue) // XYZ
                    MirrorHighlight(grid, 0, mirrorYZ.Value, alreadyMirrored);

                Notifications.Show(3, $"Mirror paint will affect {mirroredValid} of {mirroredValidTotal} blocks.", MyFontEnum.White, 32);
            }
        }

        void DrawBlockSelection(IMySlimBlock block, SelectionState state)
        {
            var grid = block.CubeGrid;
            var def = (MyCubeBlockDefinition)block.BlockDefinition;

            var color = (state == SelectionState.InvalidButMirrorValid ? Color.Yellow : (state == SelectionState.Valid ? Color.Green : Color.Red));
            var lineWidth = (grid.GridSizeEnum == MyCubeSize.Large ? 0.01f : 0.008f);

            MatrixD worldMatrix;
            BoundingBoxD localBB;

            if(block.FatBlock != null)
            {
                var inflate = new Vector3(0.05f) / grid.GridSize;
                worldMatrix = block.FatBlock.WorldMatrix;
                localBB = new BoundingBoxD(block.FatBlock.LocalAABB.Min - inflate, block.FatBlock.LocalAABB.Max + inflate);
            }
            else
            {
                Matrix localMatrix;
                block.Orientation.GetMatrix(out localMatrix);
                worldMatrix = localMatrix * Matrix.CreateTranslation(block.Position) * Matrix.CreateScale(grid.GridSize) * grid.WorldMatrix;

                var inflate = new Vector3(0.05f);
                var offset = new Vector3(0.5f);
                localBB = new BoundingBoxD(-def.Center - offset - inflate, def.Size - def.Center - offset + inflate);
            }

            // TODO: draw lines of consistent width between fatblock == null and fatblock != null

            MySimpleObjectDraw.DrawTransparentBox(ref worldMatrix, ref localBB, ref color, MySimpleObjectRasterizer.Wireframe, 1, lineWidth, null, BLOCK_SELECTION_LINE_MATERIAL, blendType: BLOCK_SELECTION_LINE_BLENDTYPE);
        }

        Vector3I? MirrorHighlight(IMyCubeGrid grid, int axis, Vector3I originalPosition, List<Vector3I> alreadyMirrored)
        {
            Vector3I? mirrorPosition = null;

            switch(axis)
            {
                case 0:
                    if(grid.XSymmetryPlane.HasValue)
                        mirrorPosition = originalPosition + new Vector3I(((grid.XSymmetryPlane.Value.X - originalPosition.X) * 2) - (grid.XSymmetryOdd ? 1 : 0), 0, 0);
                    break;
                case 1:
                    if(grid.YSymmetryPlane.HasValue)
                        mirrorPosition = originalPosition + new Vector3I(0, ((grid.YSymmetryPlane.Value.Y - originalPosition.Y) * 2) - (grid.YSymmetryOdd ? 1 : 0), 0);
                    break;
                case 2:
                    if(grid.ZSymmetryPlane.HasValue)
                        mirrorPosition = originalPosition + new Vector3I(0, 0, ((grid.ZSymmetryPlane.Value.Z - originalPosition.Z) * 2) + (grid.ZSymmetryOdd ? 1 : 0)); // reversed on odd
                    break;
            }

            if(mirrorPosition.HasValue && mirrorPosition.Value != originalPosition && !alreadyMirrored.Contains(mirrorPosition.Value))
            {
                alreadyMirrored.Add(mirrorPosition.Value);

                var block = grid.GetCubeBlock(mirrorPosition.Value);

                if(block != null)
                {
                    mirroredValidTotal++;

                    var paintMaterial = Palette.GetLocalPaintMaterial();
                    bool validSelection = LocalToolHandler.IsMirrorBlockValid(block, paintMaterial);

                    if(validSelection)
                        mirroredValid++;

                    DrawBlockSelection(block, (validSelection ? SelectionState.Valid : SelectionState.Invalid));
                }
            }

            return mirrorPosition; // this must be returned regardless if block exists or not
        }
        #endregion Selection draw

        #region Aimed info GUI
        void UpdateGUI()
        {
            if(!TextAPIEnabled || !CheckPlayerField.Ready)
                return;

            if(uiTitle == null)
            {
                const int MAX_EXPECTED_CHARACTERS_TITLE = 64;
                const int MAX_EXPECTED_CHARACTERS_TEXT = 512;

                // NOTE: this creation order is needed to have background elements stay in background when everything uses PostPP (or SDR) at once.

                int i = 0;
                ui[i++] = uiTextBg = new HudAPIv2.BillBoardHUDMessage(PaletteHUD.MATERIAL_PALETTE_BACKGROUND, uiPosition, UI_TEXT_BG_COLOR, Width: UI_BOX_WIDTH, Height: UI_TEXT_BG_HEIGHT, Blend: UI_BG_BLENDTYPE);
                ui[i++] = uiTitleBg = new HudAPIv2.BillBoardHUDMessage(PaletteHUD.MATERIAL_PALETTE_BACKGROUND, uiPosition, UI_TITLE_BG_COLOR, Width: UI_BOX_WIDTH, Height: UI_TITLE_BG_HEIGHT, Blend: UI_BG_BLENDTYPE);

                ui[i++] = uiTitle = new HudAPIv2.HUDMessage(new StringBuilder(MAX_EXPECTED_CHARACTERS_TITLE), uiPosition, Scale: UI_TITLE_SCALE, Blend: UI_FG_BLENDTYPE);
                ui[i++] = uiText = new HudAPIv2.HUDMessage(new StringBuilder(MAX_EXPECTED_CHARACTERS_TEXT), uiPosition, Scale: UI_TEXT_SCALE, Blend: UI_FG_BLENDTYPE);

                ui[i++] = uiTargetColor = new HudAPIv2.BillBoardHUDMessage(MATERIAL_ICON_GENERIC_BLOCK, uiPosition, Color.White, Width: UI_COLORBOX_WIDTH, Height: UI_COLORBOX_HEIGHT, Blend: UI_FG_BLENDTYPE);
                ui[i++] = uiPaintColor = new HudAPIv2.BillBoardHUDMessage(MATERIAL_ICON_PAINT_AMMO, uiPosition, Color.White, Width: UI_COLORBOX_WIDTH, Height: UI_COLORBOX_HEIGHT, Blend: UI_FG_BLENDTYPE);

                ui[i++] = uiProgressBarBg = new HudAPIv2.BillBoardHUDMessage(PaletteHUD.MATERIAL_PALETTE_BACKGROUND, uiPosition, UI_PROGRESSBAR_BG_COLOR, Width: UI_PROGRESSBAR_WIDTH, Height: UI_PROGRESSBAR_HEIGHT, Blend: UI_BG_BLENDTYPE);
                ui[i++] = uiProgressBar = new HudAPIv2.BillBoardHUDMessage(PaletteHUD.MATERIAL_PALETTE_BACKGROUND, uiPosition, UI_PROGRESSBAR_COLOR, Width: UI_PROGRESSBAR_WIDTH, Height: UI_PROGRESSBAR_HEIGHT, Blend: UI_FG_BLENDTYPE);

                UpdateUISettings();
            }

            bool targetCharacter = (Palette.ColorPickMode && LocalToolHandler.AimedPlayer != null);
            bool visible = (!MyAPIGateway.Gui.IsCursorVisible && Palette.LocalInfo != null && (targetCharacter || LocalToolHandler.AimedBlock != null));

            SetGUIVisible(visible);

            if(!visible)
                return;

            PaintMaterial targetMaterial;
            var paint = Palette.GetLocalPaintMaterial();
            int ammo = (LocalToolHandler.LocalTool != null ? LocalToolHandler.LocalTool.Ammo : 0);
            var title = uiTitle.Message.Clear().Append("<color=220,244,252>");
            float progress = 0f;

            if(targetCharacter)
            {
                uiTargetColor.Material = MATERIAL_ICON_GENERIC_CHARACTER;

                targetMaterial = LocalToolHandler.AimedPlayersPaint;

                uiTargetColor.BillBoardColor = (targetMaterial.ColorMask.HasValue ? Utils.ColorMaskToRGB(targetMaterial.ColorMask.Value) : Color.Gray);

                title.AppendLimitedChars(LocalToolHandler.AimedPlayer.DisplayName, GUI_TITLE_MAX_CHARS);
            }
            else
            {
                uiTargetColor.Material = MATERIAL_ICON_GENERIC_BLOCK;

                var block = LocalToolHandler.AimedBlock;
                targetMaterial = new PaintMaterial(block.ColorMaskHSV, block.SkinSubtypeId);

                uiTargetColor.BillBoardColor = Utils.ColorMaskToRGB(targetMaterial.ColorMask.Value);

                if(paint.ColorMask.HasValue)
                    progress = Utils.ColorScalar(targetMaterial.ColorMask.Value, paint.ColorMask.Value);
                else if(paint.Skin.HasValue)
                    progress = (targetMaterial.Skin == paint.Skin.Value ? 1f : 0.25f);

                var selectedDef = (MyCubeBlockDefinition)block.BlockDefinition;

                title.AppendLimitedChars(selectedDef.DisplayNameText, GUI_TITLE_MAX_CHARS);
            }

            uiPaintColor.BillBoardColor = (paint.ColorMask.HasValue ? Utils.ColorMaskToRGB(paint.ColorMask.Value) : Color.Gray);

            var height = UI_PROGRESSBAR_HEIGHT * progress;
            uiProgressBar.Height = height;
            uiProgressBar.Offset = new Vector2D(uiProgressBar.Width * 0.5, -UI_PROGRESSBAR_HEIGHT + uiProgressBar.Height * 0.5) + uiProgressBarPosition;

            var text = uiText.Message;
            text.Clear().Append(blockInfoStatus[0]);
            text.Append('\n');
            text.Append('\n');

            {
                text.Append("<color=220,244,252>");

                if(Palette.ColorPickMode && LocalToolHandler.AimedPlayer != null)
                    text.Append("Engineer's selected paint:");
                else
                    text.Append("Block's material:");

                text.Append('\n').Append("        HSV: ");

                if(targetMaterial.ColorMask.HasValue)
                {
                    if(Palette.IsColorMaskInPalette(targetMaterial.ColorMask.Value))
                        text.Append("<color=55,255,55>");
                    else
                        text.Append("<color=255,200,25>");
                    text.Append(Utils.ColorMaskToString(targetMaterial.ColorMask.Value));
                }
                else
                    text.Append("(N/A)");

                text.Append('\n');

                text.Append("<color=white>        Skin: ");
                if(targetMaterial.Skin.HasValue)
                {
                    var targetSkin = Palette.GetSkinInfo(targetMaterial.Skin.Value);

                    if(targetSkin != null)
                    {
                        if(!targetSkin.LocallyOwned)
                            text.Append("<color=red>");
                        else if(targetSkin.Index == 0)
                            text.Append("<color=gray>");
                        text.Append(targetSkin.Name);
                    }
                    else
                    {
                        text.Append("<color=gray>").Append(targetMaterial.Skin.Value.ToString()).Append(" <color=red>(uninstalled)");
                    }
                }
                else
                {
                    text.Append("(N/A)");
                }
                text.Append('\n');
            }

            text.Append('\n');

            {
                text.Append("<color=220,244,252>");
                if(Palette.ColorPickMode)
                {
                    text.Append("Replace slot: ").Append(Palette.LocalInfo.SelectedColorIndex + 1);
                }
                else
                {
                    text.Append("Paint: ");

                    if(Main.IgnoreAmmoConsumption)
                        text.Append("Inf.");
                    else
                        text.Append(ammo);
                }
                text.Append('\n');
            }

            {
                text.Append("        ");
                if(paint.ColorMask.HasValue)
                    text.Append("<color=white>HSV: ").Append(Utils.ColorMaskToString(paint.ColorMask.Value)).Append('\n');
                else
                    text.Append('\n');
            }

            {
                if(paint.Skin.HasValue)
                {
                    text.Append("        <color=white>Skin: ");

                    var skin = Palette.GetSkinInfo(paint.Skin.Value);
                    if(skin != null)
                    {
                        if(skin.Index == 0)
                            text.Append("<color=gray>");
                        text.Append(skin.Name);
                    }
                    else
                    {
                        text.Append("<color=gray>").Append(paint.Skin.Value.ToString()).Append(" <color=red>(uninstalled)");
                    }
                }
                text.Append('\n');
            }

            text.Append("<color=white>");

            if(blockInfoStatus[1] != null)
                text.Append('\n').Append(blockInfoStatus[1]);

            if(blockInfoStatus[2] != null)
                text.Append('\n').Append(blockInfoStatus[2]);
        }

        void UpdateUISettings()
        {
            if(uiTitle == null)
                return;

            SetUIOption(HudAPIv2.Options.HideHud, Settings.hidePaletteWithHUD);

            var alpha = (Settings.aimInfoBackgroundOpacity < 0 ? GameConfig.HudBackgroundOpacity : Settings.aimInfoBackgroundOpacity);

            uiTitleBg.BillBoardColor = UI_TITLE_BG_COLOR * alpha;
            uiTextBg.BillBoardColor = UI_TEXT_BG_COLOR * alpha;
            uiProgressBarBg.BillBoardColor = UI_PROGRESSBAR_BG_COLOR * MathHelper.Clamp(alpha * 2, 0.1f, 0.9f);

            float aspectRatioMod = (float)(1d / GameConfig.AspectRatio);
            float boxBgWidth = UI_BOX_WIDTH * aspectRatioMod;
            float colorWidth = UI_COLORBOX_WIDTH * aspectRatioMod;
            float progressBarWidth = UI_PROGRESSBAR_WIDTH * aspectRatioMod;

            for(int i = 0; i < ui.Length; ++i)
            {
                var msgBase = ui[i];

                if(msgBase is HudAPIv2.HUDMessage)
                {
                    var obj = (HudAPIv2.HUDMessage)msgBase;

                    obj.Origin = uiPosition;
                }
                else if(msgBase is HudAPIv2.BillBoardHUDMessage)
                {
                    var obj = (HudAPIv2.BillBoardHUDMessage)msgBase;

                    obj.Origin = uiPosition;
                }
            }

            //float scale = settings.aimInfoScale;
            const float scale = 1f; // TODO: make it scaleable?

            uiTitleBg.Width = boxBgWidth * scale;
            uiTextBg.Width = boxBgWidth * scale;
            uiPaintColor.Width = colorWidth * scale;
            uiTargetColor.Width = colorWidth * scale;
            uiProgressBar.Width = progressBarWidth * scale;
            uiProgressBarBg.Width = progressBarWidth * scale;

            uiTitle.Offset = new Vector2D(0.012, -0.018) * scale;
            uiTitleBg.Offset = new Vector2D(uiTitleBg.Width * 0.5, uiTitleBg.Height * -0.5);

            uiText.Offset = new Vector2D(0.07 * aspectRatioMod, -0.09) * scale;
            uiTextBg.Offset = new Vector2D(uiTextBg.Width * 0.5, uiTextBg.Height * -0.5) + (uiTextBgPosition * scale);

            uiTargetColor.Offset = new Vector2D(0.09 * aspectRatioMod, -0.195) * scale;
            uiPaintColor.Offset = new Vector2D(0.09 * aspectRatioMod, -0.295) * scale;

            uiProgressBar.Offset = new Vector2D(uiProgressBar.Width * 0.5, uiProgressBar.Height * -0.5) + (uiProgressBarPosition * scale);
            uiProgressBarBg.Offset = new Vector2D(uiProgressBarBg.Width * 0.5, uiProgressBarBg.Height * -0.5) + (uiProgressBarPosition * scale);

            uiTitle.Scale = UI_TITLE_SCALE * scale;
            uiTitleBg.Scale = scale;
            uiText.Scale = UI_TEXT_SCALE * scale;
            uiTextBg.Scale = scale;
            uiTargetColor.Scale = scale;
            uiPaintColor.Scale = scale;
            uiProgressBar.Scale = scale;
            uiProgressBarBg.Scale = scale;
        }

        void LocalToolHolstered(PaintGunItem item)
        {
            SetGUIVisible(false);
        }

        void SetGUIVisible(bool set)
        {
            if(uiTitle == null)
                return;

            if(set == guiVisible)
                return;

            guiVisible = set;

            for(int i = 0; i < ui.Length; ++i)
            {
                ui[i].Visible = set;
            }
        }

        void SetUIOption(HudAPIv2.Options flag, bool set)
        {
            if(uiTitle == null)
                return;

            for(int i = 0; i < ui.Length; ++i)
            {
                var msgBase = ui[i];

                // just in case this issue comes back I wanna know if it's it specifically
                if(msgBase.BackingObject == null)
                {
                    Log.Error($"{msgBase.GetType().Name} (ui{i}) has BackingObject = null!!!");
                    continue;
                }

                var hudMessage = msgBase as HudAPIv2.HUDMessage;
                if(hudMessage != null)
                {
                    if(set)
                        hudMessage.Options |= flag;
                    else
                        hudMessage.Options &= ~flag;
                    continue;
                }

                var hudBillboard = msgBase as HudAPIv2.BillBoardHUDMessage;
                if(hudBillboard != null)
                {
                    if(set)
                        hudBillboard.Options |= flag;
                    else
                        hudBillboard.Options &= ~flag;
                    continue;
                }
            }
        }
        #endregion Aimed info GUI
    }
}