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
        public void ReceivedPacket(byte[] bytes)
        {
            try
            {
                if(DEBUG)
                    Log.Info($"ReceivedPacket; bytes={bytes.Length}:{string.Join(",", bytes)}");

                var packet = MyAPIGateway.Utilities.SerializeFromBinary<PacketData>(bytes); // this will throw errors on invalid data

                if(packet == null)
                    return;

                bool isServer = MyAPIGateway.Multiplayer.IsServer;
                ulong skipSteamId = 0;

                switch(packet.Type)
                {
                    case PacketAction.CONSUME_AMMO:
                        {
                            if(!isServer)
                                return; // server-side only

                            IMyEntity ent;
                            if(packet.EntityId == 0 || !MyAPIGateway.Entities.TryGetEntityById(packet.EntityId, out ent))
                            {
                                if(DEBUG)
                                    Log.Error($"Can't get entity; packet={packet}");
                                return;
                            }

                            var inv = ent.GetInventory();

                            if(inv != null)
                                inv.RemoveItemsOfType(1, PAINT_MAG, false); // inventory actions get synchronized to clients automatically if called server-side

                            return; // don't relay to clients
                        }
                    case PacketAction.GUN_FIRING_ON:
                    case PacketAction.GUN_FIRING_OFF:
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
                            break; // relay to clients
                        }
                    case PacketAction.COLOR_PICK_ON:
                    case PacketAction.COLOR_PICK_OFF:
                        {
                            var steamId = packet.SteamId;

                            if(steamId == 0)
                            {
                                if(DEBUG)
                                    Log.Error($"Unexpected steamId 0 in packet: {packet}");
                                return;
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

                            break; // relay to clients
                        }
                    case PacketAction.PAINT_BLOCK:
                        {
                            var steamId = packet.SteamId;

                            if(!(isServer && steamId == MyAPIGateway.Multiplayer.MyId)) // if sent by server, ignore action because it was already done
                            {
                                IMyEntity ent;
                                if(packet.EntityId == 0 || !MyAPIGateway.Entities.TryGetEntityById(packet.EntityId, out ent))
                                {
                                    if(DEBUG)
                                        Log.Error($"Can't find entity; packet={packet}");
                                    return;
                                }

                                var grid = (ent as MyCubeGrid);

                                if(grid == null)
                                {
                                    if(DEBUG)
                                        Log.Error($"Can't find grid; packet={packet}");
                                    return;
                                }

                                var identity = MyAPIGateway.Players.TryGetIdentityId(steamId);

                                if(!grid.ColorGridOrBlockRequestValidation(identity))
                                {
                                    if(DEBUG)
                                        Log.Error($"Can't paint unallied grids; packet={packet}");
                                    return;
                                }

                                if(!packet.GridPosition.HasValue || !grid.CubeExists(packet.GridPosition.Value))
                                {
                                    if(DEBUG)
                                        Log.Error($"Can't paint inexistent blocks; packet={packet}");
                                    return;
                                }

                                var colorMask = RGBToColorMask(new Color(packet.PackedColor));

                                if(packet.MirrorPlanes.HasValue) // symmetry paint
                                {
                                    PaintBlockSymmetry(grid, packet.GridPosition.Value, colorMask, packet.MirrorPlanes.Value, packet.OddAxis);
                                }
                                else
                                {
                                    PaintBlock(grid, packet.GridPosition.Value, colorMask);
                                }
                            }

                            skipSteamId = steamId; // skip relaying to this id
                            break; // relay to clients
                        }
                    case PacketAction.BLOCK_REPLACE_COLOR: // replace color on all blocks
                        {
                            var steamId = packet.SteamId;

                            if(!(isServer && steamId == MyAPIGateway.Multiplayer.MyId)) // if sent by server, ignore action because it was already done
                            {
                                IMyEntity ent;
                                if(packet.EntityId == 0 || !MyAPIGateway.Entities.TryGetEntityById(packet.EntityId, out ent))
                                    return;

                                var grid = (ent as MyCubeGrid);

                                if(grid == null)
                                    return;

                                var identity = MyAPIGateway.Players.TryGetIdentityId(steamId);

                                if(!grid.ColorGridOrBlockRequestValidation(identity))
                                    return;

                                var oldColorMask = RGBToColorMask(new Color(packet.PackedColor));
                                var newColorMask = RGBToColorMask(new Color(packet.PackedColor2));

                                ReplaceColorInGrid(grid, steamId, oldColorMask, newColorMask, packet.UseGridSystem);
                            }

                            skipSteamId = steamId; // skip relaying to this id
                            break; // relay to clients
                        }
                    case PacketAction.SET_COLOR:
                        {
                            if(!isServer)
                                return; // server-side only

                            var slot = packet.Slot;
                            var steamId = packet.SteamId;
                            var colorMask = RGBToColorMask(new Color(packet.PackedColor));

                            EnsureColorDataEntry(steamId);

                            playerColorData[steamId].Colors[slot] = colorMask;

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

                            return; // don't relay to clients
                        }
                    case PacketAction.SELECTED_COLOR_SLOT:
                        {
                            var slot = packet.Slot;
                            var steamId = packet.SteamId;

                            EnsureColorDataEntry(steamId);

                            var cd = playerColorData[steamId];
                            cd.SelectedSlot = slot;

                            var player = GetPlayerBySteamId(steamId);

                            if(player != null)
                            {
                                player.SelectedBuildColorSlot = slot;
                                player.Character?.EquippedTool?.GameLogic?.GetAs<PaintGunItem>()?.SetToolColor(cd.Colors[slot]);
                            }

                            skipSteamId = steamId; // skip relaying to this id
                            break; // relay to clients
                        }
                    case PacketAction.REQUEST_COLOR_LIST:
                        {
                            if(!isServer)
                                return; // server-side only

                            var steamId = packet.SteamId;

                            // send all online players' colors to the connected player
                            foreach(var kv in playerColorData)
                            {
                                if(IsPlayerOnline(kv.Key))
                                    SendToPlayer_SendColorList(steamId, kv.Key, (byte)kv.Value.SelectedSlot, kv.Value.Colors);
                            }

                            if(EnsureColorDataEntry(steamId)) // send this player's colors to everyone if available, otherwise they'll be sent automatically when they are available
                            {
                                var cd = playerColorData[steamId];
                                SendToPlayer_SendColorList(0, steamId, (byte)cd.SelectedSlot, cd.Colors);
                            }

                            return; // don't relay to clients
                        }
                    case PacketAction.UPDATE_COLOR:
                        {
                            if(isServer)
                                return; // client-side only

                            var slot = packet.Slot;
                            var steamId = packet.SteamId;
                            var colorMask = RGBToColorMask(new Color(packet.PackedColor));

                            EnsureColorDataEntry(steamId);

                            playerColorData[steamId].Colors[slot] = colorMask;

                            return; // don't relay (not that it even can since it's clientside only)
                        }
                    case PacketAction.UPDATE_COLOR_LIST:
                        {
                            if(isServer)
                                return; // client-side only

                            var slot = packet.Slot;
                            var steamId = packet.SteamId;

                            EnsureColorDataEntry(steamId);

                            var cd = playerColorData[steamId];

                            for(int i = 0; i < COLOR_PALETTE_SIZE; i++)
                            {
                                cd.Colors[i] = RGBToColorMask(new Color(packet.PackedColors[i]));
                            }

                            var player = GetPlayerBySteamId(steamId);

                            if(player != null)
                            {
                                player.SelectedBuildColorSlot = slot;
                                player.Character?.EquippedTool?.GameLogic?.GetAs<PaintGunItem>()?.SetToolColor(cd.Colors[slot]);
                            }

                            return; // don't relay (not that it even can since it's clientside only)
                        }
                }

                // relay packet to clients if type allows it
                if(isServer && MyAPIGateway.Players.Count > 1)
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

                        MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, p.SteamUserId, true);

                        if(DEBUG)
                            Log.Info($"relaying {packet.Type} to {p.DisplayName} ({p.SteamUserId})");
                    }

                    players.Clear();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_RemoveAmmo(long entId)
        {
            try
            {
                var packet = new PacketData()
                {
                    Type = PacketAction.CONSUME_AMMO,
                    EntityId = entId,
                };

                var bytes = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packet);

                if(DEBUG)
                    Log.Info($"SendToServer_RemoveAmmo({entId}) :: bytes={bytes.Length}:{string.Join(",", bytes)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_PaintBlock(MyCubeGrid grid, Vector3I gridPosition, Vector3 color, Vector3I? mirrorPlanes = null, OddAxis odd = OddAxis.NONE)
        {
            try
            {
                var packet = new PacketData()
                {
                    Type = PacketAction.PAINT_BLOCK,
                    SteamId = MyAPIGateway.Multiplayer.MyId,
                    EntityId = grid.EntityId,
                    GridPosition = gridPosition,
                    PackedColor = ColorMaskToRGB(color).PackedValue,
                    MirrorPlanes = mirrorPlanes,
                    OddAxis = odd,
                };

                var bytes = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packet);

                if(DEBUG)
                    Log.Info($"SendToServer_PaintBlock() :: bytes={bytes.Length}:{string.Join(",", bytes)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);

                // do the action locally as well
                if(mirrorPlanes.HasValue)
                    PaintBlockSymmetry(grid, gridPosition, color, mirrorPlanes.Value, odd);
                else
                    PaintBlock(grid, gridPosition, color);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_ReplaceColor(MyCubeGrid grid, Vector3 oldColorMask, Vector3 newColorMask, bool useGridSystem)
        {
            try
            {
                var steamId = MyAPIGateway.Multiplayer.MyId;
                var packet = new PacketData()
                {
                    Type = PacketAction.BLOCK_REPLACE_COLOR,
                    SteamId = steamId,
                    EntityId = grid.EntityId,
                    PackedColor = ColorMaskToRGB(oldColorMask).PackedValue,
                    PackedColor2 = ColorMaskToRGB(newColorMask).PackedValue,
                    UseGridSystem = useGridSystem,
                };

                var bytes = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packet);

                if(DEBUG)
                    Log.Info($"SendToServer_ReplaceColor() :: bytes={bytes.Length}:{string.Join(",", bytes)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);

                // do the action locally as well
                ReplaceColorInGrid(grid, steamId, oldColorMask, newColorMask, useGridSystem);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToServer_SelectedColorSlot(byte slot)
        {
            var steamId = MyAPIGateway.Multiplayer.MyId;
            var packet = new PacketData()
            {
                Type = PacketAction.SELECTED_COLOR_SLOT,
                SteamId = steamId,
                Slot = slot,
            };

            var bytes = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packet);

            if(DEBUG)
                Log.Info($"SendToServer_SelectedColorSlot({slot}) :: bytes={bytes.Length}:{string.Join(",", bytes)}");

            MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
        }

        public void SendToServer_ColorPickMode(bool mode)
        {
            try
            {
                var steamId = MyAPIGateway.Multiplayer.MyId;
                var packet = new PacketData()
                {
                    Type = (mode ? PacketAction.COLOR_PICK_ON : PacketAction.COLOR_PICK_OFF),
                    SteamId = steamId,
                };

                var bytes = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packet);

                if(DEBUG)
                    Log.Info($"SendToServer_ColorPickMode({mode}) :: bytes={bytes.Length}:{string.Join(",", bytes)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);

                colorPickMode = mode;
                prevColorMaskPreview = GetBuildColorMask();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public bool SendToServer_SetColor(byte slot, Vector3 colorMask, bool checkAndSelect)
        {
            try
            {
                var myId = MyAPIGateway.Multiplayer.MyId;
                PlayerColorData cd;

                if(checkAndSelect && playerColorData.TryGetValue(myId, out cd))
                {
                    for(int i = 0; i < cd.Colors.Count; i++)
                    {
                        if(ColorMaskEquals(cd.Colors[i], colorMask))
                        {
                            localColorData.SelectedSlot = i;
                            MyAPIGateway.Session.Player.SelectedBuildColorSlot = i;
                            SendToServer_SelectedColorSlot((byte)i);
                            ShowNotification(0, $"Color exists in slot {i + 1}, selected.", MyFontEnum.White, 2000);
                            return false; // color exists in palette, stop sending.
                        }
                    }
                }

                var packet = new PacketData()
                {
                    Type = PacketAction.SET_COLOR,
                    SteamId = myId,
                    Slot = slot,
                    PackedColor = ColorMaskToRGB(colorMask).PackedValue,
                };

                var bytes = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packet);

                if(DEBUG)
                    Log.Info($"SendToServer_SetColor({slot}, {colorMask}, {checkAndSelect}) :: bytes={bytes.Length}:{string.Join(",", bytes)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);

                MyAPIGateway.Session.Player.ChangeOrSwitchToColor(colorMask);
                return true;
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
                var packet = new PacketData()
                {
                    Type = PacketAction.REQUEST_COLOR_LIST,
                    SteamId = steamId,
                };

                var bytes = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packet);

                if(DEBUG)
                    Log.Info($"SendToServer_RequestColorList({steamId}) :: bytes={bytes.Length}:{string.Join(",", bytes)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToAllPlayers_UpdateColor(ulong colorOwner, byte slot, Vector3 color)
        {
            try
            {
                if(!MyAPIGateway.Multiplayer.IsServer) // server side only
                    return;

                players.Clear();
                MyAPIGateway.Players.GetPlayers(players);

                if(players.Count == 0)
                    return;

                var packet = new PacketData()
                {
                    Type = PacketAction.UPDATE_COLOR,
                    SteamId = colorOwner,
                    Slot = slot,
                    PackedColor = ColorMaskToRGB(color).PackedValue,
                };

                var bytes = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packet);

                if(DEBUG)
                    Log.Info($"SendToAllPlayers_UpdateColor({colorOwner}, {slot}, {color}) :: bytes={bytes.Length}:{string.Join(",", bytes)}");

                var myId = MyAPIGateway.Multiplayer.MyId;

                foreach(var p in players)
                {
                    if(myId == p.SteamUserId) // don't re-send to yourself
                        continue;

                    MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, p.SteamUserId, true);
                }

                players.Clear();
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public void SendToPlayer_SendColorList(ulong sendTo, ulong colorOwner, byte slot, List<Vector3> colorList)
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

                var packet = new PacketData()
                {
                    Type = PacketAction.UPDATE_COLOR_LIST,
                    SteamId = colorOwner,
                    Slot = slot,
                };

                packet.PackedColors = new uint[COLOR_PALETTE_SIZE];

                for(int i = 0; i < COLOR_PALETTE_SIZE; i++)
                {
                    packet.PackedColors[i] = ColorMaskToRGB(colorList[i]).PackedValue;
                }

                var bytes = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packet);

                if(DEBUG)
                    Log.Info($"SendToPlayer_SendColorList({sendTo}, {colorOwner}, {slot}, ...) :: bytes={bytes.Length}:{string.Join(",", bytes)}");

                if(sendTo == 0)
                {
                    var myId = MyAPIGateway.Multiplayer.MyId;

                    players.Clear();
                    MyAPIGateway.Players.GetPlayers(players);

                    foreach(var p in players)
                    {
                        if(myId == p.SteamUserId) // don't re-send to yourself
                            continue;

                        MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, p.SteamUserId, true);
                    }

                    players.Clear();
                }
                else
                {
                    MyAPIGateway.Multiplayer.SendMessageTo(PACKET, bytes, sendTo, true);
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

                var packet = new PacketData()
                {
                    Type = (firing ? PacketAction.GUN_FIRING_ON : PacketAction.GUN_FIRING_OFF),
                    SteamId = MyAPIGateway.Multiplayer.MyId,
                };

                var bytes = MyAPIGateway.Utilities.SerializeToBinary<PacketData>(packet);

                if(DEBUG)
                    Log.Info($"SendToServer_PaintGunFiring({item}, {firing}) :: bytes={bytes.Length}:{string.Join(",", bytes)}");

                MyAPIGateway.Multiplayer.SendMessageToServer(PACKET, bytes, true);
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }
    }
}