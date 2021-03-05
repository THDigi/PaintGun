using System.Text;
using Sandbox.ModAPI;
using VRage.Input;
using VRage.Utils;

namespace Digi.PaintGun.Utilities
{
    public static class StringBuilderExtensions
    {
        public static StringBuilder AppendInputBind(this StringBuilder sb, MyStringId controlId)
        {
            var control = MyAPIGateway.Input.GetGameControl(controlId);
            if(control == null)
                return sb.Append("<Unknown:").Append(controlId.String).Append(">");

            string mouse = control.GetControlButtonName(MyGuiInputDeviceEnum.Mouse);
            if(!string.IsNullOrEmpty(mouse))
                return sb.Append(mouse);

            string key = control.GetControlButtonName(MyGuiInputDeviceEnum.Keyboard);
            if(string.IsNullOrEmpty(key))
                key = control.GetControlButtonName(MyGuiInputDeviceEnum.KeyboardSecond);
            if(!string.IsNullOrEmpty(key))
                return sb.Append(key);

            return sb.Append("<").Append(control.GetControlName().String).Append(">");
        }
    }
}
