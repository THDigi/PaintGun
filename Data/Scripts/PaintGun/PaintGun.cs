using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using VRage;
using VRage.ObjectBuilders;
using VRage.Game.Components;
using VRage.ModAPI;

using Digi.Utils;

namespace Digi.PaintGun
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AutomaticRifle), PaintGunMod.PAINT_GUN_ID)]
    public class PaintGun : MyGameLogicComponent
    {
        public class Particle
        {
            public Color color;
            public Vector3 relativePosition;
            public Vector3 velocity;
            public Vector3 playerVelocity;
            public short life;
            public float radius;
            public float angle;

            public Particle() { }
        }

        public ulong heldById = 0;
        public bool heldByLocalPlayer = false;
        public long cooldown = 0;
        private Vector3 color = PaintGunMod.DEFAULT_COLOR;

        private bool first = true;
        private byte skip = 0;
        private long lastShotTime = 0;
        private MyEntity3DSoundEmitter soundEmitter;
        public List<Particle> particles = new List<Particle>(20);

        public static Random rand = new Random();

        private const int SKIP_UPDATES = 10;
        private const long DELAY_SHOOT = (TimeSpan.TicksPerMillisecond * 200);
        private const long DELAY_ACTION_SHOOT_COOLDOWN = (TimeSpan.TicksPerMillisecond * 600);
        private readonly MySoundPair soundPair = new MySoundPair("PaintGunSpray");
        private static List<IMyPlayer> players = new List<IMyPlayer>(0);
        private const int PARTICLE_MAX_DISTANCE_SQ = 1000 * 1000;

        private PaintGunMod mod = null;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            Entity.NeedsUpdate |= MyEntityUpdateEnum.EACH_FRAME;
        }

        public void FirstUpdate()
        {
            var tool = Entity.GetObjectBuilder(false) as MyObjectBuilder_AutomaticRifle;
            lastShotTime = tool.GunBase.LastShootTime;

            var charEnt = MyAPIGateway.Session.ControlledObject as IMyCharacter;

            if(charEnt != null) // first check if local player holds the tool
            {
                var charObj = charEnt.GetObjectBuilder(false) as MyObjectBuilder_Character;

                if(charObj.HandWeapon != null && charObj.HandWeapon.EntityId == Entity.EntityId)
                {
                    heldById = MyAPIGateway.Multiplayer.MyId;
                    heldByLocalPlayer = true;

                    //PaintGunMod.SetToolStatus("Type /pg for Paint Gun options.", MyFontEnum.DarkBlue, 3000);
                }
            }

            if(heldById == 0) // then find the owner through the player list
            {
                // TODO if holdingTools is removed then no reason to know which player holds this entity

                long skipEntId = (MyAPIGateway.Session.ControlledObject is IMyCharacter ? MyAPIGateway.Session.ControlledObject.Entity.EntityId : -1);
                MyAPIGateway.Players.GetPlayers(players, delegate (IMyPlayer p)
                                                {
                                                    if(heldById == 0 && p.Controller != null && p.Controller.ControlledEntity is IMyCharacter)
                                                    {
                                                        var ent = p.Controller.ControlledEntity.Entity;

                                                        if(skipEntId != ent.EntityId) // skip local character entity
                                                        {
                                                            var charObj = ent.GetObjectBuilder(false) as MyObjectBuilder_Character;

                                                            if(charObj != null && charObj.HandWeapon != null && charObj.HandWeapon.EntityId == Entity.EntityId)
                                                            {
                                                                heldById = p.SteamUserId;
                                                            }
                                                        }
                                                    }

                                                    return false; // no need to add anything to the list.
                                                });
            }

            if(heldById == 0)
            {
                Log.Info("ERROR: Can't find holder of a paint gun entity."); // silent error
                MyAPIGateway.Utilities.ShowNotification("Can't find holder of paint gun entity, please report the circumstances!", 10000, MyFontEnum.Red);
                return;
            }

            if(mod.holdingTools.ContainsKey(heldById))
                mod.holdingTools[heldById] = Entity;
            else
                mod.holdingTools.Add(heldById, Entity);

            if(heldByLocalPlayer)
                mod.localHeldTool = this;

            PlayerColorData cd;

            if(mod.playerColorData.TryGetValue(heldById, out cd))
                SetToolColor(cd.colors[cd.selectedSlot]);

            if(soundEmitter == null)
            {
                soundEmitter = new MyEntity3DSoundEmitter(Entity as MyEntity);
                soundEmitter.CustomMaxDistance = 30f;
                soundEmitter.CustomVolume = mod.settings.spraySoundVolume;
            }
        }

        public void SetToolColor(Vector3 color)
        {
            if(this.color.EqualsToHSV(color))
                return;

            this.color = color;
            Entity.SetEmissiveParts("Emissive", PaintGunMod.HSVtoRGB(color), 0);
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if(first)
                {
                    if(mod == null)
                    {
                        mod = PaintGunMod.instance;
                        return;
                    }

                    if(!mod.init || mod.settings == null)
                        return;

                    first = false;
                    FirstUpdate();
                }

                if(mod.isThisHostDedicated || heldById == 0)
                    return;

                var tool = Entity.GetObjectBuilder(false) as MyObjectBuilder_AutomaticRifle;
                var player = (heldByLocalPlayer && MyAPIGateway.Session.ControlledObject != null ? MyAPIGateway.Session.ControlledObject.Entity : null);
                long ticks = DateTime.UtcNow.Ticks;
                long shootTime = tool.GunBase.LastShootTime;
                bool trigger = shootTime + DELAY_SHOOT > ticks;

                if(heldByLocalPlayer)
                {
                    if(mod.replaceAllMode && !MyAPIGateway.Session.CreativeMode)
                        mod.replaceAllMode = false; // in case creative mode was on and gets turned off, replace all mode shouldn't remain active either.

                    if(InputHandler.IsInputReadable())
                    {
                        if(!mod.pickColorMode && InputHandler.GetPressedOr(mod.settings.pickColor1, mod.settings.pickColor2))
                        {
                            mod.SendToServer_ColorPickMode(true);
                            return;
                        }

                        if(InputHandler.GetPressedOr(mod.settings.replaceMode1, mod.settings.replaceMode2))
                        {
                            if(!mod.replaceAllModeInputPressed)
                            {
                                mod.replaceAllModeInputPressed = true;

                                if(MyAPIGateway.Session.CreativeMode)
                                {
                                    mod.replaceAllMode = !mod.replaceAllMode;
                                    MyAPIGateway.Utilities.ShowNotification("Replace color mode " + (mod.replaceAllMode ? "enabled." : "turned off."), 2000, MyFontEnum.White);
                                    return;
                                }
                                else
                                {
                                    MyAPIGateway.Utilities.ShowNotification("Replace color mode is only available in creative.", 3000, MyFontEnum.Red);
                                }
                            }
                        }
                        else if(mod.replaceAllModeInputPressed)
                        {
                            mod.replaceAllModeInputPressed = false;
                        }
                    }
                }

                bool colorPickMode = mod.playersColorPickMode.Contains(heldById);

                // update tool color
                if(!colorPickMode)
                {
                    PlayerColorData cd;

                    if(mod.playerColorData.TryGetValue(heldById, out cd))
                        SetToolColor(cd.colors[cd.selectedSlot]);
                }

                if(!colorPickMode && cooldown > 0)
                {
                    if(cooldown + DELAY_ACTION_SHOOT_COOLDOWN > ticks)
                        colorPickMode = true;
                    else
                        cooldown = 0;
                }

                if(!colorPickMode)
                {
                    if(mod.settings.sprayParticles)
                    {
                        var matrix = Entity.WorldMatrix;
                        var pos = matrix.Translation + matrix.Up * 0.06525 + matrix.Forward * 0.17; // paint gun's muzzle
                        var camera = MyAPIGateway.Session.Camera;

                        if(camera != null && Vector3D.DistanceSquared(camera.WorldMatrix.Translation, pos) <= PARTICLE_MAX_DISTANCE_SQ)
                        {
                            if(trigger)
                            {
                                var p = new Particle();
                                p.velocity = matrix.Forward * 0.5;
                                p.life = 30;
                                p.color = PaintGunMod.HSVtoRGB(color);
                                p.radius = 0.01f;
                                p.angle = (float)(rand.Next(2) == 0 ? -rand.NextDouble() : rand.NextDouble()) * 45;
                                particles.Add(p);
                            }

                            if(particles.Count > 0)
                            {
                                for(int i = particles.Count - 1; i >= 0; i--)
                                {
                                    var p = particles[i];

                                    if(--p.life <= 0 || p.color.A <= 0)
                                    {
                                        particles.RemoveAt(i);
                                        continue;
                                    }

                                    MyTransparentGeometry.AddPointBillboard("Smoke", p.color, pos + p.relativePosition, p.radius, p.angle, 0, true, true, false, -1);

                                    if(p.angle > 0)
                                        p.angle += (p.life * 0.001f);
                                    else
                                        p.angle -= (p.life * 0.001f);

                                    p.relativePosition += p.velocity / 60.0f;
                                    p.radius *= 1.35f;

                                    if(p.life <= 20)
                                        p.color *= 0.7f;

                                    p.velocity *= 1.3f;
                                }
                            }
                        }
                        else
                        {
                            if(particles.Count > 0)
                                particles.Clear();
                        }
                    }
                    else
                    {
                        if(particles.Count > 0)
                            particles.Clear();
                    }
                }

                if(++skip > SKIP_UPDATES)
                {
                    skip = 0;

                    if(trigger && !colorPickMode && soundEmitter != null && !soundEmitter.IsPlaying)
                    {
                        soundEmitter.CustomVolume = mod.settings.spraySoundVolume;
                        soundEmitter.PlaySound(soundPair, true);
                    }

                    if((!trigger || colorPickMode) && soundEmitter != null && soundEmitter.IsPlaying)
                    {
                        soundEmitter.StopSound(false);
                    }

                    if(heldByLocalPlayer)
                    {
                        mod.symmetryInput = false;
                        mod.selectedSlimBlock = null;
                        mod.selectedCharacter = null;
                        mod.selectedInvalid = false;
                        bool painted = mod.HoldingTool(trigger);

                        if(!mod.pickColorMode && painted && !MyAPIGateway.Session.CreativeMode) // expend the ammo manually when painting
                        {
                            if(MyAPIGateway.Multiplayer.IsServer)
                            {
                                var inv = (player as MyEntity).GetInventory(0) as IMyInventory;

                                if(inv != null)
                                    inv.RemoveItemsOfType((MyFixedPoint)1, PaintGunMod.PAINT_MAG, false);
                            }
                            else
                            {
                                mod.SendToServer_Ammo(player.EntityId, false);
                            }
                        }
                    }
                }

                if(shootTime > lastShotTime)
                {
                    lastShotTime = shootTime;

                    if(heldByLocalPlayer && !MyAPIGateway.Session.CreativeMode) // always add the shot ammo back
                    {
                        if(MyAPIGateway.Multiplayer.IsServer)
                        {
                            var inv = (player as MyEntity).GetInventory(0) as IMyInventory;

                            if(inv != null)
                                inv.AddItems((MyFixedPoint)1, PaintGunMod.PAINT_MAG);
                        }
                        else
                        {
                            mod.SendToServer_Ammo(player.EntityId, true);
                        }
                    }
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override void Close()
        {
            try
            {
                if(mod == null)
                    return;

                mod.holdingTools.Remove(heldById);

                if(heldByLocalPlayer)
                {
                    mod.localHeldTool = null;
                    mod.symmetryInput = false;
                    mod.selectedSlimBlock = null;
                    mod.selectedCharacter = null;
                    mod.selectedInvalid = false;

                    if(mod.pickColorMode)
                    {
                        mod.SendToServer_ColorPickMode(false);

                        mod.SetToolStatus(0, "Color picking cancelled.", MyFontEnum.Red, 1000);
                        mod.SetToolStatus(1, null);
                        mod.SetToolStatus(2, null);
                        mod.SetToolStatus(3, null);

                        PaintGunMod.PlaySound("HudUnable", 0.5f);
                    }
                    else if(mod.replaceAllMode)
                    {
                        mod.SetToolStatus(0, "Replace color mode turned off.", MyFontEnum.White, 2000);
                        mod.SetToolStatus(1, null);
                        mod.SetToolStatus(2, null);
                        mod.SetToolStatus(3, null);

                        mod.replaceAllMode = false;
                    }
                    else if(mod.toolStatus != null)
                    {
                        mod.SetToolStatus(0, null);
                        mod.SetToolStatus(1, null);
                        mod.SetToolStatus(2, null);
                        mod.SetToolStatus(3, null);
                    }

                    PaintGunMod.SetCrosshairColor(null);
                }

                if(soundEmitter != null)
                {
                    if(soundEmitter.IsPlaying)
                        soundEmitter.StopSound(false, true);
                    else
                        soundEmitter.Cleanup();

                    soundEmitter = null;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        public override MyObjectBuilder_EntityBase GetObjectBuilder(bool copy = false)
        {
            return Entity.GetObjectBuilder(copy);
        }
    }
}