using Digi.NetworkLib;
using Digi.PaintGun.Features.Palette;
using ProtoBuf;
using VRage.Utils;

namespace Digi.PaintGun.Features.Sync
{
    [ProtoContract(UseProtoMembersOnly = true)]
    public class PacketPaletteUpdate : PacketBase
    {
        [ProtoMember(1)]
        int? SelectedColorSlot;

        [ProtoMember(2)]
        MyStringHash? SelectedSkin;

        [ProtoMember(3)]
        bool? ApplyColor;

        [ProtoMember(4)]
        bool? ApplySkin;

        [ProtoMember(5)]
        bool? ColorPickMode;

        //[ProtoMember(6)]
        //List<uint> PackedColorMasks;

        public PacketPaletteUpdate() { } // Empty constructor required for deserialization

        public void Send(int? selectedColorSlot = null, MyStringHash? selectedSkin = null, bool? applyColor = null, bool? applySkin = null, bool? colorPickMode = null) //, List<Vector3> colorMasks = null)
        {
            SelectedColorSlot = selectedColorSlot;
            SelectedSkin = selectedSkin;
            ApplyColor = applyColor;
            ApplySkin = applySkin;
            ColorPickMode = colorPickMode;
            //PackedColorMasks = null;
            //
            //if(colorMasks != null)
            //{
            //    PackedColorMasks = Main.Caches.PackedColors;
            //    PackedColorMasks.Clear();
            //
            //    foreach(var colorMask in colorMasks)
            //    {
            //        PackedColorMasks.Add(colorMask.PackHSVToUint());
            //    }
            //}

            // too frequent
            //if(Constants.NETWORK_ACTION_LOGGING)
            //{
            //    Log.Info($@"{GetType().Name} :: Sending pallete update: SelectedColorIndex={Utils.PrintNullable(SelectedColorIndex)}, SelectedSkinIndex={Utils.PrintNullable(SelectedSkinIndex)}, ApplyColor={Utils.PrintNullable(ApplyColor)}, ApplySkin={Utils.PrintNullable(ApplySkin)}, ColorPickMode={Utils.PrintNullable(ColorPickMode)}");
            //}

            Network.SendToServer(this);
            //PackedColorMasks = null;
        }

        public override void Received(ref RelayMode relay)
        {
            // too frequent
            //if(Constants.NETWORK_ACTION_LOGGING)
            //{
            //    Log.Info($@"{GetType().Name} :: Received pallete update for {Utils.PrintPlayerName(SteamId)}; SelectedColorIndex={Utils.PrintNullable(SelectedColorIndex)}, SelectedSkinIndex={Utils.PrintNullable(SelectedSkinIndex)}, ApplyColor={Utils.PrintNullable(ApplyColor)}, ApplySkin={Utils.PrintNullable(ApplySkin)}, ColorPickMode={Utils.PrintNullable(ColorPickMode)}");
            //}

            relay = RelayMode.RelayOriginal;

            PlayerInfo pi = Main.Palette.GetOrAddPlayerInfo(OriginalSenderSteamId);

            if(SelectedColorSlot.HasValue)
                pi.SelectedColorSlot = SelectedColorSlot.Value;

            if(SelectedSkin.HasValue)
                pi.SelectedSkin = SelectedSkin.Value;

            if(ApplyColor.HasValue)
                pi.ApplyColor = ApplyColor.Value;

            if(ApplySkin.HasValue)
                pi.ApplySkin = ApplySkin.Value;

            if(ColorPickMode.HasValue)
                pi.ColorPickMode = ColorPickMode.Value;

            //if(PackedColorMasks != null)
            //    pi.SetColors(PackedColorMasks);
        }
    }
}