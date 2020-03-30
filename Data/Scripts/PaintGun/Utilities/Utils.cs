using System;
using System.Collections.Generic;
using System.Text;
using Digi.PaintGun.Features.Palette;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.Game.Entities.Character.Components;
using Sandbox.ModAPI;
using SpaceEngineers.Game.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace Digi.PaintGun.Utilities
{
    /// <summary>
    /// Various random utility methods
    /// </summary>
    public static class Utils
    {
        // NOTE Session.EnableCopyPaste used as spacemaster check
        public static bool CreativeToolsEnabled => MyAPIGateway.Session.CreativeMode || (MyAPIGateway.Session.HasCreativeRights && MyAPIGateway.Session.EnableCopyPaste);

        public static bool SafeZoneCanPaint(IMySlimBlock block, ulong playerSteamId)
        {
            var gridSize = block.CubeGrid.GridSize;
            var gridSizeHalf = gridSize * 0.5f;
            var worldAABB = new BoundingBoxD(block.Min * gridSize - gridSizeHalf, block.Max * gridSize + gridSizeHalf).TransformFast(block.CubeGrid.WorldMatrix);

            return MySessionComponentSafeZones.IsActionAllowed(worldAABB, CastHax(MySessionComponentSafeZones.AllowedActions, Constants.SAFE_ZONE_ACCES_FOR_PAINT), 0, playerSteamId);
        }

        public static bool SafeZoneCanPaint(MyEntity entity, ulong playerSteamId)
        {
            return MySessionComponentSafeZones.IsActionAllowed(entity, CastHax(MySessionComponentSafeZones.AllowedActions, Constants.SAFE_ZONE_ACCES_FOR_PAINT), 0, playerSteamId);
        }

        public static bool SafeZoneCanPaint(Vector3D point, ulong playerSteamId)
        {
            return MySessionComponentSafeZones.IsActionAllowed(point, CastHax(MySessionComponentSafeZones.AllowedActions, Constants.SAFE_ZONE_ACCES_FOR_PAINT), 0, playerSteamId);
        }

        /// <summary>
        /// hacky: used for giving a specific type to a method from own casted object
        /// </summary>
        public static T CastHax<T>(T pointerForType, object val) => (T)val;

        public static bool ValidateSkinOwnership(ulong steamId, SerializedPaintMaterial paint)
        {
            // PlayerInfo.OwnedSkinIndexes is only assigned and filled server-side
            if(MyAPIGateway.Session.IsServer && paint.SkinIndex.HasValue && paint.SkinIndex.Value != 0)
            {
                var pi = PaintGunMod.Instance.Palette.GetPlayerInfo(steamId);

                if(pi == null)
                {
                    // DEBUG <<<
                    if(MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE)
                    {
                        Log.Info($"ValidateSkinOwnership() DEBUG: {steamId.ToString()} has no PlayerInfo!");
                    }

                    return false;
                }

                if(pi.OwnedSkinIndexes == null)
                {
                    // DEBUG <<<
                    if(MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE)
                    {
                        Log.Info($"ValidateSkinOwnership() DEBUG: {steamId.ToString()} has no OwnedSkinIndexes list, is it before ownership testing was completed?");
                    }

                    return false;
                }

                if(!pi.OwnedSkinIndexes.Contains(paint.SkinIndex.Value))
                {
                    // DEBUG <<<
                    if(MyAPIGateway.Session.OnlineMode == MyOnlineModeEnum.OFFLINE)
                    {
                        var skin = PaintGunMod.Instance.Palette.GetSkinInfo(paint.SkinIndex.Value);
                        string skinName = (skin != null ? skin.SubtypeId.ToString() : $"UnknownId#{paint.SkinIndex.Value.ToString()}");
                        Log.Info($"ValidateSkinOwnership() DEBUG: {steamId.ToString()} tried to paint with a skin ({skinName}) they don't own... ?");
                        Log.Info($"ValidateSkinOwnership() DEBUG: ownedIds={string.Join(", ", pi.OwnedSkinIndexes)}");
                        Log.Info($"ValidateSkinOwnership() DEBUG: MyId={MyAPIGateway.Multiplayer.MyId.ToString()}; players={MyAPIGateway.Multiplayer.Players.Count.ToString()}; PlayerInfos={PaintGunMod.Instance.Palette.PlayerInfo.Count.ToString()}");
                    }

                    return false;
                }
            }

            return true;
        }

        public static StringBuilder AppendLimitedChars(this StringBuilder sb, string text, int maxChars, bool addDots = true)
        {
            var originalLen = sb.Length;

            sb.Append(text);

            if((sb.Length - originalLen) > maxChars)
            {
                sb.Length = originalLen + maxChars;

                if(addDots)
                    sb.Append('…');
            }

            return sb;
        }

        public static string GetBlockName(IMySlimBlock block)
        {
            return (block.FatBlock == null ? block.ToString() : block.FatBlock.DefinitionDisplayNameText);
        }

        public static string ColorMaskToHSVText(Vector3 colorMask)
        {
            var hsv = ColorMaskToFriendlyHSV(colorMask);
            return $"HSV: {hsv.X.ToString("0.0")}°, {hsv.Y.ToString("0.0")}%, {hsv.Z.ToString("0.0")}%";
        }

        public static int ColorPercent(Vector3 blockColor, Vector3 paintColor)
        {
            float percentScale = (Math.Abs(paintColor.X - blockColor.X) + Math.Abs(paintColor.Y - blockColor.Y) + Math.Abs(paintColor.Z - blockColor.Z)) / 3f;
            return (int)MathHelper.Clamp((1 - percentScale) * 99, 0, 99);
        }

        public static float ColorScalar(Vector3 blockColor, Vector3 paintColor)
        {
            var blockHSV = ColorMaskToHSV(blockColor);
            var paintHSV = ColorMaskToHSV(paintColor);
            var defaultHSV = ColorMaskToHSV(PaintGunMod.Instance.Palette.DefaultColorMask);

            float addPaint = 1 - ((Math.Abs(paintHSV.X - blockHSV.X) + Math.Abs(paintHSV.Y - blockHSV.Y) + Math.Abs(paintHSV.Z - blockHSV.Z)) / 3f);
            float removePaint = 1 - ((Math.Abs(defaultHSV.Y - blockHSV.Y) + Math.Abs(defaultHSV.Z - blockHSV.Z)) * 0.5f);
            float def2paint = 1 - ((Math.Abs(paintHSV.Y - defaultHSV.Y) + Math.Abs(paintHSV.Z - defaultHSV.Z)) * 0.5f);

            bool needsToAddPaint = Math.Abs(paintColor.X - blockColor.X) < 0.0001f;

            var progress = addPaint;

            if(needsToAddPaint)
                progress = 0.5f + MathHelper.Clamp(addPaint - 0.5f, 0, 0.5f); // 0.5f + (((addPaint - def2paint) / (1 - def2paint)) * 0.5f);
            else
                progress = removePaint * 0.5f;

            return MathHelper.Clamp(progress, 0, 1);
        }

        public static string ColorMaskToString(Vector3 colorMask)
        {
            var hsv = ColorMaskToFriendlyHSV(colorMask);
            return $"{hsv.X.ToString("0.0")}°, {hsv.Y.ToString("0.0")}%, {hsv.Z.ToString("0.0")}%";
        }

        #region ColorMask <> HSV <> RGB conversions
        /// <summary>
        /// Float HSV to game's color mask (0-1/-1-1/-1-1).
        /// </summary>
        public static Vector3 HSVToColorMask(Vector3 hsv)
        {
            return MyColorPickerConstants.HSVToHSVOffset(hsv);
        }

        /// <summary>
        /// Game's color mask (0-1/-1-1/-1-1) to float HSV.
        /// </summary>
        public static Vector3 ColorMaskToHSV(Vector3 colorMask)
        {
            return MyColorPickerConstants.HSVOffsetToHSV(colorMask);
        }

        /// <summary>
        /// Game's color mask (0-1/-1-1/-1-1) to HSV (0-360/0-100/0-100) for printing to users.
        /// </summary>
        public static Vector3 ColorMaskToFriendlyHSV(Vector3 colorMask)
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
            return colorMask1.PackHSVToUint() == colorMask2.PackHSVToUint();
        }

        public static Vector3 ColorMaskNormalize(Vector3 colorMask)
        {
            return ColorExtensions.UnpackHSVFromUint(colorMask.PackHSVToUint());
        }
        #endregion ColorMask <> HSV <> RGB conversions

        #region Recursive ship finder
        public static void GetShipSubgrids(MyCubeGrid grid, HashSet<MyCubeGrid> results)
        {
            results.Add(grid);
            GetSubgridsRecursive(grid, results);
        }

        static void GetSubgridsRecursive(MyCubeGrid grid, HashSet<MyCubeGrid> results)
        {
            foreach(var block in grid.GetFatBlocks())
            {
                var g = GetAttachedGrid(block);

                if(g != null && !results.Contains(g))
                {
                    results.Add(g);
                    GetSubgridsRecursive(g, results);
                }
            }
        }

        static MyCubeGrid GetAttachedGrid(MyCubeBlock block)
        {
            var mechanicalBlock = block as IMyMechanicalConnectionBlock; // includes piston, rotor and suspension bases

            if(mechanicalBlock != null)
                return mechanicalBlock.TopGrid as MyCubeGrid;

            var attachableBlock = block as IMyAttachableTopBlock; // includes rotor tops, piston tops and wheels

            if(attachableBlock != null)
                return attachableBlock.Base?.CubeGrid as MyCubeGrid;

            return null;
        }
        #endregion Recursive ship finder

        /// <summary>
        /// Chat message with the sender name being colored.
        /// NOTE: this is synchronized to all players but only the intended player(s) will see it.
        /// <paramref name="identityId"/> set to 0 will show to all players, default (-1) will show to local player.
        /// </summary>
        public static void ShowColoredChatMessage(string from, string message, MyFontEnum font, long identityId = -1)
        {
            if(identityId == -1)
            {
                if(MyAPIGateway.Session?.Player == null)
                    return;

                identityId = MyAPIGateway.Session.Player.IdentityId;
            }

            // NOTE: this is sent to all players and only shown if their identityId matches the one sent.
            MyVisualScriptLogicProvider.SendChatMessage(message, from, identityId, font);
        }

        public static IMyPlayer GetPlayerBySteamId(ulong steamId)
        {
            var players = PaintGunMod.Instance.Caches.Players;
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

        public static IMyPlayer GetPlayerByIdentityId(long identityId)
        {
            var players = PaintGunMod.Instance.Caches.Players;
            players.Clear();
            MyAPIGateway.Players.GetPlayers(players);
            IMyPlayer player = null;

            foreach(var p in players)
            {
                if(p.IdentityId == identityId)
                {
                    player = p;
                    break;
                }
            }

            players.Clear();
            return player;
        }

        #region Paint permission check
        // NOTE copied from Sandbox.Game.Entities.MyCubeGrid because it's private
        public static bool AllowedToPaintGrid(IMyCubeGrid grid, long identityId)
        {
            if(identityId == 0 || grid.BigOwners.Count == 0)
                return true;

            foreach(long owner in grid.BigOwners)
            {
                var relation = GetRelationBetweenPlayers(owner, identityId);

                // vanilla only checks Self/Owner, this mod allows allies to paint aswell
                if(relation == MyRelationsBetweenPlayerAndBlock.FactionShare || relation == MyRelationsBetweenPlayerAndBlock.Owner)
                    return true;
            }

            return false;
        }

        // NOTE copied from Sandbox.Game.World.MyPlayer because it's not exposed
        static MyRelationsBetweenPlayerAndBlock GetRelationBetweenPlayers(long id1, long id2)
        {
            if(id1 == id2)
                return MyRelationsBetweenPlayerAndBlock.Owner;

            IMyFaction f1 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(id1);
            IMyFaction f2 = MyAPIGateway.Session.Factions.TryGetPlayerFaction(id2);

            if(f1 == null || f2 == null)
                return MyRelationsBetweenPlayerAndBlock.Enemies;

            if(f1 == f2)
                return MyRelationsBetweenPlayerAndBlock.FactionShare;

            var factionRelation = MyAPIGateway.Session.Factions.GetRelationBetweenFactions(f1.FactionId, f2.FactionId);

            if(factionRelation == MyRelationsBetweenFactions.Neutral)
                return MyRelationsBetweenPlayerAndBlock.Neutral;

            return MyRelationsBetweenPlayerAndBlock.Enemies;
        }
        #endregion Paint permission check

        public static float GetBlockSurface(IMySlimBlock block)
        {
            Vector3 blockSize;
            block.ComputeScaledHalfExtents(out blockSize);
            blockSize *= 2;
            return (blockSize.X * blockSize.Y) + (blockSize.Y * blockSize.Z) + (blockSize.Z * blockSize.X) / 6;
        }

        public static bool IsAimingDownSights(IMyCharacter character)
        {
            var weaponPosComp = character?.Components?.Get<MyCharacterWeaponPositionComponent>();
            return (weaponPosComp != null ? weaponPosComp.IsInIronSight : false);
        }

        public static BoundingSphereD GetCharacterSelectionSphere(IMyCharacter character)
        {
            var sphere = character.WorldVolume;
            sphere.Center += character.WorldMatrix.Up * 0.2;
            sphere.Radius *= 0.6;

            var crouching = (MyCharacterMovement.GetMode(character.CurrentMovementState) == MyCharacterMovement.Crouching);
            if(crouching)
            {
                sphere.Center += character.WorldMatrix.Up * 0.1;
                sphere.Radius *= 1.2;
            }

            return sphere;
        }

        public static IMyPlayer GetPlayerOrError(object caller, ulong steamId)
        {
            var player = GetPlayerBySteamId(steamId);

            if(player == null)
                Log.Error($"{caller?.GetType().Name} Player ({steamId.ToString()}) not found!", Log.PRINT_MESSAGE);

            return player;
        }

        public static IMyCharacter GetCharacterOrError(object caller, IMyPlayer player)
        {
            if(player == null)
                return null;

            if(player.Character == null)
                Log.Error($"{caller?.GetType().Name} Player {player.DisplayName} ({player.SteamUserId.ToString()}) has no character!", Log.PRINT_MESSAGE);

            return player.Character;
        }

        public static IMyInventory GetCharacterInventoryOrError(object caller, IMyCharacter character)
        {
            if(character == null)
                return null;

            var inv = character.GetInventory();

            if(inv == null)
                Log.Error($"{caller?.GetType().Name} Player {character.DisplayName} has no inventory (entId={character.EntityId.ToString()})", Log.PRINT_MESSAGE);

            return inv;
        }

        public static T GetEntityOrError<T>(object caller, long entityId, bool logError = true) where T : class, IMyEntity
        {
            IMyEntity ent;

            if(entityId == 0 || !MyAPIGateway.Entities.TryGetEntityById(entityId, out ent))
            {
                if(logError)
                    Log.Error($"{caller?.GetType().Name} :: Can't find entity from id={entityId.ToString()}", Log.PRINT_MESSAGE);
                return null;
            }

            var casted = ent as T;

            if(casted == null)
            {
                if(logError)
                    Log.Error($"{caller?.GetType().Name} :: Found entity {ent} (id={entityId.ToString()}) but it's not the expected type: {typeof(T).FullName}", Log.PRINT_MESSAGE);
                return null;
            }

            return casted;
        }

        public static bool IsLocalMod()
        {
            var modContext = PaintGunMod.Instance.Session.ModContext;

            foreach(var mod in MyAPIGateway.Session.Mods)
            {
                if(mod.Name == modContext.ModId)
                {
                    return mod.PublishedFileId == 0;
                }
            }

            return false;
        }

        public static string PrintPlayerName(ulong steamId)
        {
            var player = GetPlayerBySteamId(steamId);

            if(player == null)
                return $"[NotFound!] ({steamId.ToString()})";
            else
                return $"{player.DisplayName} ({steamId.ToString()})";
        }

        public static string PrintNullable<T>(Nullable<T> nullable) where T : struct
        {
            return (nullable.HasValue ? nullable.ToString() : "NULL");
        }

        public static string PrintObject<T>(T obj) where T : class
        {
            return (obj != null ? obj.ToString() : "NULL");
        }

        public static string PrintVector(Vector3 vec)
        {
            const string FORMAT = "0.##";
            return $"{vec.X.ToString(FORMAT)},{vec.Y.ToString(FORMAT)},{vec.Z.ToString(FORMAT)}";
        }
    }
}