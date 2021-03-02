using System;
using System.Collections.Generic;
using Digi.ComponentLib;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Weapons;
using VRage.Collections;
using VRage.ModAPI;

namespace Digi.PaintGun.Features.Tool
{
    /// <summary>
    /// Handles all held paint guns, including other players'.
    /// </summary>
    public class ToolHandler : ModComponent
    {
        public delegate void ToolEventDelegate(PaintGunItem item);
        public event ToolEventDelegate ToolSpawned;
        public event ToolEventDelegate ToolRemoved;

        public List<PaintGunItem> Tools = new List<PaintGunItem>();

        MyConcurrentPool<PaintGunItem> itemPool = new MyConcurrentPool<PaintGunItem>(defaultCapacity: 1, activator: () => new PaintGunItem(), clear: (i) => i.Clear());
        MyConcurrentPool<Particle> particlePool = new MyConcurrentPool<Particle>(defaultCapacity: 0, activator: () => new Particle(), clear: (p) => p.Clear());
        List<IMyAutomaticRifleGun> spawnedRifles = new List<IMyAutomaticRifleGun>();

        const UpdateFlags UPDATE_METHODS = (UpdateFlags.UPDATE_AFTER_SIM | UpdateFlags.UPDATE_DRAW);

        public ToolHandler(PaintGunMod main) : base(main)
        {
            if(Main.IsDedicatedServer)
                throw new Exception($"Why's this called in DS?");

            MyAPIGateway.Entities.OnEntityAdd += EntityAdded;
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
            MyAPIGateway.Entities.OnEntityAdd -= EntityAdded;

            foreach(var tool in Tools)
            {
                tool.Unload();
            }
        }

        public PaintGunItem GetToolHeldBy(ulong steamId)
        {
            for(int i = Tools.Count - 1; i >= 0; --i)
            {
                var tool = Tools[i];

                if(tool.OwnerSteamId == steamId)
                    return tool;
            }

            return null;
        }

        public Particle GetPooledParticle()
        {
            return particlePool.Get();
        }

        public void ReturnParticleToPool(Particle particle)
        {
            particlePool.Return(particle);
        }

        void EntityAdded(IMyEntity entity)
        {
            try
            {
                var rifle = entity as IMyAutomaticRifleGun;

                if(rifle != null && rifle.DefinitionId.SubtypeName == Constants.PAINTGUN_ID)
                {
                    if(spawnedRifles.Contains(rifle))
                        return;

                    spawnedRifles.Add(rifle);
                    SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        protected override void UpdateAfterSim(int tick)
        {
            if(spawnedRifles.Count > 0)
            {
                for(int i = spawnedRifles.Count - 1; i >= 0; --i)
                {
                    var rifle = spawnedRifles[i];

                    if(!rifle.MarkedForClose && rifle.Owner != null)
                        AddTool(rifle);
                }

                spawnedRifles.Clear();

                if(Tools.Count == 0)
                    SetUpdateMethods(UPDATE_METHODS, false);
            }

            for(int i = Tools.Count - 1; i >= 0; --i)
            {
                var item = Tools[i];

                if(!item.UpdateSimulation())
                {
                    RemoveTool(item, i);
                    continue;
                }
            }
        }

        protected override void UpdateDraw()
        {
            // particle draw needed in Draw() method because they skew weirdly at high character velocity.

            for(int i = Tools.Count - 1; i >= 0; --i)
            {
                Tools[i].UpdateDraw();
            }
        }

        // FIXME: equipping tool with gamepad by pressing dpad-down multiple times makes subsequent equips undetectable by script.
        void AddTool(IMyAutomaticRifleGun rifle)
        {
            var item = itemPool.Get();

            if(!item.Init(rifle))
            {
                itemPool.Return(item);
                return;
            }

            Tools.Add(item);
            ToolSpawned?.Invoke(item);

            SetUpdateMethods(UPDATE_METHODS, true);
        }

        void RemoveTool(PaintGunItem item, int index)
        {
            ToolRemoved?.Invoke(item);

            Tools.RemoveAtFast(index);
            itemPool.Return(item);

            if(Tools.Count == 0)
            {
                SetUpdateMethods(UpdateFlags.UPDATE_DRAW, false);

                if(spawnedRifles.Count == 0)
                    SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, false);
            }
        }
    }
}