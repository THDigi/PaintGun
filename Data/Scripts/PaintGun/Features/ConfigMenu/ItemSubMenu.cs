﻿using Digi.PaintGun.Utilities;
using VRageMath;
using static Draygo.API.HudAPIv2;

namespace Digi.PaintGun.Features.ConfigMenu
{
    public class ItemSubMenu : ItemBase<MenuSubCategory>
    {
        public string Title;
        public Color Color = new Color(0, 155, 255);

        public ItemSubMenu(MenuCategoryBase category, string title, string header = null) : base(category)
        {
            Title = title;
            Item = new MenuSubCategory(string.Empty, category, header ?? title);
            UpdateTitle();
        }

        protected override void UpdateValue()
        {
            // nothing to update
        }

        protected override void UpdateTitle()
        {
            string titleColor = (Item.Interactable ? "" : "<color=gray>");
            string valueColor = Utils.ColorTag(Item.Interactable ? Color : Color.Gray);
            Item.Text = $"{titleColor}{Title} {valueColor}>>>";
        }
    }
}
