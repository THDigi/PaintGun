﻿using System;
using VRage.Game;
using VRage.Game.Components;

namespace Digi.ComponentLib
{
    /// <summary>
    /// Component to tie component logic to game API.
    /// Intended as partial for easy configuration without modifying the lib.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.NoUpdate)]
    public partial class PaintGun_GameSession : MySessionComponentBase
    {
        IModBase main;
        public bool Paused;

        public override void LoadData()
        {
            try
            {
                LoadMod();
            }
            catch(Exception e)
            {
                Log.Error(e);
                UnloadData();
                throw new Exception("Error in mod loading, see above exceptions.");
            }
        }

        public override void BeforeStart()
        {
            try
            {
                main?.WorldStart();
            }
            catch(Exception e)
            {
                Log.Error(e);
                UnloadData();
                throw new Exception("Error in mod loading, see above exceptions.");
            }
        }

        protected override void UnloadData()
        {
            try
            {
                main?.WorldExit();
            }
            catch(Exception e)
            {
                Log.Error(e);
                throw new Exception("Error in mod unloading, see above exceptions.");
            }

            Log.Close();
        }

        public override void HandleInput()
        {
            main?.UpdateInput();
        }

        public override void UpdateBeforeSimulation()
        {
            Paused = false;
            main?.UpdateBeforeSim();
        }

        public override void UpdateAfterSimulation()
        {
            Paused = false;
            main?.UpdateAfterSim();
        }

        public override void Draw()
        {
            main?.UpdateDraw();
        }

        public override MyObjectBuilder_SessionComponent GetObjectBuilder()
        {
            main?.WorldSave();
            return base.GetObjectBuilder();
        }

        public override void UpdatingStopped()
        {
            Paused = true;
        }
    }
}