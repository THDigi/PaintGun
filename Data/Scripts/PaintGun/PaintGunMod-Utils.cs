using System;
using System.Collections.Generic;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun
{
    public partial class PaintGunMod
    {
        static byte ColorPercent(Vector3 blockColor, Vector3 paintColor)
        {
            float percentScale = (Math.Abs(paintColor.X - blockColor.X) + Math.Abs(paintColor.Y - blockColor.Y) + Math.Abs(paintColor.Z - blockColor.Z)) / 3f;
            return (byte)MathHelper.Clamp((1 - percentScale) * 99, 0, 99);
        }

        static float ColorPercentFull(Vector3 blockColor, Vector3 paintColor)
        {
            var blockHSV = ColorMaskToHSV(blockColor);
            var paintHSV = ColorMaskToHSV(paintColor);
            var defaultHSV = ColorMaskToHSV(instance.DEFAULT_COLOR);

            float addPaint = 1 - ((Math.Abs(paintHSV.X - blockHSV.X) + Math.Abs(paintHSV.Y - blockHSV.Y) + Math.Abs(paintHSV.Z - blockHSV.Z)) / 3f);
            float removePaint = 1 - ((Math.Abs(defaultHSV.Y - blockHSV.Y) + Math.Abs(defaultHSV.Z - blockHSV.Z)) * 0.5f);
            float def2paint = 1 - ((Math.Abs(paintHSV.Y - defaultHSV.Y) + Math.Abs(paintHSV.Z - defaultHSV.Z)) * 0.5f);

            bool needsToAddPaint = Math.Abs(paintColor.X - blockColor.X) < 0.0001f;

            // DEBUG TODO improve the progress bar
            //MyAPIGateway.Utilities.ShowNotification($"[DEBUG: needsToAddPaint={needsToAddPaint}; addPaint={addPaint:0.00}; removePaint={removePaint:0.00}; def2paint={def2paint:0.00}]", 16);

            var progress = addPaint;

            if(needsToAddPaint)
                progress = 0.5f + MathHelper.Clamp(addPaint - 0.5f, 0, 0.5f); // 0.5f + (((addPaint - def2paint) / (1 - def2paint)) * 0.5f);
            else
                progress = removePaint * 0.5f;

            return MathHelper.Clamp(progress, 0, 1);
        }

        static float GetBlockSurface(IMySlimBlock block)
        {
            Vector3 blockSize;
            block.ComputeScaledHalfExtents(out blockSize);
            blockSize *= 2;
            return (blockSize.X * blockSize.Y) + (blockSize.Y * blockSize.Z) + (blockSize.Z * blockSize.X) / 6;
        }

        void SetToolColor(Vector3 colorMask)
        {
            localHeldTool?.SetToolColor(colorMask);
        }

        bool CheckColorList(ulong steamId, List<Vector3> oldList, List<Vector3> newList)
        {
            bool changed = false;

            for(byte i = 0; i < oldList.Count; i++)
            {
                if(!ColorMaskEquals(oldList[i], newList[i]))
                {
                    SendToAllPlayers_UpdateColor(steamId, i, newList[i]);
                    changed = true;
                }
            }

            return changed;
        }

        public IMyPlayer GetPlayerBySteamId(ulong steamId)
        {
            players.Clear();
            MyAPIGateway.Players.GetPlayers(players);
            IMyPlayer player = null;

            foreach(var p in players)
            {
                if(p.SteamUserId == steamId)
                {
                    player = p;
                    break;
                }
            }

            players.Clear();
            return player;
        }

        public string GetPlayerName(ulong steamId)
        {
            IMyPlayer player = GetPlayerBySteamId(steamId);
            return (player?.DisplayName ?? "(not found)");
        }

        public bool IsPlayerOnline(ulong steamId)
        {
            return (GetPlayerBySteamId(steamId) != null);
        }

        public static ulong GetSteamId(string playerIdstring)
        {
            return ulong.Parse(playerIdstring.Substring(0, playerIdstring.IndexOf(':')));
        }

        public void SetGUIToolStatus(int line, string text, string color = null)
        {
            blockInfoStatus[line] = (color != null && GUIUsed ? $"<color={color}>{text}" : text);
        }

        public void ShowNotification(int id, string text, string font = MyFontEnum.White, int aliveTime = TOOLSTATUS_TIMEOUT)
        {
            if(text == null)
            {
                if(toolStatus[id] != null)
                    toolStatus[id].Hide();

                return;
            }

            if(toolStatus[id] == null)
                toolStatus[id] = MyAPIGateway.Utilities.CreateNotification(text, aliveTime, font);

            toolStatus[id].Font = font;
            toolStatus[id].Text = text;
            toolStatus[id].AliveTime = aliveTime;
            toolStatus[id].Show();
        }

        public static string ColorMaskToString(Vector3 colorMask)
        {
            var hsv = ColorMaskToHSVFriendly(colorMask);
            return $"HSV: {hsv.X:0.0}°, {hsv.Y:0.0}%, {hsv.Z:0.0}%";
        }

        #region ColorMask <> HSV <> RGB conversions
        /// <summary>
        /// Float HSV to game's color mask (0-1/-1-1/-1-1).
        /// </summary>
        public static Vector3 HSVToColorMask(Vector3 hsv)
        {
            // HACK copied data from MyGuiScreenColorPicker.OnValueChange()
            return new Vector3(
                    MathHelper.Clamp(hsv.X, 0f, 1f),
                    MathHelper.Clamp(hsv.Y - MyColorPickerConstants.SATURATION_DELTA, -1f, 1f),
                    MathHelper.Clamp(hsv.Z - MyColorPickerConstants.VALUE_DELTA + MyColorPickerConstants.VALUE_COLORIZE_DELTA, -1f, 1f)
                );
        }

        /// <summary>
        /// Game's color mask (0-1/-1-1/-1-1) to float HSV.
        /// </summary>
        public static Vector3 ColorMaskToHSV(Vector3 colorMask)
        {
            // HACK copied and inverted with data from MyGuiScreenColorPicker.OnValueChange()
            return new Vector3(
                    MathHelper.Clamp(colorMask.X, 0f, 1f),
                    MathHelper.Clamp(colorMask.Y + MyColorPickerConstants.SATURATION_DELTA, 0f, 1f),
                    MathHelper.Clamp(colorMask.Z + MyColorPickerConstants.VALUE_DELTA - MyColorPickerConstants.VALUE_COLORIZE_DELTA, 0f, 1f)
                );
        }

        /// <summary>
        /// Game's color mask (0-1/-1-1/-1-1) to HSV (0-360/0-100/0-100) for printing to users.
        /// </summary>
        public static Vector3 ColorMaskToHSVFriendly(Vector3 colorMask)
        {
            var hsv = ColorMaskToHSV(colorMask);
            return new Vector3(Math.Round(hsv.X * 360, 1), Math.Round(hsv.Y * 100, 1), Math.Round(hsv.Z * 100, 1));
        }

        /// <summary>
        /// Game's color mask (0-1/-1-1/-1-1) to byte RGB.
        /// </summary>
        public static Color ColorMaskToRGB(Vector3 colorMask)
        {
            return ColorMaskToHSV(colorMask).HSVtoColor();
        }

        /// <summary>
        /// Byte RGB to game's color mask (0-1/-1-1/-1-1).
        /// </summary>
        public static Vector3 RGBToColorMask(Color rgb)
        {
            return HSVToColorMask(ColorExtensions.ColorToHSV(rgb));
        }

        public static bool ColorMaskEquals(Vector3 colorMask1, Vector3 colorMask2)
        {
            return ColorMaskToRGB(colorMask1) == ColorMaskToRGB(colorMask2);
        }
        #endregion

        public Vector3 GetBuildColorMask()
        {
            return (localColorData != null ? localColorData.Colors[localColorData.SelectedSlot] : DEFAULT_COLOR);
        }

        public void PlaySound(MySoundPair soundPair, float volume)
        {
            if(emitter == null)
                emitter = new MyEntity3DSoundEmitter(null);

            emitter.SetPosition(MyAPIGateway.Session.Camera.WorldMatrix.Translation);
            emitter.CustomVolume = volume;
            emitter.PlaySingleSound(soundPair);
        }

        private bool EnsureColorDataEntry(ulong steamId)
        {
            if(!playerColorData.ContainsKey(steamId))
            {
                var emptyColor = new Vector3(0, -1, -1);
                var colors = new List<Vector3>();

                for(int i = 0; i < COLOR_PALETTE_SIZE; i++)
                {
                    colors.Add(emptyColor);
                }

                playerColorData.Add(steamId, new PlayerColorData(steamId, colors));
                return false;
            }

            return true;
        }

        public static void DrawInflatedSelectionBox(IMySlimBlock block, MyStringId lineMaterial)
        {
            var grid = block.CubeGrid;
            var def = (MyCubeBlockDefinition)block.BlockDefinition;
            var color = Color.White;

            Matrix lm;
            block.Orientation.GetMatrix(out lm);
            var wm = lm * Matrix.CreateTranslation(block.Position) * Matrix.CreateScale(grid.GridSize) * grid.WorldMatrix;
            var lineWidth = (grid.GridSizeEnum == MyCubeSize.Large) ? 0.06f : 0.03f;
            var v1 = new Vector3(0.5f, 0.5f, 0.5f);
            var v2 = new Vector3(0.05f);
            var localBB = new BoundingBoxD(-def.Center - v1 - v2, def.Size - def.Center - v1 + v2);
            MySimpleObjectDraw.DrawTransparentBox(ref wm, ref localBB, ref color, MySimpleObjectRasterizer.Wireframe, 1, lineWidth, null, lineMaterial);
        }

        void UpdateCameraViewProjInvMatrix()
        {
            var cam = MyAPIGateway.Session.Camera;
            viewProjInv = MatrixD.Invert(cam.ViewMatrix * cam.ProjectionMatrix);
        }

        Vector3D HUDtoWorld(Vector2 hud)
        {
            var vec4 = new Vector4D(hud.X, hud.Y, 0d, 1d);
            Vector4D.Transform(ref vec4, ref viewProjInv, out vec4);
            return new Vector3D((vec4.X / vec4.W), (vec4.Y / vec4.W), (vec4.Z / vec4.W));
        }
    }
}