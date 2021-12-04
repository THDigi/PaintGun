﻿using System;
using System.Collections.Generic;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;

namespace Digi.PaintGun.Features.Tool
{
    public class PaintGunItem
    {
        public IMyAutomaticRifleGun Rifle { get; private set; }
        public bool Spraying { get; set; }
        public int Ammo { get; private set; }

        /// <summary>
        /// Used for outside event triggering, only true for a really short time.
        /// Use <see cref="OwnerSteamId"/> == 0 as "initialized" check.
        /// </summary>
        public bool JustInitialized { get; private set; }

        public Color PaintColorRGB { get; private set; }

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

        public ulong OwnerSteamId { get; private set; }
        public bool OwnerIsLocalPlayer { get; private set; } = false;
        public PlayerInfo OwnerInfo { get; private set; }

        int SprayCooldown;
        readonly PaintGunMod Main;
        readonly List<Particle> Particles = new List<Particle>(30);
        readonly MyEntity3DSoundEmitter SoundEmitter = new MyEntity3DSoundEmitter(null);

        static readonly MySoundPair SpraySound = new MySoundPair("PaintGunSpray");
        static readonly MyStringId SprayMaterial = MyStringId.GetOrCompute("PaintGun_Spray");
        const BlendTypeEnum SprayBlendType = BlendTypeEnum.SDR;

        public const int SprayCooldownColorpicker = Constants.TICKS_PER_SECOND * 1;
        const int SprayMaxViewDistSq = 100 * 100;

        public PaintGunItem()
        {
            Main = PaintGunMod.Instance;

            if(Main.IsDedicatedServer)
                throw new ArgumentException($"{GetType().Name} got created on DS side, not designed for that.");

            Main.Settings.SettingsLoaded += UpdateSprayVolume;

            //SoundEmitter.CustomMaxDistance = 30f;

            // remove all 2D forcing conditions
            SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ShouldPlay2D].ClearImmediate();

            if(MyAPIGateway.Session.SessionSettings.RealisticSound)
            {
                // remove some unnecessary conditions
                foreach(ConcurrentCachingList<Delegate> funcList in SoundEmitter.EmitterMethods.Values)
                {
                    foreach(Delegate func in funcList)
                    {
                        switch(func.Method.Name)
                        {
                            case "IsCurrentWeapon":
                            case "IsControlledEntity":
                            case "IsOnSameGrid":
                                funcList.Remove(func);
                                break;
                        }
                    }
                }

                // custom IsCurrentWeapon because Entity is not set for this emitter to detect on its own
                SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CanHear].Add(new Func<bool>(EmitterCanHear));

                //foreach(KeyValuePair<int, ConcurrentCachingList<Delegate>> kv in SoundEmitter.EmitterMethods)
                //{
                //    kv.Value.ApplyChanges();
                //    foreach(Delegate func in kv.Value)
                //    {
                //        Log.Info($"{((MyEntity3DSoundEmitter.MethodsEnum)kv.Key)} {func.Method.Name}");
                //    }
                //}
            }
        }

        public void Unload()
        {
            SoundEmitter.Cleanup();
            Clear(false);
            Particles.Clear();

            if(Main?.Settings != null)
                Main.Settings.SettingsLoaded -= UpdateSprayVolume;
        }

        /// <summary>
        /// Called when object is being retrieved from the pool.
        /// </summary>
        public bool Init(IMyAutomaticRifleGun entity)
        {
            if(entity == null)
                throw new ArgumentException($"{GetType().Name} :: got created with null entity!");

            if(SoundEmitter == null)
                throw new NullReferenceException($"{GetType().Name} :: soundEmitter is null");

            Rifle = entity;
            //SoundEmitter.Entity = (MyEntity)Rifle;
            UpdateSprayVolume();

            if(Rifle.GunBase == null)
                throw new NullReferenceException($"{GetType().Name} :: Rifle.GunBase == null; ent={Rifle}; owner={Rifle.Owner}/{Rifle.OwnerIdentityId.ToString()}");

            // HACK: prevent tool from ever reloading, which breaks animations for other things
            if(Rifle.GunBase.CurrentAmmo <= 0)
                Rifle.GunBase.CurrentAmmo = 1;

            if(Rifle.Owner == null)
            {
                Log.Error($"Can't find holder of a PaintGun entity because it's null! OwnerIdentityId={Rifle.OwnerIdentityId.ToString()} entId={Rifle.EntityId.ToString()}", Log.PRINT_MESSAGE);
                return false;
            }

            if(MyAPIGateway.Multiplayer == null)
                throw new NullReferenceException($"{GetType().Name} :: MyAPIGateway.Multiplayer == null");

            if(MyAPIGateway.Session == null)
                throw new NullReferenceException($"{GetType().Name} :: MyAPIGateway.Session == null");

            IMyCharacter charEnt = MyAPIGateway.Session.ControlledObject as IMyCharacter;

            if(charEnt != null && charEnt == Rifle.Owner)
            {
                OwnerSteamId = MyAPIGateway.Multiplayer.MyId;
                OwnerIsLocalPlayer = true;
            }

            if(OwnerSteamId == 0)
            {
                List<IMyPlayer> players = Main.Caches?.Players.Get();

                if(players == null)
                    throw new NullReferenceException($"{GetType().Name} :: players cache is null");

                if(MyAPIGateway.Players == null)
                    throw new NullReferenceException($"{GetType().Name} :: MyAPIGateway.Players == null");

                MyAPIGateway.Players.GetPlayers(players);

                foreach(IMyPlayer player in players)
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

            OwnerPaletteUpdated(OwnerInfo);
            OwnerInfo.OnColorSlotSelected += OwnerColorSlotSelected;
            OwnerInfo.OnColorListChanged += OwnerColorListChanged;
            OwnerInfo.OnApplyColorChanged += OwnerPaletteUpdated;
            OwnerInfo.OnApplySkinChanged += OwnerPaletteUpdated;
            OwnerInfo.OnColorPickModeChanged += OwnerColorPickModeChanged;
            return true;
        }

        /// <summary>
        /// Called by pool when returned to it.
        /// </summary>
        public void Clear(bool returnParticles = true)
        {
            Rifle = null;
            Spraying = false;
            SprayCooldown = 0;
            Ammo = 0;
            JustInitialized = false;
            SoundEmitter.StopSound(true);
            SoundEmitter.Entity = null;
            OwnerSteamId = 0;
            OwnerIsLocalPlayer = false;
            PaintPreviewColorRGB = null;

            if(OwnerInfo != null)
            {
                OwnerInfo.OnColorSlotSelected -= OwnerColorSlotSelected;
                OwnerInfo.OnColorListChanged -= OwnerColorListChanged;
                OwnerInfo.OnApplyColorChanged -= OwnerPaletteUpdated;
                OwnerInfo.OnApplySkinChanged -= OwnerPaletteUpdated;
                OwnerInfo.OnColorPickModeChanged -= OwnerColorPickModeChanged;
                OwnerInfo = null;
            }

            ClearParticles();
        }

        bool EmitterCanHear()
        {
            if(!OwnerIsLocalPlayer)
                return false;

            IMyCharacter chr = MyAPIGateway.Session.ControlledObject as IMyCharacter;
            return (chr != null && chr.EquippedTool == Rifle);
        }

        void UpdateSprayVolume()
        {
            SoundEmitter.CustomVolume = Main.Settings.spraySoundVolume;
        }

        public bool UpdateSimulation()
        {
            if(Rifle == null || Rifle.MarkedForClose)
                return false;

            int updateRate = (OwnerIsLocalPlayer ? LocalToolHandler.PAINT_UPDATE_TICKS : Constants.TICKS_PER_SECOND);
            if(Main.Tick % updateRate == 0)
            {
                MyFixedPoint? amount = Rifle?.Owner?.GetInventory()?.GetItemAmount(Main.Constants.PAINT_MAG_ID);
                Ammo = (amount.HasValue ? (int)amount.Value : 0);
            }

            if(SoundEmitter == null)
            {
                Log.Error($"{GetType().Name} :: SoundEmitter for PaintGunItem entId={Rifle.EntityId.ToString()} is null for some reason.", Log.PRINT_MESSAGE);
                return false;
            }

            if(SprayCooldown == 0)
            {
                if(Main.Tick % 10 == 0)
                {
                    SoundEmitter.Update();
                }

                if(Spraying && !OwnerInfo.ColorPickMode)
                {
                    Vector3D soundPos;

                    IMyCharacter owner = Rifle.Owner as IMyCharacter;
                    if(owner != null)
                    {
                        MatrixD headMatrix = owner.GetHeadMatrix(true, true);
                        soundPos = headMatrix.Translation + headMatrix.Down * 0.1 + headMatrix.Forward * 0.6;

                        // HACK: sound somehow plays ahead of position, adjusted by trial and error
                        if(owner.Physics != null)
                            soundPos -= owner.Physics.LinearVelocity / Constants.TICKS_PER_SECOND; // remove 1 tick worth of velocity
                    }
                    else
                    {
                        MatrixD muzzleMatrix = Rifle.GunBase.GetMuzzleWorldMatrix();
                        soundPos = muzzleMatrix.Translation + muzzleMatrix.Forward * 0.2;
                    }

                    //MyTransparentGeometry.AddPointBillboard(MyStringId.GetOrCompute("WhiteDot"), Color.Red, soundPos, 0.01f, 0, blendType: BlendTypeEnum.AdditiveTop);
                    SoundEmitter.SetPosition(soundPos);

                    if(SoundEmitter.CustomVolume != Main.Settings.spraySoundVolume)
                    {
                        SoundEmitter.CustomVolume = Main.Settings.spraySoundVolume;
                    }

                    if(!SoundEmitter.IsPlaying)
                    {
                        SoundEmitter.PlaySound(SpraySound, stopPrevious: true, skipIntro: true, force2D: false);
                    }
                }

                if((!Spraying || OwnerInfo.ColorPickMode) && SoundEmitter.IsPlaying)
                {
                    SoundEmitter.StopSound(false);
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

            if(!OwnerInfo.ColorPickMode && !valid && Particles.Count > 0)
            {
                ClearParticles();
            }
        }

        bool UpdateParticles(bool spawn)
        {
            if(!Main.Settings.sprayParticles)
                return false;

            IMyCamera camera = MyAPIGateway.Session?.Camera;
            if(camera == null)
                return false;

            MatrixD matrix = Rifle.WorldMatrix;

            if(Vector3D.DistanceSquared(camera.WorldMatrix.Translation, matrix.Translation) > SprayMaxViewDistSq)
                return false;

            bool paused = Main.IsPaused;

            if(!paused && spawn)
            {
                Particle particle = Main.ToolHandler.GetPooledParticle();
                particle.Init(matrix, PaintColorRGB);
                Particles.Add(particle);
            }

            if(Particles.Count > 0)
            {
                //const double NozzlePosX = 0.06525;
                //const double NozzlePosZ = 0.16;
                //Vector3D muzzleWorldPos = matrix.Translation + matrix.Up * NozzlePosX + matrix.Forward * NozzlePosZ;
                Vector3D muzzleWorldPos = Rifle.GetMuzzlePosition();

                for(int i = Particles.Count - 1; i >= 0; i--)
                {
                    Particle p = Particles[i];

                    MyTransparentGeometry.AddPointBillboard(SprayMaterial, p.Color, muzzleWorldPos + p.RelativePosition, p.Radius, p.Angle, blendType: SprayBlendType);

                    if(!paused)
                    {
                        if(--p.Life <= 0 || p.Color.A <= 0)
                        {
                            Particles.RemoveAtFast(i);
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
            foreach(Particle particle in Particles)
            {
                Main.ToolHandler.ReturnParticleToPool(particle);
            }

            Particles.Clear();
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

        void OwnerColorPickModeChanged(PlayerInfo pi)
        {
            SprayCooldown = SprayCooldownColorpicker;
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