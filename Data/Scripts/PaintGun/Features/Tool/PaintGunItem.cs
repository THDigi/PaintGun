using System;
using System.Collections.Generic;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace Digi.PaintGun.Features.Tool
{
    public class PaintGunItem
    {
        public IMyAutomaticRifleGun Rifle;
        public bool Spraying;
        public int SprayCooldown;
        public int Ammo => Rifle.CurrentAmmunition;

        /// <summary>
        /// Used for outside event triggering, only true for a really short time.
        /// Use <see cref="OwnerSteamId"/> == 0 as "initialized" check.
        /// </summary>
        public bool JustInitialized;

        public Color PaintColorRGB;

        private Color? _paintPreviewColorRGB;
        public Color? PaintPreviewColorRGB
        {
            get { return _paintPreviewColorRGB; }
            set
            {
                _paintPreviewColorRGB = value;

                if(_paintPreviewColorRGB.HasValue)
                    SetPaintColorRGB(_paintPreviewColorRGB.Value);
            }
        }

        public ulong OwnerSteamId;
        public bool OwnerIsLocalPlayer = false;
        public PlayerInfo OwnerInfo;

        readonly List<Particle> particles = new List<Particle>(30);
        readonly MyEntity3DSoundEmitter soundEmitter = new MyEntity3DSoundEmitter(null)
        {
            CustomMaxDistance = 30f,
        };

        PaintGunMod Main => PaintGunMod.Instance;

        readonly MySoundPair SPRAY_SOUND = new MySoundPair("PaintGunSpray");
        readonly MyStringId SPRAY_MATERIAL = MyStringId.GetOrCompute("PaintGun_Spray");
        const BlendTypeEnum SPRAY_BLEND_TYPE = BlendTypeEnum.SDR;

        public const int SPRAY_COOLDOWN_COLORPICKMODE = Constants.TICKS_PER_SECOND * 1;
        const int SPRAY_MAX_VIEW_DISTANCE_SQ = 100 * 100;
        const double NOZZLE_POSITION_Y = 0.06525;
        const double NOZZLE_POSITION_Z = 0.16;

        public PaintGunItem()
        {
            if(Main.IsDedicatedServer)
                throw new ArgumentException($"{GetType().Name} got created on DS side, not designed for that.");

            Main.Settings.SettingsLoaded += UpdateSprayVolume;
        }

        /// <summary>
        /// Called when object is being retrieved from the pool.
        /// </summary>
        public bool Init(IMyAutomaticRifleGun entity)
        {
            if(entity == null)
                throw new ArgumentException($"{GetType().Name} :: got created with null entity!");

            if(soundEmitter == null)
                throw new NullReferenceException($"{GetType().Name} :: soundEmitter is null");

            Rifle = entity;
            soundEmitter.Entity = (MyEntity)Rifle;
            UpdateSprayVolume();

            if(Rifle.Owner == null)
            {
                Log.Error($"Can't find holder of a PaintGun entity because it's null! OwnerIdentityId={Rifle.OwnerIdentityId.ToString()} entId={Rifle.EntityId.ToString()}", Log.PRINT_MESSAGE);
                return false;
            }

            if(MyAPIGateway.Multiplayer == null)
                throw new NullReferenceException($"{GetType().Name} :: MyAPIGateway.Multiplayer == null");

            if(MyAPIGateway.Session == null)
                throw new NullReferenceException($"{GetType().Name} :: MyAPIGateway.Session == null");

            var charEnt = MyAPIGateway.Session.ControlledObject as IMyCharacter;

            if(charEnt != null && charEnt == Rifle.Owner)
            {
                OwnerSteamId = MyAPIGateway.Multiplayer.MyId;
                OwnerIsLocalPlayer = true;
            }

            if(OwnerSteamId == 0)
            {
                var players = Main.Caches?.Players.Get();

                if(players == null)
                    throw new NullReferenceException($"{GetType().Name} :: players cache is null");

                if(MyAPIGateway.Players == null)
                    throw new NullReferenceException($"{GetType().Name} :: MyAPIGateway.Players == null");

                MyAPIGateway.Players.GetPlayers(players);

                foreach(var player in players)
                {
                    if(player?.Character != null && player.Character.EntityId == Rifle.Owner.EntityId)
                    {
                        OwnerSteamId = player.SteamUserId;
                        break;
                    }
                }

                Main.Caches.Players.Return(players);
            }

            if(OwnerSteamId == 0)
            {
                Log.Error($"Can't find holder of a PaintGun entity! entId={Rifle.EntityId.ToString()}", Log.PRINT_MESSAGE);
                return false;
            }

            if(Main.Palette == null)
                throw new NullReferenceException($"{GetType().Name} :: Palette == null");

            OwnerInfo = Main.Palette.GetOrAddPlayerInfo(OwnerSteamId);

            if(OwnerInfo == null)
                throw new NullReferenceException($"{GetType().Name} :: OwnerInfo == null");

            OwnerInfo.OnColorSlotSelected += OwnerColorSlotSelected;
            OwnerInfo.OnColorListChanged += OwnerColorListChanged;
            OwnerInfo.OnApplyColorChanged += OwnerPaletteUpdated;
            OwnerInfo.OnApplySkinChanged += OwnerPaletteUpdated;
            return true;
        }

        void UpdateSprayVolume()
        {
            soundEmitter.CustomVolume = Main.Settings.spraySoundVolume;
        }

        /// <summary>
        /// Called by pool when returned to it.
        /// </summary>
        public void Clear(bool returnParticles = true)
        {
            Rifle = null;
            JustInitialized = false;
            soundEmitter.StopSound(false);
            soundEmitter.Entity = null;
            OwnerSteamId = 0;
            OwnerIsLocalPlayer = false;

            if(OwnerInfo != null)
            {
                OwnerInfo.OnColorSlotSelected -= OwnerColorSlotSelected;
                OwnerInfo.OnColorListChanged -= OwnerColorListChanged;
                OwnerInfo.OnApplyColorChanged -= OwnerPaletteUpdated;
                OwnerInfo.OnApplySkinChanged -= OwnerPaletteUpdated;
                OwnerInfo = null;
            }

            ClearParticles();
        }

        public void Unload()
        {
            soundEmitter.Cleanup();
            Clear(false);
            particles.Clear();

            Main.Settings.SettingsLoaded -= UpdateSprayVolume;
        }

        public bool UpdateSimulation()
        {
            if(Rifle == null || Rifle.MarkedForClose)
                return false;

            if(soundEmitter == null)
            {
                Log.Error($"{GetType().Name} :: SoundEmitter for PaintGunItem entId={Rifle.EntityId.ToString()} is null for some reason.", Log.PRINT_MESSAGE);
                return false;
            }

            if(SprayCooldown == 0)
            {
                bool force2D = (OwnerIsLocalPlayer && MyAPIGateway.Session.Player.Character.IsInFirstPersonView);

                if(OwnerIsLocalPlayer && soundEmitter.IsPlaying && soundEmitter.Force2D != force2D)
                {
                    soundEmitter.StopSound(false);
                }

                if(Spraying && !OwnerInfo.ColorPickMode && !soundEmitter.IsPlaying)
                {
                    soundEmitter.CustomVolume = (force2D ? Main.Settings.spraySoundVolume * 0.5f : Main.Settings.spraySoundVolume);
                    soundEmitter.Force2D = force2D;
                    soundEmitter.PlaySound(SPRAY_SOUND, force2D: force2D);
                }

                if((!Spraying || OwnerInfo.ColorPickMode) && soundEmitter.IsPlaying)
                {
                    soundEmitter.StopSound(false);
                }
            }
            else
            {
                SprayCooldown--;
            }

            return true;
        }

        public void UpdateDraw()
        {
            if(OwnerSteamId == 0)
                return;

            bool spawn = (Spraying && SprayCooldown == 0 && !OwnerInfo.ColorPickMode && (Main.IgnoreAmmoConsumption || Ammo > 0));
            bool valid = UpdateParticles(spawn);

            if(!OwnerInfo.ColorPickMode && !valid && particles.Count > 0)
            {
                ClearParticles();
            }
        }

        bool UpdateParticles(bool spawn)
        {
            if(!Main.Settings.sprayParticles)
                return false;

            var camera = MyAPIGateway.Session?.Camera;

            if(camera == null)
                return false;

            var matrix = Rifle.WorldMatrix;

            if(Vector3D.DistanceSquared(camera.WorldMatrix.Translation, matrix.Translation) > SPRAY_MAX_VIEW_DISTANCE_SQ)
                return false;

            var paused = Main.IsPaused;

            if(!paused && spawn)
            {
                var particle = Main.ToolHandler.GetPooledParticle();
                particle.Init(matrix, PaintColorRGB);
                particles.Add(particle);
            }

            if(particles.Count > 0)
            {
                var muzzleWorldPos = matrix.Translation + matrix.Up * NOZZLE_POSITION_Y + matrix.Forward * NOZZLE_POSITION_Z;

                for(int i = particles.Count - 1; i >= 0; i--)
                {
                    var p = particles[i];

                    MyTransparentGeometry.AddPointBillboard(SPRAY_MATERIAL, p.Color, muzzleWorldPos + p.RelativePosition, p.Radius, p.Angle, blendType: SPRAY_BLEND_TYPE);

                    if(!paused)
                    {
                        if(--p.Life <= 0 || p.Color.A <= 0)
                        {
                            particles.RemoveAtFast(i);
                            Main.ToolHandler.ReturnParticleToPool(p);
                            continue;
                        }

                        if(p.Angle > 0)
                            p.Angle += (p.Life * 0.001f);
                        else
                            p.Angle -= (p.Life * 0.001f);

                        p.RelativePosition += p.VelocityPerTick;
                        p.VelocityPerTick *= 1.3f;
                        p.Radius *= MyUtils.GetRandomFloat(1.25f, 1.45f);

                        if(p.Life <= 20)
                            p.Color *= 0.7f;
                    }
                }
            }

            return true;
        }

        void ClearParticles()
        {
            foreach(var particle in particles)
            {
                Main.ToolHandler.ReturnParticleToPool(particle);
            }

            particles.Clear();
        }

        void OwnerColorListChanged(PlayerInfo pi, int? index)
        {
            OwnerPaletteUpdated(pi);
        }

        void OwnerColorSlotSelected(PlayerInfo pi, int prevIndex, int newIndex)
        {
            OwnerPaletteUpdated(pi);
        }

        void OwnerPaletteUpdated(PlayerInfo pi)
        {
            if(PaintPreviewColorRGB.HasValue)
                return;

            if(pi.ApplyColor)
                SetPaintColorMask(pi.SelectedColorMask);
            else
                SetPaintColorMask(Main.Palette.DefaultColorMask);
        }

        void SetPaintColorRGB(Color colorRGB)
        {
            PaintColorRGB = colorRGB;
            Rifle.SetEmissiveParts("ColorLabel", colorRGB, 0);
        }

        void SetPaintColorMask(Vector3 colorMask)
        {
            PaintColorRGB = Utils.ColorMaskToRGB(colorMask);
            Rifle.SetEmissiveParts("ColorLabel", PaintColorRGB, 0);
        }
    }
}