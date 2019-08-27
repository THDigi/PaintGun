using System;
using System.Collections.Generic;
using Sandbox.Common.ObjectBuilders;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.ObjectBuilders;
using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun
{
    [MyEntityComponentDescriptor(typeof(MyObjectBuilder_AutomaticRifle), false, PaintGunMod.PAINTGUN_ID)]
    public class PaintGunItem : MyGameLogicComponent
    {
        public bool Firing = false;
        public int FireCooldown = 0;
        public int Ammo => rifle.CurrentAmmunition;

        private bool init = false;
        private IMyAutomaticRifleGun rifle;
        private ulong heldById = 0;
        private bool heldByLocalPlayer = false;
        private bool colorPickMode = false;
        private Color colorRGB;
        private MyEntity3DSoundEmitter soundEmitter;
        private readonly List<Particle> particles = new List<Particle>(20);

        private const int PARTICLE_MAX_DISTANCE_SQ = 1000 * 1000;
        private const long DELAY_SHOOT = (TimeSpan.TicksPerMillisecond * 200);
        private const long DELAY_ACTION_SHOOT_COOLDOWN = (TimeSpan.TicksPerMillisecond * 600);

        private PaintGunMod mod = null;

        public override void Init(MyObjectBuilder_EntityBase objectBuilder)
        {
            NeedsUpdate = MyEntityUpdateEnum.EACH_FRAME;
        }

        public void FirstUpdate()
        {
            rifle = (IMyAutomaticRifleGun)Entity;
            var owner = rifle.Owner as IMyCharacter;

            if(owner == null)
            {
                Log.Error("ERROR: Can't find holder of a paint gun entity because it's null or not a character; ent=" + rifle.Owner,
                          "Can't find holder of paint gun entity, please report the circumstances!");
                return;
            }

            var charEnt = MyAPIGateway.Session.ControlledObject as IMyCharacter;

            if(charEnt != null && charEnt.EntityId == owner.EntityId)
            {
                heldById = MyAPIGateway.Multiplayer.MyId;
                heldByLocalPlayer = true;
            }

            if(heldById == 0) // then find the owner through the player list
            {
                mod.players.Clear();
                MyAPIGateway.Players.GetPlayers(mod.players);

                foreach(var p in mod.players)
                {
                    if(p.Character == owner)
                    {
                        heldById = p.SteamUserId;
                        break;
                    }
                }

                mod.players.Clear();
            }

            if(heldById == 0)
            {
                Log.Error("ERROR: Can't find holder of a paint gun entity.",
                    "Can't find holder of paint gun entity, please report the circumstances!");
                return;
            }

            if(heldByLocalPlayer)
            {
                mod.localHeldTool = this;
            }

            if(!mod.isDS)
            {
                mod.ToolDraw.Add(this); // register for draw updates

                UpdateToolColor();

                if(soundEmitter == null)
                {
                    soundEmitter = new MyEntity3DSoundEmitter((MyEntity)Entity)
                    {
                        CustomMaxDistance = 30f,
                        CustomVolume = mod.settings.spraySoundVolume
                    };
                }
            }
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if(!init)
                {
                    if(mod == null)
                    {
                        mod = PaintGunMod.instance;
                        return;
                    }

                    if(!mod.init || mod.settings == null)
                        return;

                    init = true;
                    FirstUpdate();
                }

                if(mod.isDS || heldById == 0)
                    return;

                colorPickMode = mod.playersColorPickMode.Contains(heldById);

                // update tool color
                if(!colorPickMode && mod.tick % 10 == 0)
                {
                    UpdateToolColor();
                }

                if(FireCooldown == 0)
                {
                    if(soundEmitter != null)
                    {
                        bool force2D = (heldByLocalPlayer && MyAPIGateway.Session.Player.Character.IsInFirstPersonView);

                        if(heldByLocalPlayer && soundEmitter.IsPlaying && soundEmitter.Force2D != force2D)
                        {
                            soundEmitter.StopSound(false);
                        }

                        if(Firing && !colorPickMode && !soundEmitter.IsPlaying)
                        {
                            soundEmitter.CustomVolume = (force2D ? mod.settings.spraySoundVolume * 0.5f : mod.settings.spraySoundVolume);
                            soundEmitter.Force2D = force2D;
                            soundEmitter.PlaySound(mod.SPRAY_SOUND, force2D: force2D);
                        }

                        if((!Firing || colorPickMode) && soundEmitter.IsPlaying)
                        {
                            soundEmitter.StopSound(false);
                        }
                    }
                }
                else
                {
                    --FireCooldown;
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        void UpdateToolColor()
        {
            PlayerColorData cd;
            if(mod.playerColorData.TryGetValue(heldById, out cd))
            {
                SetToolColor(cd.ApplyColor ? cd.Colors[cd.SelectedSlot] : PaintGunMod.instance.DEFAULT_COLOR);
            }
        }

        public void Draw()
        {
            bool spawn = (!colorPickMode && Firing && FireCooldown == 0 && (mod.IgnoreAmmoConsumption || rifle.CurrentAmmunition > 0));
            bool valid = UpdateParticles(spawn);

            if(!colorPickMode && !valid && particles.Count > 0)
            {
                particles.Clear();
            }
        }

        bool UpdateParticles(bool spawn)
        {
            if(!mod.settings.sprayParticles)
                return false;

            var camera = MyAPIGateway.Session?.Camera;

            if(camera == null)
                return false;

            var matrix = Entity.WorldMatrix;

            if(Vector3D.DistanceSquared(camera.WorldMatrix.Translation, matrix.Translation) > PARTICLE_MAX_DISTANCE_SQ)
                return false;

            var paused = MyParticlesManager.Paused;

            if(!paused && spawn)
            {
                particles.Add(new Particle
                {
                    VelocityPerTick = (matrix.Forward * 0.5) / 60f,
                    Life = 30,
                    Color = colorRGB,
                    Radius = 0.01f,
                    Angle = MyUtils.GetRandomFloat(-1, 1) * 45
                });
            }

            if(particles.Count > 0)
            {
                var material = mod.MATERIAL_SPRAY;
                var muzzleWorldPos = matrix.Translation + matrix.Up * 0.06525 + matrix.Forward * 0.16;

                for(int i = particles.Count - 1; i >= 0; i--)
                {
                    var p = particles[i];

                    MyTransparentGeometry.AddPointBillboard(material, p.Color, muzzleWorldPos + p.RelativePosition, p.Radius, p.Angle, blendType: PaintGunMod.SPRAY_BLEND_TYPE);

                    if(!paused)
                    {
                        if(--p.Life <= 0 || p.Color.A <= 0)
                        {
                            particles.RemoveAtFast(i);
                            continue;
                        }

                        if(p.Angle > 0)
                            p.Angle += (p.Life * 0.001f);
                        else
                            p.Angle -= (p.Life * 0.001f);

                        p.RelativePosition += p.VelocityPerTick;
                        p.VelocityPerTick *= 1.3f;
                        p.Radius *= 1.35f;

                        if(p.Life <= 20)
                            p.Color *= 0.7f;
                    }
                }
            }

            return true;
        }

        public void SetToolColor(Vector3 colorMask)
        {
            colorRGB = PaintGunMod.ColorMaskToRGB(colorMask);
            Entity.SetEmissiveParts("ColorLabel", colorRGB, 0);
        }

        public override void Close()
        {
            try
            {
                if(mod == null)
                    return;

                mod.ToolDraw.Remove(this); // unregister from draw updates

                if(heldByLocalPlayer)
                    mod.ToolHolstered();

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
    }
}