using System.Text;
using Digi.ComponentLib;
using Digi.PaintGun.Features.Tool;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Input;
using VRageMath;

namespace Digi.PaintGun.Features.Testing
{
    public class TestComp : ModComponent
    {
        public TestComp(PaintGunMod main) : base(main)
        {
            //UpdateMethods |= UpdateFlags.UPDATE_DRAW;
        }

        protected override void RegisterComponent()
        {
            //Log.Info($"GamePaths.ModsPath={MyAPIGateway.Utilities.GamePaths.ModsPath}");
            //Log.Info($"ModContext.ModPath={Main.Session.ModContext.ModPath}"); // <<< this is the correct one

            //DoListSerializationTest();

            //SetUpdateMethods(UpdateFlags.UPDATE_AFTER_SIM, true);
        }

        protected override void UnregisterComponent()
        {
        }

        protected override void UpdateInput(bool anyKeyOrMouse, bool inMenu, bool paused)
        {
        }

        protected override void UpdateBeforeSim(int tick)
        {
        }

        //MyEntity Ent;
        //MyEntity3DSoundEmitter SoundEmitter;
        //bool Spraying = true;

        //void SpawnSpraySound()
        //{
        //    Ent = new MyEntity();
        //    Ent.Save = false;
        //    Ent.Init(new StringBuilder("hi"), @"Models\Debug\Capsule.mwm", null, null);
        //    MyEntities.Add(Ent);
        //    Ent.PositionComp.SetPosition(MyAPIGateway.Session.Camera.WorldMatrix.Translation);

        //    SoundEmitter = new MyEntity3DSoundEmitter(Ent);

        //    MyVisualScriptLogicProvider.AddGPS("spray", "", Ent.WorldMatrix.Translation, new Color(255, 0, 255));
        //}

        protected override void UpdateAfterSim(int tick)
        {
            //if(Ent == null)
            //{
            //    if(MyAPIGateway.Input.IsNewKeyPressed(MyKeys.L))
            //    {
            //        SpawnSpraySound();
            //    }

            //    return;
            //}

            //if(MyAPIGateway.Input.IsNewKeyPressed(MyKeys.L))
            //{
            //    Spraying = !Spraying;
            //    MyAPIGateway.Utilities.ShowNotification($"Spray={Spraying}");
            //}

            //bool OwnerIsLocalPlayer = MyAPIGateway.Input.IsAnyShiftKeyPressed();
            //bool force2D = (OwnerIsLocalPlayer);

            //if(OwnerIsLocalPlayer && SoundEmitter.IsPlaying && SoundEmitter.Force2D != force2D)
            //{
            //    SoundEmitter.StopSound(false);
            //}

            //if(Spraying && !SoundEmitter.IsPlaying)
            //{
            //    SoundEmitter.CustomVolume = (force2D ? Main.Settings.spraySoundVolume * 0.5f : Main.Settings.spraySoundVolume);
            //    SoundEmitter.Force2D = force2D;
            //    SoundEmitter.PlaySound(PaintGunItem.SpraySound, force2D: force2D);
            //}

            //if((!Spraying) && SoundEmitter.IsPlaying)
            //{
            //    SoundEmitter.StopSound(false);
            //}
        }

        protected override void UpdateDraw()
        {
            //try
            //{
            //    var material = MyStringId.GetOrCompute("SafeZoneShield_Material");
            //    var pos = new Vector3D(5, 0, 0);
            //    //MyTransparentGeometry.AddBillboardOriented(material, Color.White, pos, Vector3.Left, Vector3.Up, 1, 1);
            //    //MyTransparentGeometry.AddPointBillboard(material, Color.White, pos, 1, 0);
            //
            //    MyTransparentGeometry.AddTriangleBillboard(pos + Vector3.Up, pos + Vector3.Left, pos + Vector3.Right, Vector3.Forward, Vector3.Forward, Vector3.Forward, Vector2.Zero, new Vector2(0, 1), Vector2.One, material, uint.MaxValue, pos, Color.White);
            //}
            //catch(Exception)
            //{
            //    Log.Error("SafeZoneShield_Material not found", Log.PRINT_MSG);
            //    throw;
            //}
            //
            //try
            //{
            //    var material = MyStringId.GetOrCompute("SafeZoneShieldGlass");
            //    var pos = new Vector3D(0, 0, 0);
            //    //MyTransparentGeometry.AddBillboardOriented(material, Color.White, pos, Vector3.Left, Vector3.Up, 1, 1);
            //    //MyTransparentGeometry.AddPointBillboard(material, Color.White, pos, 1, 0);
            //
            //    MyTransparentGeometry.AddTriangleBillboard(pos + Vector3.Up, pos + Vector3.Left, pos + Vector3.Right, Vector3.Forward, Vector3.Forward, Vector3.Forward, Vector2.Zero, new Vector2(0, 1), Vector2.One, material, uint.MaxValue, pos, Color.White);
            //}
            //catch(Exception)
            //{
            //    Log.Error("SafeZoneShieldGlass not found", Log.PRINT_MSG);
            //    throw;
            //}

            //if(MyAPIGateway.Input.IsAnyAltKeyPressed())
            //{
            //    DebugDrawCharacterSphere();
            //}
        }

        //private void DebugDrawCharacterSphere()
        //{
        //    var character = MyAPIGateway.Session.Player.Character;
        //    var sphere = Utils.GetCharacterSelectionSphere(character);
        //    var matrix = MatrixD.CreateTranslation(sphere.Center);
        //    var color = Color.Lime;
        //    var material = MyStringId.GetOrCompute("Square");
        //
        //    MySimpleObjectDraw.DrawTransparentSphere(ref matrix, (float)sphere.Radius, ref color, MySimpleObjectRasterizer.Wireframe, 12, material, material, 0.001f, blendType: BlendTypeEnum.SDR);
        //}

        //void DoListSerializationTest()
        //{
        //    {
        //        var data = new SerializedList();

        //        data.PackedColorMasks = null;

        //        var bytes = MyAPIGateway.Utilities.SerializeToBinary(data);

        //        Log.Info($"null list --- bytes={bytes.Length}; actual bytes: {string.Join(", ", bytes)}");
        //    }

        //    {
        //        var data = new SerializedList();

        //        data.PackedColorMasks = new List<uint>();

        //        var bytes = MyAPIGateway.Utilities.SerializeToBinary(data);

        //        Log.Info($"empty list --- bytes={bytes.Length}; actual bytes: {string.Join(", ", bytes)}");
        //    }

        //    {
        //        var data = new SerializedList();

        //        data.PackedColorMasks = new List<uint>();
        //        data.PackedColorMasks.Add(118218915);

        //        var bytes = MyAPIGateway.Utilities.SerializeToBinary(data);

        //        Log.Info($"single num --- bytes={bytes.Length}; actual bytes: {string.Join(", ", bytes)}");
        //    }

        //    {
        //        var data = new SerializedList();

        //        data.PackedColorMasks = new List<uint>();
        //        data.PackedColorMasks.Add(0);
        //        data.PackedColorMasks.Add(0);
        //        data.PackedColorMasks.Add(118218915);
        //        data.PackedColorMasks.Add(0);

        //        var bytes = MyAPIGateway.Utilities.SerializeToBinary(data);

        //        Log.Info($"list with 4 values, 3 of which are 0 --- bytes={bytes.Length}; actual bytes: {string.Join(", ", bytes)}");
        //    }

        //    {
        //        var data = new SerializedList();

        //        data.PackedColorMasks = new List<uint>();
        //        data.PackedColorMasks.Add(118218915);
        //        data.PackedColorMasks.Add(118218915);
        //        data.PackedColorMasks.Add(118218915);
        //        data.PackedColorMasks.Add(118218915);

        //        var bytes = MyAPIGateway.Utilities.SerializeToBinary(data);

        //        Log.Info($"list with 4 values --- bytes={bytes.Length}; actual bytes: {string.Join(", ", bytes)}");
        //    }

        //    {
        //        var data = new SerializedDictionary();

        //        data.PackedColorMasks = new Dictionary<int, uint>();

        //        var bytes = MyAPIGateway.Utilities.SerializeToBinary(data);

        //        Log.Info($"empty dictionary --- bytes={bytes.Length}; actual bytes: {string.Join(", ", bytes)}");
        //    }

        //    {
        //        var data = new SerializedDictionary();

        //        data.PackedColorMasks = new Dictionary<int, uint>();
        //        data.PackedColorMasks.Add(5, 118218915);

        //        var bytes = MyAPIGateway.Utilities.SerializeToBinary(data);

        //        Log.Info($"single dictionary key --- bytes={bytes.Length}; actual bytes: {string.Join(", ", bytes)}");
        //    }

        //    {
        //        var data = new SerializedDictionary();

        //        data.PackedColorMasks = new Dictionary<int, uint>();
        //        data.PackedColorMasks.Add(1, 118218915);
        //        data.PackedColorMasks.Add(4, 118218915);
        //        data.PackedColorMasks.Add(6, 118218915);
        //        data.PackedColorMasks.Add(8, 118218915);

        //        var bytes = MyAPIGateway.Utilities.SerializeToBinary(data);

        //        Log.Info($"4 dictionary keys --- bytes={bytes.Length}; actual bytes: {string.Join(", ", bytes)}");
        //    }
        //}

        //[ProtoContract(UseProtoMembersOnly = true)]
        //class SerializedList
        //{
        //    [ProtoMember(1)]
        //    public List<uint> PackedColorMasks;

        //    public SerializedList() { } // Empty constructor required for deserialization
        //}

        //[ProtoContract(UseProtoMembersOnly = true)]
        //class SerializedDictionary
        //{
        //    [ProtoMember(1)]
        //    public Dictionary<int, uint> PackedColorMasks;

        //    public SerializedDictionary() { } // Empty constructor required for deserialization
        //}
    }
}