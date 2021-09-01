using System;
using System.Collections.Generic;
using Digi.PaintGun.Utilities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Input;
using VRageMath;
using static Draygo.API.HudAPIv2;

namespace Digi.PaintGun.Features.ConfigMenu
{
    public class ItemInput : ItemBase<MenuKeybindInput>
    {
        public Func<ControlCombination> Getter;
        public Action<ControlCombination> Setter;
        public string Title;
        public Color ValueColor = new Color(0, 255, 100);
        public ControlCombination DefaultValue;

        public ItemInput(MenuCategoryBase category, string title, Func<ControlCombination> getter, Action<ControlCombination> setter, ControlCombination defaultValue) : base(category)
        {
            Title = title;
            Getter = getter;
            Setter = setter;
            DefaultValue = defaultValue;

            Item = new MenuKeybindInput(string.Empty, category, "Press a key to bind.\nCan be combined with alt/ctrl/shift.\nUnbind by confirming without a key.", OnSubmit);

            UpdateTitle();
        }

        protected override void UpdateValue()
        {
            // nothing to update
        }

        protected override void UpdateTitle()
        {
            string titleColored = (Item.Interactable ? Title : "<color=gray>" + Title);

            ControlCombination value = Getter();
            string valStr = "(none)";
            if(value != null && value.combination.Count > 0)
                valStr = value.combinationString;

            string valueColored = (Item.Interactable ? Utils.ColorTag(ValueColor, valStr) : valStr);

            string defaultColored = "";
            string comboValue = value?.combinationString ?? "";
            string comboDefault = DefaultValue?.combinationString ?? "";
            if(comboDefault == comboValue)
                defaultColored = (Item.Interactable ? Utils.ColorTag(Color.Gray, "[default]") : "[default]");

            Item.Text = $"{titleColored}: {valueColored} {defaultColored}";
        }

        private void OnSubmit(MyKeys key, bool shift, bool ctrl, bool alt)
        {
            try
            {
                ControlCombination combination;
                if(TryGetCombination(key, alt, ctrl, shift, out combination))
                {
                    Setter?.Invoke(combination);
                    UpdateTitle();
                }
            }
            catch(Exception e)
            {
                Log.Error(e);
            }
        }

        private static bool TryGetCombination(MyKeys key, bool alt, bool ctrl, bool shift, out ControlCombination combination)
        {
            combination = null;
            if(key == MyKeys.None) // unbind
                return true;

            string input = InputHandler.inputNames.GetValueOrDefault(key, null);
            if(input == null)
            {
                MyAPIGateway.Utilities.ShowNotification($"Unknown key: {key.ToString()}", 5000, MyFontEnum.Red);
                return false;
            }

            string combinationString = (alt ? "alt " : "") + (ctrl ? "ctrl " : "") + (shift ? "shift " : "") + input;

            combination = ControlCombination.CreateFrom(combinationString);

            MyAPIGateway.Utilities.ShowNotification($"Bound succesfully to: {combination.GetFriendlyString()}", 3000, MyFontEnum.Green);
            return true;
        }
    }
}
