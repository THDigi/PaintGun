using System;
using System.Collections.Generic;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage;
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

        private PaintMaterial? _paintPreviewMaterial;
        public PaintMaterial? PaintPreviewMaterial
        {
            get { return _paintPreviewMaterial; }
            set
            {
                _paintPreviewMaterial = value;

                if(_paintPreviewMaterial.HasValue)
                    OwnerPaletteUpdate(OwnerInfo);
            }
        }

        public ulong OwnerSteamId { get; private set; }
        public bool OwnerIsLocalPlayer { get; private set; } = false;
        public PlayerInfo OwnerInfo { get; private set; }

        int SprayCooldown;
        Color ParticleColor;
        MyEntitySubpart MagazineSubpart;

        int UpdatePaintCanAtTick;
        const int PaintCanUpdateWaitTicks = 6;

        readonly PaintGunMod Main;
        readonly List<Particle> Particles = new List<Particle>(30);
        readonly SpraySoundEmitter SoundEmitter;

        public static readonly MySoundPair SpraySound = new MySoundPair("PaintGunSpray");
        static readonly MyStringId SprayMaterial = MyStringId.GetOrCompute("PaintGun_Spray");
        const BlendTypeEnum SprayBlendType = BlendTypeEnum.SDR;

        public const int SprayCooldownColorpicker = Constants.TICKS_PER_SECOND * 1;
        const int SprayMaxViewDistSq = 100 * 100;

        public PaintGunItem()
        {
            Main = PaintGunMod.Instance;

            if(Main.IsDedicatedServer)
                throw new ArgumentException($"{GetType().Name} got created on DS side, not designed for that.");

            SoundEmitter = new SpraySoundEmitter(GetSpraySoundPosition, IsHoldingPaintGun);
        }

        public void Unload()
        {
            SoundEmitter.Dispose();
            Clear(false);
            Particles.Clear();
        }

        /// <summary>
        /// Called when object is being retrieved from the pool.
        /// </summary>
        public bool Init(IMyAutomaticRifleGun entity)
        {
            if(entity == null)
                throw new ArgumentException($"{GetType().Name} :: got created with null entity!");

            Rifle = entity;

            if(!Rifle.TryGetSubpart("magazine", out MagazineSubpart))
                throw new Exception($"PaintGun model doesn't have the expected magazine subpart, restarting game or re-subscribing to mod could fix.");

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
                Log.Error($"Can't find steamId for holder of PaintGun entity! OwnerIdentityId={Rifle.OwnerIdentityId.ToString()} entId={Rifle.EntityId.ToString()}", Log.PRINT_MESSAGE);
                return false;
            }

            if(Main.Palette == null)
                throw new NullReferenceException($"{GetType().Name} :: Palette == null");

            OwnerInfo = Main.Palette.GetOrAddPlayerInfo(OwnerSteamId);

            if(OwnerInfo == null)
                throw new NullReferenceException($"{GetType().Name} :: OwnerInfo == null");

            OwnerInfo.ColorSlotSelected += OwnerColorSlotSelected;
            OwnerInfo.ColorListChanged += OwnerColorListChanged;
            OwnerInfo.SkinSelected += OwnerSkinSelected;
            OwnerInfo.ApplyColorChanged += OwnerPaletteUpdate;
            OwnerInfo.ApplySkinChanged += OwnerPaletteUpdate;
            OwnerInfo.ColorPickModeChanged += OwnerColorPickModeChanged;

            UpdatePaintCanMaterial();
            return true;
        }

        /// <summary>
        /// Called by pool when returned to it.
        /// </summary>
        public void Clear(bool returnParticles = true)
        {
            Rifle = null;
            MagazineSubpart = null;
            Spraying = false;
            SprayCooldown = 0;
            Ammo = 0;
            JustInitialized = false;
            SoundEmitter.Stop();
            OwnerSteamId = 0;
            OwnerIsLocalPlayer = false;
            PaintPreviewMaterial = null;

            if(OwnerInfo != null)
            {
                OwnerInfo.ColorSlotSelected -= OwnerColorSlotSelected;
                OwnerInfo.ColorListChanged -= OwnerColorListChanged;
                OwnerInfo.SkinSelected -= OwnerSkinSelected;
                OwnerInfo.ApplyColorChanged -= OwnerPaletteUpdate;
                OwnerInfo.ApplySkinChanged -= OwnerPaletteUpdate;
                OwnerInfo.ColorPickModeChanged -= OwnerColorPickModeChanged;
                OwnerInfo = null;
            }

            ClearParticles();
        }

        bool IsHoldingPaintGun()
        {
            if(!OwnerIsLocalPlayer)
                return false;

            IMyCharacter chr = MyAPIGateway.Session.ControlledObject as IMyCharacter;
            return (chr != null && chr.EquippedTool == Rifle);
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

            if(MagazineSubpart != null)
            {
                bool VisibleMag = (Ammo > 0 || Main.IgnoreAmmoConsumption);
                if(MagazineSubpart.Render.Visible != VisibleMag)
                {
                    MagazineSubpart.Render.Visible = VisibleMag;
                }
            }

            if(SprayCooldown > 0)
                SprayCooldown--;

            SoundEmitter.PlaySpray = (Spraying && SprayCooldown == 0 && !OwnerInfo.ColorPickMode);
            SoundEmitter.Update(Main.Settings.spraySoundVolume);

            if(UpdatePaintCanAtTick == Main.Tick)
            {
                UpdatePaintCanAtTick = 0;
                UpdatePaintCanMaterial();
            }

            return true;
        }

        Vector3D GetSpraySoundPosition()
        {
            IMyCharacter owner = Rifle.Owner as IMyCharacter;
            if(owner != null)
            {
                MatrixD headMatrix = owner.GetHeadMatrix(true, true);
                Vector3D soundPos = headMatrix.Translation + headMatrix.Down * 0.1 + headMatrix.Forward * 0.6;

                // HACK: sound somehow plays ahead of position, adjusted by trial and error
                if(owner.Physics != null)
                    soundPos -= owner.Physics.LinearVelocity / Constants.TICKS_PER_SECOND; // remove 1 tick worth of velocity

                return soundPos;
            }
            else
            {
                MatrixD muzzleMatrix = Rifle.GunBase.GetMuzzleWorldMatrix();
                return muzzleMatrix.Translation + muzzleMatrix.Forward * 0.2;
            }
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
                particle.Init(matrix, ParticleColor);
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

        void OwnerColorPickModeChanged(PlayerInfo pi)
        {
            SprayCooldown = SprayCooldownColorpicker;
        }

        void OwnerColorListChanged(PlayerInfo pi, int? index)
        {
            ScheduleCanMaterialUpdate();
        }

        void OwnerColorSlotSelected(PlayerInfo pi, int prevIndex, int newIndex)
        {
            ScheduleCanMaterialUpdate();
        }

        void OwnerSkinSelected(PlayerInfo pi, MyStringHash prevSkin, MyStringHash newSkin)
        {
            ScheduleCanMaterialUpdate();
        }

        void OwnerPaletteUpdate(PlayerInfo pi)
        {
            ScheduleCanMaterialUpdate();
        }

        void ScheduleCanMaterialUpdate()
        {
            UpdatePaintCanAtTick = Main.Tick + PaintCanUpdateWaitTicks;
        }

        void UpdatePaintCanMaterial()
        {
            PaintMaterial material = PaintPreviewMaterial ?? OwnerInfo.GetPaintMaterial();

            MyDefinitionManager.MyAssetModifiers skinRender = default(MyDefinitionManager.MyAssetModifiers);
            Vector3? skinColorOverride = null;

            if(material.Skin.HasValue)
            {
                skinRender = MyDefinitionManager.Static.GetAssetModifierDefinitionForRender(material.Skin.Value);

                MyAssetModifierDefinition skinDef = MyDefinitionManager.Static.GetAssetModifierDefinition(new MyDefinitionId(typeof(MyObjectBuilder_AssetModifierDefinition), material.Skin.Value));
                if(skinDef != null && skinDef.DefaultColor.HasValue)
                {
                    skinColorOverride = skinDef.DefaultColor.Value.ColorToHSVDX11();
                }
            }

            Vector3 colorMask = skinColorOverride ?? material.ColorMask ?? Main.Palette.DefaultColorMask;

            IMyEntity paintEntity = (MagazineSubpart as IMyEntity) ?? Rifle;

            paintEntity.Render.MetalnessColorable = skinRender.MetalnessColorable;
            paintEntity.Render.TextureChanges = skinRender.SkinTextureChanges;

            // required to properly update skin (like glamour not being recolorable
            paintEntity.Render.RemoveRenderObjects();
            paintEntity.Render.AddRenderObjects();

            if(!paintEntity.Render.EnableColorMaskHsv)
                paintEntity.Render.EnableColorMaskHsv = true;

            paintEntity.Render.ColorMaskHsv = colorMask;

            ParticleColor = Utils.ColorMaskToRGB(colorMask);
        }
    }
}