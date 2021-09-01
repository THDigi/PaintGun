using System;
using System.Text;
using Digi.PaintGun.Utilities;
using Sandbox.Definitions;
using Sandbox.Game;
using Sandbox.Game.Gui;
using VRage.Game;

namespace Digi.PaintGun.Features.Tool
{
    public class LocalToolDescription : ModComponent
    {
        StringBuilder SB = new StringBuilder(512);
        bool RequiresRefresh = false;

        public LocalToolDescription(PaintGunMod main) : base(main)
        {
            if(Main.IsDedicatedServer)
                throw new Exception($"Why's this called in DS?");
        }

        protected override void RegisterComponent()
        {
            RefreshToolDescription();
            Main.GameConfig.GamepadUseChanged += GamepadUseChanged;
            Main.LocalToolHandler.LocalToolEquipped += LocalToolEquipped;

            RefreshToolDescription();
            Refresh();
        }

        protected override void UnregisterComponent()
        {
            Main.GameConfig.GamepadUseChanged -= GamepadUseChanged;
            Main.LocalToolHandler.LocalToolEquipped -= LocalToolEquipped;
        }

        public void RefreshToolDescription()
        {
            RequiresRefresh = true;
        }

        void GamepadUseChanged(bool isUsed)
        {
            RequiresRefresh = true;

            if(Main.LocalToolHandler.LocalTool != null)
                Refresh();
        }

        void LocalToolEquipped(PaintGunItem item)
        {
            Refresh();
        }

        void Refresh()
        {
            if(!RequiresRefresh)
                return;

            RequiresRefresh = false;

            MyDefinitionId defId = new MyDefinitionId(typeof(MyObjectBuilder_PhysicalGunObject), Constants.PAINTGUN_PHYSITEMID);
            MyPhysicalItemDefinition itemDef = MyDefinitionManager.Static.GetPhysicalItemDefinition(defId);
            if(itemDef == null)
                throw new Exception($"Can't find '{defId.ToString()}' hand item definition!");

            SB.Clear();
            SB.Append("Paints blocks.");

            if(Main.GameConfig.UsingGamepad)
            {
                SB.Append("\n").Append(Main.Constants.GamepadBindName_Paint).Append(" paint, ").Append(Main.Constants.GamepadBindName_DeepPaintMode).Append(" deep paint mode");
                SB.Append("\n").Append(Main.Constants.GamepadBindName_CycleColors).Append(" cycle colors");
                SB.Append("\n").Append(Main.Constants.GamepadBindName_CycleSkins).Append(" cycle skins");
            }
            else // kb+m
            {
                SB.Append("\n[").AppendInputBind(MyControlsSpace.PRIMARY_TOOL_ACTION).Append("] paint, [").AppendInputBind(MyControlsSpace.SECONDARY_TOOL_ACTION).Append("] deep paint mode");
                SB.Append("\n[").Append(Main.Settings.requireCtrlForColorCycle ? "Ctrl+" : "").Append("Scroll] cycle colors");
                SB.Append("\n[Shift+Scroll] cycle skins");
            }

            SB.Append("\n[").AppendInputBind(MyControlsSpace.CUBE_COLOR_CHANGE).Append("] toggle apply color");
            SB.Append("\n[Shift+").AppendInputBind(MyControlsSpace.CUBE_COLOR_CHANGE).Append("] toggle apply skin");
            SB.Append("\n[Shift+").AppendInputBind(MyControlsSpace.SECONDARY_TOOL_ACTION).Append("] grab color+skin from aimed block/player");
            SB.Append("\n[Shift+").AppendInputBind(MyControlsSpace.LANDING_GEAR).Append("] color+skin picking mode");
            SB.Append("\n[Shift+").AppendInputBind(MyControlsSpace.CUBE_BUILDER_CUBESIZE_MODE).Append("] replace mode (creative/SM)");
            SB.Append("\n[").Append(ChatCommands.ChatCommands.MAIN_COMMAND).Append("] in chat for more info");

            itemDef.DescriptionString = SB.ToString();

            // refresh HUD with the new text if paintgun is equipped
            //if(MyHud.BlockInfo.DefinitionId.SubtypeName == Constants.PAINTGUN_PHYSITEMID)
            if(Main.LocalToolHandler.LocalTool != null)
            {
                MyHud.BlockInfo.SetContextHelp(itemDef);
            }
        }
    }
}