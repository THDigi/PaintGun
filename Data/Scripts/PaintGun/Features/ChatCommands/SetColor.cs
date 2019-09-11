using System;
using System.Text;
using Digi.PaintGun.Features.Palette;
using Digi.PaintGun.Utilities;
using VRage.Game;
using VRage.Game.ModAPI.Ingame.Utilities;
using VRageMath;

namespace Digi.PaintGun.Features.ChatCommands
{
    public class SetColor : CommandHandlerBase
    {
        const StringComparison COMPARE_TYPE = StringComparison.InvariantCultureIgnoreCase;

        public SetColor() : base("rgb", "hsv")
        {
        }

        public override void Execute(MyCommandLine parser)
        {
            var rgb = (parser.Argument(1) == "rgb");
            var values = new float[3];

            if(rgb && parser.Argument(2).StartsWith("#", COMPARE_TYPE))
            {
                var hexText = parser.Argument(2);

                if(hexText.Length < 7)
                {
                    Utils.ShowColoredChatMessage(PaintGunMod.MOD_NAME, "Invalid HEX color, needs 6 characters after #.", MyFontEnum.Red);
                    return;
                }

                int c = 0;

                for(int i = 1; i < 7; i += 2)
                {
                    values[c++] = Convert.ToInt32(hexText[i].ToString() + hexText[i + 1].ToString(), 16);
                }
            }
            else
            {
                if(parser.ArgumentCount != 5)
                {
                    Utils.ShowColoredChatMessage(PaintGunMod.MOD_NAME, "Need to specify 3 numbers separated by spaces.", MyFontEnum.Red);
                    return;
                }

                for(int i = 0; i < 3; i++)
                {
                    var arg = parser.Argument(i + 2);
                    if(!float.TryParse(arg, out values[i]))
                    {
                        Utils.ShowColoredChatMessage(PaintGunMod.MOD_NAME, $"'{arg}' is not a valid number!", MyFontEnum.Red);
                        return;
                    }
                }
            }

            Vector3 colorMask;

            if(rgb)
            {
                colorMask = Utils.RGBToColorMask(new Color(MathHelper.Clamp((int)values[0], 0, 255), MathHelper.Clamp((int)values[1], 0, 255), MathHelper.Clamp((int)values[2], 0, 255)));
            }
            else
            {
                colorMask = Utils.HSVToColorMask(new Vector3(MathHelper.Clamp(values[0], 0f, 360f) / 360.0f, MathHelper.Clamp(values[1], 0f, 100f) / 100.0f, MathHelper.Clamp(values[2], 0f, 100f) / 100.0f));
            }

            var material = new PaintMaterial(colorMask, Main.Palette.GetLocalPaintMaterial().Skin);

            Main.Palette.GrabPaletteFromPaint(material);
        }

        public override void PrintHelp(StringBuilder text)
        {
            text.Append(ChatCommands.MAIN_COMMAND).Append(" rgb <0~255> <0~255> <0~255>").Append('\n');
            text.Append(ChatCommands.MAIN_COMMAND).Append(" rgb #<00~FF><00~FF><00~FF>").Append('\n');
            text.Append(ChatCommands.MAIN_COMMAND).Append(" hsv <0.0~360.0> <0.0~100.0> <0.0~100.0>").Append('\n');
            text.Append("  Set the currently selected slot's color.").Append('\n');
            text.Append('\n');
        }
    }
}