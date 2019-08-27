using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ModAPI;
using VRageMath;

namespace Digi.PaintGun
{
    public partial class PaintGunMod
    {
        #region Receives
        public void ReceivedPacket(byte[] rawData)
        {
            PacketData packet = null;

            try
            {
                packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketData>(rawData); // this will throw errors on invalid data

                if(packet == null)
                    return;

                if(DEBUG)
                    Log.Info($"[DEBUG] ReceivedPacket() :: {packet.ToString().Replace("\n", ", ")}");

                ulong skipSteamId = 0;

                switch(packet.Type)
                {
                    case PacketAction.CONSUME_AMMO:
                        if(!Received_ConsumeAmmo(packet, ref skipSteamId))
                            return;
                        break;
                    case PacketAction.GUN_FIRING_ON:
                    case PacketAction.GUN_FIRING_OFF:
                        if(!Received_GunFiring(packet, ref skipSteamId))
                            return;
                        break;
                    case PacketAction.COLOR_PICK_ON:
                    case PacketAction.COLOR_PICK_OFF:
                        if(!Received_ColorPickMode(packet, ref skipSteamId))
                            return;
                        break;
                    case PacketAction.PAINT_BLOCK:
                        if(!Received_PaintBlock(packet, ref skipSteamId))
                            return;
                        break;
                    case PacketAction.BLOCK_REPLACE_COLOR:
                        if(!Received_BlockReplaceColor(packet, ref skipSteamId))
                            return;
                        break;
                    case PacketAction.SET_COLOR:
                        if(!Received_SetColor(packet, ref skipSteamId))
                            return;
                        break;
                    case PacketAction.SELECTED_SLOTS:
                        if(!Received_SelectedSlots(packet, ref skipSteamId))
                            return;
                        break;
                    case PacketAction.REQUEST_COLOR_LIST:
                        if(!Received_RequestColorList(packet, ref skipSteamId))
                            return;
                        break;
                    case PacketAction.UPDATE_COLOR:
                        if(!Received_UpdateColor(packet, ref skipSteamId))
                            return;
                        break;
                    case PacketAction.UPDATE_COLOR_LIST:
                        if(!Received_UpdateColorList(packet, ref skipSteamId))
                            return;
                        break;
                    case PacketAction.SKINTEST_REQUEST:
                        ownershipTestServer?.ReceivedPacket(packet);
                        return; // don't relay
                    case PacketAction.SKINTEST_RESULT:
                        ownershipTestPlayer?.ReceivedPacket(packet);
                        return; // don't relay
                }

                // relay packet to clients if type allows it
                if(MyAPIGateway.Multiplayer.IsServer && MyAPIGateway.Players.Count > 1)
                {
                    var myId = MyAPIGateway.Multiplayer.MyId;

                    players.Clear();
                    MyAPIGateway.Players.GetPlayers(players);

                    foreach(var p in players)
                    {
                        if(myId == p.SteamUserId) // don't re-send to yourself
                            continue;

                        if(skipSteamId > 0 && skipSteamId == p.SteamUserId) // don't send to the skipped ID
                            continue;

                        MyAPIGateway.Multiplayer.SendMessageTo(PACKET, rawData, p.SteamUserId, true);

                        if(DEBUG)
                            Log.Info($"relaying {packet.Type} to {p.DisplayName} ({p.SteamUserId})");
                    }

                    players.Clear();
                }
            }
            catch(Exception e)
            {
                Log.Info($"ReceivedPacket() :: rawData={rawData.Length}:{string.Join(",", rawData)}\nPacket: {packet}");
                Log.Error(e);
            }
        }

        private bool Received_ConsumeAmmo(PacketData packet, ref ulong skipSteamId)
        {
            if(!MyAPIGateway.Multiplayer.IsServer)
                return false; // server-side only

            IMyEntity ent;
            if(packet.EntityId == 0 || !MyAPIGateway.Entities.TryGetEntityById(packet.EntityId, out ent))
            {
                if(DEBUG)
                    Log.Error($"Can't get entity; packet={packet}");

                return false;
            }

            var inv = ent.GetInventory();

            if(inv != null)
                inv.RemoveItemsOfType(1, PAINT_MAG, false); // inventory actions get synchronized to clients automatically if called server-side

            return false; // don't relay to clients
        }

        private bool Received_GunFiring(PacketData packet, ref ulong skipSteamId)
        {
            var steamId = packet.SteamId;
            players.Clear();

            MyAPIGateway.Players.GetPlayers(players);

            foreach(var p in players)
            {
                if(p.SteamUserId == steamId)
                {
                    var item = p.Character?.EquippedTool?.GameLogic.GetAs<PaintGunItem>();

                    if(item != null)
                    {
                        item.Firing = (packet.Type == PacketAction.GUN_FIRING_ON);
                    }
                    else if(DEBUG)
                        Log.Info($"WARNING: Character no longer has paint gun equipped; packet={packet}");

                    break;
                }
            }

            skipSteamId = steamId;
            return true; // relay to clients
        }

        private bool Received_ColorPickMode(PacketData packet, ref ulong skipSteamId)
        {
            var steamId = packet.SteamId;

            if(steamId == 0)
            {
                if(DEBUG)
                    Log.Error($"Unexpected steamId 0 in packet: {packet}");
                return false;
            }

            if(packet.Type == PacketAction.COLOR_PICK_ON)
            {
                if(!playersColorPickMode.Contains(steamId))
                    playersColorPickMode.Add(steamId);
            }
            else
            {
                playersColorPickMode.Remove(steamId);

                if(localHeldTool != null)
                    localHeldTool.FireCooldown = 30;
            }

            return true; // relay to clients
        }

        private bool Received_PaintBlock(PacketData packet, ref ulong skipSteamId)
        {
            var steamId = packet.SteamId;

            if(!(MyAPIGateway.Multiplayer.IsServer && steamId == MyAPIGateway.Multiplayer.MyId)) // if sent by server, ignore action because it was already done
            {
                IMyEntity ent;
                if(packet.EntityId == 0 || !MyAPIGateway.Entities.TryGetEntityById(packet.EntityId, out ent))
                {
                    if(DEBUG)
                        Log.Error($"Can't find entity; packet={packet}");
                    return false;
                }

                var grid = ent as MyCubeGrid;

                if(grid == null)
                {
                    if(DEBUG)
                        Log.Error($"Can't find grid; packet={packet}");
                    return false;
                }

                var identity = MyAPIGateway.Players.TryGetIdentityId(steamId);

                if(!AllowedToPaintGrid(grid, identity))
                {
                    if(DEBUG)
                        Log.Error($"Can't paint unallied grids; packet={packet}");
                    return false;
                }

                if(!packet.GridPosition.HasValue || !grid.CubeExists(packet.GridPosition.Value))
                {
                    if(DEBUG)
                        Log.Error($"Can't paint inexistent blocks; packet={packet}");
                    return false;
                }

                var paint = packet.Paint.GetMaterial();

                if(packet.MirrorPlanes.HasValue) // symmetry paint
                {
                    PaintBlockSymmetry(grid, packet.GridPosition.Value, paint, packet.MirrorPlanes.Value, packet.OddAxis, false);
                }
                else
                {
                    PaintBlock(grid, packet.GridPosition.Value, paint, false);
                }
            }

            skipSteamId = steamId; // skip relaying to this id
            return true; // relay to clients
        }

        private bool Received_BlockReplaceColor(PacketData packet, ref ulong skipSteamId)
        {
            var steamId = packet.SteamId;

            if(!(MyAPIGateway.Multiplayer.IsServer && steamId == MyAPIGateway.Multiplayer.MyId)) // if sent by server, ignore action because it was already done
            {
                IMyEntity ent;
                if(packet.EntityId == 0 || !MyAPIGateway.Entities.TryGetEntityById(packet.EntityId, out ent))
                    return false;

                var grid = (ent as MyCubeGrid);

                if(grid == null)
                    return false;

                var identity = MyAPIGateway.Players.TryGetIdentityId(steamId);

                if(!AllowedToPaintGrid(grid, identity))
                    return false;

                var newPaint = packet.Paint.GetMaterial();
                var oldPaint = packet.OldPaint.GetMaterial();

                ReplaceColorInGrid(grid, oldPaint, newPaint, packet.UseGridSystem, false);
            }

            skipSteamId = steamId; // skip relaying to this id
            return true; // relay to clients
        }

        private bool Received_SetColor(PacketData packet, ref ulong skipSteamId)
        {
            if(!MyAPIGateway.Multiplayer.IsServer) // server-side only 
                return false;

            var steamId = packet.SteamId;
            EnsureColorDataEntry(steamId);
            var pcd = playerColorData[steamId];

            if(packet.Slot.HasValue)
            {
                var slot = packet.Slot.Value;

                if(packet.Paint.Color.HasValue)
                {
                    var colorMask = RGBToColorMask(packet.Paint.Color.Value);

                    pcd.Colors[slot] = colorMask;

                    foreach(var kv in MyCubeBuilder.AllPlayersColors)
                    {
                        var id = GetSteamId(kv.Key.ToString());

                        if(steamId == id)
                        {
                            kv.Value[slot] = colorMask;
                            SendToAllPlayers_UpdateColor(steamId, slot, colorMask);
                            break;
                        }
                    }
                }
            }

            if(packet.Paint.SkinIndex.HasValue)
            {
                pcd.SelectedSkinIndex = packet.Paint.SkinIndex.Value;
            }

            return false; // don't relay to clients
        }

        private bool Received_SelectedSlots(PacketData packet, ref ulong skipSteamId)
        {
            var steamId = packet.SteamId;
            EnsureColorDataEntry(steamId);
            var pcd = playerColorData[steamId];

            if(packet.Slot.HasValue)
            {
                var slot = packet.Slot.Value;
                pcd.SelectedSlot = slot;

                var player = GetPlayerBySteamId(steamId);

                if(player != null)
                {
                    player.SelectedBuildColorSlot = slot;
                    player.Character?.EquippedTool?.GameLogic?.GetAs<PaintGunItem>()?.SetToolColor(pcd.ApplyColor ? pcd.Colors[slot] : DEFAULT_COLOR);
                }
            }

            if(packet.Paint.SkinIndex.HasValue)
            {
                pcd.SelectedSkinIndex = packet.Paint.SkinIndex.Value;
            }

            skipSteamId = steamId; // skip relaying to this id
            return true; // relay to clients
        }

        private bool Received_RequestColorList(PacketData packet, ref ulong skipSteamId)
        {
            if(!MyAPIGateway.Multiplayer.IsServer) // server-side only 
                return false;

            var steamId = packet.SteamId;

            // send all online players' colors to the connected player
            foreach(var kv in playerColorData)
            {
                if(IsPlayerOnline(kv.Key))
                    SendToPlayer_SendColorList(steamId, kv.Key, kv.Value.SelectedSlot, kv.Value.Colors);
            }

            if(EnsureColorDataEntry(steamId)) // send this player's colors to everyone if available, otherwise they'll be sent automatically when they are available
            {
                var pcd = playerColorData[steamId];
                SendToPlayer_SendColorList(0, steamId, pcd.SelectedSlot, pcd.Colors);
            }

            return false; // don't relay to clients
        }

        private bool Received_UpdateColor(PacketData packet, ref ulong skipSteamId)
        {
            if(MyAPIGateway.Multiplayer.IsServer) // client-side only
                return false;

            var slot = packet.Slot.Value;
            var steamId = packet.SteamId;
            var colorMask = RGBToColorMask(packet.Paint.Color.Value);

            EnsureColorDataEntry(steamId);

            var pcd = playerColorData[steamId];
            pcd.Colors[slot] = colorMask;

            if(packet.Paint.SkinIndex.HasValue)
                pcd.SelectedSkinIndex = packet.Paint.SkinIndex.Value;

            return false; // don't relay (not that it even can since it's clientside only)
        }

        private bool Received_UpdateColorList(PacketData packet, ref ulong skipSteamId)
        {
            if(MyAPIGateway.Multiplayer.IsServer) // client-side only
                return false;

            var slot = packet.Slot.Value;
            var steamId = packet.SteamId;

            EnsureColorDataEntry(steamId);

            var pcd = playerColorData[steamId];

            for(int i = 0; i < COLOR_PALETTE_SIZE; i++)
            {
                pcd.Colors[i] = RGBToColorMask(packet.PackedColors[i]);
            }

            var player = GetPlayerBySteamId(steamId);

            if(player != null)
            {
                player.SelectedBuildColorSlot = slot;
                player.Character?.EquippedTool?.GameLogic?.GetAs<PaintGunItem>()?.SetToolColor(pcd.ApplyColor ? pcd.Colors[slot] : DEFAULT_COLOR);
            }

            return false; // don't relay (not that it even can since it's clientside only)
        }
        #endregion

        #region Sends
        public void SendToServer_RemoveAmmo(long entId)
        {
            try
            {
                packetConsumeAmmo.EntityId = entId;

                var rawData = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packetConsumeAmmo);

                if(DEBUG)
                    Log.Info($"SendToServer_RemoveAmmo({entId}) :: rawData={rawData.Length}:{string.Join(",", rawData)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, rawData, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_PaintBlock(MyCubeGrid grid, Vector3I gridPosition, PaintMaterial paint, Vector3I? mirrorPlanes = null, OddAxis odd = OddAxis.NONE)
        {
            try
            {
                packetPaint.SteamId = MyAPIGateway.Multiplayer.MyId;
                packetPaint.EntityId = grid.EntityId;
                packetPaint.GridPosition = gridPosition;
                packetPaint.Paint = new SerializedPaintMaterial(paint);
                packetPaint.MirrorPlanes = mirrorPlanes;
                packetPaint.OddAxis = odd;

                var rawData = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packetPaint);

                if(DEBUG)
                    Log.Info($"SendToServer_PaintBlock() :: rawData={rawData.Length}:{string.Join(",", rawData)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, rawData, true);

                // do the action locally as well
                if(mirrorPlanes.HasValue)
                    PaintBlockSymmetry(grid, gridPosition, paint, mirrorPlanes.Value, odd, true);
                else
                    PaintBlock(grid, gridPosition, paint, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_ReplaceColor(MyCubeGrid grid, BlockMaterial oldPaint, PaintMaterial newPaint, bool useGridSystem)
        {
            try
            {
                packetReplaceColor.SteamId = MyAPIGateway.Multiplayer.MyId;
                packetReplaceColor.EntityId = grid.EntityId;
                packetReplaceColor.UseGridSystem = useGridSystem;
                packetReplaceColor.Paint = new SerializedPaintMaterial(newPaint);
                packetReplaceColor.OldPaint = new SerializedBlockMaterial(oldPaint);

                var rawData = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packetReplaceColor);

                if(DEBUG)
                    Log.Info($"SendToServer_ReplaceColor() :: rawData={rawData.Length}:{string.Join(",", rawData)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, rawData, true);

                // do the action locally as well
                ReplaceColorInGrid(grid, oldPaint, newPaint, useGridSystem, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_SelectedSlots(int? colorSlot, int? skinIndex)
        {
            if(!colorSlot.HasValue && !skinIndex.HasValue)
                return;

            packetSelectedSlots.SteamId = MyAPIGateway.Multiplayer.MyId;
            packetSelectedSlots.Slot = colorSlot;
            packetSelectedSlots.Paint = new SerializedPaintMaterial(null, skinIndex);

            var rawData = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packetSelectedSlots);

            if(DEBUG)
                Log.Info($"SendToServer_SelectedSlots({colorSlot}, {skinIndex}) :: rawData={rawData.Length}:{string.Join(",", rawData)}");

            MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, rawData, true);
        }

        public void SendToServer_ColorPickMode(bool mode)
        {
            try
            {
                packetColorPickMode.Type = (mode ? PacketAction.COLOR_PICK_ON : PacketAction.COLOR_PICK_OFF);
                packetColorPickMode.SteamId = MyAPIGateway.Multiplayer.MyId;

                var rawData = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packetColorPickMode);

                if(DEBUG)
                    Log.Info($"SendToServer_ColorPickMode({mode}) :: rawData={rawData.Length}:{string.Join(",", rawData)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, rawData, true);

                colorPickMode = mode;
                prevColorMaskPreview = GetLocalBuildColorMask();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void PickColorAndSkinFromBlock(int slot, BlockMaterial blockMaterial)
        {
            if(SendToServer_SetColorAndSkin(slot, blockMaterial, true))
                PlayHudSound(SOUND_HUD_MOUSE_CLICK, 0.25f);
            else
                PlayHudSound(SOUND_HUD_UNABLE, SOUND_HUD_UNABLE_VOLUME, SOUND_HUD_UNABLE_TIMEOUT);
        }

        private bool SendToServer_SetColorAndSkin(int slot, BlockMaterial blockMaterial, bool checkAndSelect = true)
        {
            try
            {
                int? selectedColor = null;
                int? selectedSkin = null;

                if(checkAndSelect)
                {
                    var paintMaterial = GetLocalPaintMaterial();

                    if(paintMaterial.PaintEquals(blockMaterial))
                    {
                        ShowNotification(0, $"Color and skin already selected.", MyFontEnum.White, 2000);
                        return false;
                    }

                    for(int i = 0; i < localColorData.Colors.Count; i++)
                    {
                        if(ColorMaskEquals(localColorData.Colors[i], blockMaterial.ColorMask))
                        {
                            localColorData.SelectedSlot = i;
                            MyAPIGateway.Session.Player.SelectedBuildColorSlot = i;
                            selectedColor = i;

                            ShowNotification(0, $"Color exists in slot {i + 1}, selected.", MyFontEnum.White, 2000);
                            break;
                        }
                    }

                    if(BlockSkins[localColorData.SelectedSkinIndex].SubtypeId != blockMaterial.Skin)
                    {
                        var skin = GetSkinInfo(blockMaterial.Skin);

                        if(skin == null)
                        {
                            Log.Error($"Block has unknown skin: {blockMaterial.Skin}", Log.PRINT_MSG);
                        }
                        else if(skin.LocallyOwned)
                        {
                            localColorData.SelectedSkinIndex = skin.Index;
                            selectedSkin = skin.Index;

                            ShowNotification(1, $"Selected skin {GetSkinInfo(blockMaterial.Skin).Name}", MyFontEnum.White, 2000);
                        }
                        else
                        {
                            ShowNotification(1, $"Skin {skin.Name} is not owned, not selected.", MyFontEnum.Red, 2000);
                        }
                    }
                }

                if(selectedColor.HasValue || selectedSkin.HasValue)
                {
                    SendToServer_SelectedSlots(selectedColor, selectedSkin);
                }

                if(!selectedColor.HasValue)
                {
                    packetSetColor.SteamId = MyAPIGateway.Multiplayer.MyId;
                    packetSetColor.Slot = slot;
                    packetSetColor.Paint = new SerializedPaintMaterial(ColorMaskToRGB(blockMaterial.ColorMask), null);

                    var rawData = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packetSetColor);

                    if(DEBUG)
                        Log.Info($"SendToServer_SetColorAndSkin({slot}, {blockMaterial}, {checkAndSelect}) :: rawData={rawData.Length}:{string.Join(",", rawData)}");

                    MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, rawData, true);

                    MyAPIGateway.Session.Player.ChangeOrSwitchToColor(blockMaterial.ColorMask);
                    ShowNotification(0, $"Color slot {localColorData.SelectedSlot + 1} set to {ColorMaskToString(blockMaterial.ColorMask)}", MyFontEnum.White, 2000);
                }

                return (selectedColor.HasValue || selectedSkin.HasValue);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }

            return false;
        }

        public void SendToServer_RequestColorList(ulong steamId)
        {
            try
            {
                packetRequestColorList.SteamId = steamId;

                var rawData = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packetRequestColorList);

                if(DEBUG)
                    Log.Info($"SendToServer_RequestColorList({steamId}) :: rawData={rawData.Length}:{string.Join(",", rawData)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, rawData, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToAllPlayers_UpdateColor(ulong colorOwner, int slot, Vector3 colorMask)
        {
            try
            {
                if(!MyAPIGateway.Multiplayer.IsServer) // server side only
                    return;

                players.Clear();
                MyAPIGateway.Players.GetPlayers(players);

                if(players.Count == 0)
                    return;

                packetUpdateColor.SteamId = colorOwner;
                packetUpdateColor.Slot = slot;
                packetUpdateColor.Paint = new SerializedPaintMaterial(ColorMaskToRGB(colorMask), null);

                var rawData = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packetUpdateColor);

                if(DEBUG)
                    Log.Info($"SendToAllPlayers_UpdateColor({colorOwner}, {slot}, {colorMask}) :: rawData={rawData.Length}:{string.Join(",", rawData)}");

                var myId = MyAPIGateway.Multiplayer.MyId;

                foreach(var p in players)
                {
                    if(myId == p.SteamUserId) // don't re-send to yourself
                        continue;

                    MyAPIGateway.Multiplayer.SendMessageTo(PACKET, rawData, p.SteamUserId, true);
                }

                players.Clear();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToPlayer_SendColorList(ulong sendTo, ulong colorOwner, int slot, List<Vector3> colorList)
        {
            try
            {
                if(!MyAPIGateway.Multiplayer.IsServer) // server side only
                    return;

                if(colorList.Count != COLOR_PALETTE_SIZE)
                {
                    Log.Error("Size of color palette does not match the defined palette: list=" + colorList.Count + ", COLOR_PALETTE_SIZE=" + COLOR_PALETTE_SIZE);
                    return;
                }

                packetUpdateColorList.SteamId = colorOwner;
                packetUpdateColorList.Slot = slot;

                for(int i = 0; i < COLOR_PALETTE_SIZE; i++)
                {
                    packetUpdateColorList.PackedColors[i] = ColorMaskToRGB(colorList[i]);
                }

                var rawData = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packetUpdateColorList);

                if(DEBUG)
                    Log.Info($"SendToPlayer_SendColorList({sendTo}, {colorOwner}, {slot}, ...) :: rawData={rawData.Length}:{string.Join(",", rawData)}");

                if(sendTo == 0)
                {
                    var myId = MyAPIGateway.Multiplayer.MyId;

                    players.Clear();
                    MyAPIGateway.Players.GetPlayers(players);

                    foreach(var p in players)
                    {
                        if(myId == p.SteamUserId) // don't re-send to yourself
                            continue;

                        MyAPIGateway.Multiplayer.SendMessageTo(PACKET, rawData, p.SteamUserId, true);
                    }

                    players.Clear();
                }
                else
                {
                    MyAPIGateway.Multiplayer.SendMessageTo(PACKET, rawData, sendTo, true);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_PaintGunFiring(PaintGunItem item, bool firing)
        {
            try
            {
                item.Firing = firing;

                packetPaintGunFiring.Type = (firing ? PacketAction.GUN_FIRING_ON : PacketAction.GUN_FIRING_OFF);
                packetPaintGunFiring.SteamId = MyAPIGateway.Multiplayer.MyId;

                var rawData = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packetPaintGunFiring);

                if(DEBUG)
                    Log.Info($"SendToServer_PaintGunFiring({item}, {firing}) :: rawData={rawData.Length}:{string.Join(",", rawData)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, rawData, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
        #endregion
    }
}