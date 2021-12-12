using System;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Collections;
using VRageMath;

namespace Digi.PaintGun.Features.Tool
{
    public class SpraySoundEmitter : IDisposable
    {
        public bool PlaySpray = false;

        int EmitterUpdateSkip = 999;
        readonly MyEntity3DSoundEmitter SoundEmitter;
        readonly Func<Vector3D> PositionGetter;

        public SpraySoundEmitter(Func<Vector3D> positionGetter, Func<bool> realisticIsHoldingCondition)
        {
            if(positionGetter == null)
                throw new ArgumentNullException("positionGetter");

            if(realisticIsHoldingCondition == null)
                throw new ArgumentNullException("realisticIsHoldingCondition");

            SoundEmitter = new MyEntity3DSoundEmitter(null);
            PositionGetter = positionGetter;

            // remove all 2D forcing conditions
            ConcurrentCachingList<Delegate> shouldPlay2D = SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.ShouldPlay2D];
            shouldPlay2D.ClearImmediate();
            shouldPlay2D.Add(new Func<bool>(EmitterPlay2D)); // if no methods are declared, it defaults to always 2D

            if(MyAPIGateway.Session.SessionSettings.RealisticSound)
            {
                // remove some unnecessary conditions
                foreach(ConcurrentCachingList<Delegate> funcList in SoundEmitter.EmitterMethods.Values)
                {
                    foreach(Delegate func in funcList)
                    {
                        switch(func.Method.Name)
                        {
                            case "IsCurrentWeapon":
                            case "IsControlledEntity":
                            case "IsOnSameGrid":
                                funcList.Remove(func);
                                break;
                        }
                    }
                }

                // custom IsCurrentWeapon because Entity is not set for this emitter to detect on its own
                SoundEmitter.EmitterMethods[(int)MyEntity3DSoundEmitter.MethodsEnum.CanHear].Add(realisticIsHoldingCondition);

                //foreach(KeyValuePair<int, ConcurrentCachingList<Delegate>> kv in SoundEmitter.EmitterMethods)
                //{
                //    kv.Value.ApplyChanges();
                //    foreach(Delegate func in kv.Value)
                //    {
                //        Log.Info($"{((MyEntity3DSoundEmitter.MethodsEnum)kv.Key)} {func.Method.Name}");
                //    }
                //}
            }
        }

        public void Dispose()
        {
            SoundEmitter.Cleanup();
        }

        public void Stop()
        {
            SoundEmitter.StopSound(true);
            EmitterUpdateSkip = 999; // update on the first frame next time
        }

        public void Update(float soundVolume)
        {
            if(++EmitterUpdateSkip > 10)
            {
                EmitterUpdateSkip = 0;
                SoundEmitter.Update();
            }

            if(PlaySpray)
            {
                SoundEmitter.SetPosition(PositionGetter.Invoke());

                if(SoundEmitter.CustomVolume != soundVolume)
                {
                    SoundEmitter.CustomVolume = soundVolume;
                }

                if(!SoundEmitter.IsPlaying)
                {
                    SoundEmitter.PlaySound(PaintGunItem.SpraySound, stopPrevious: true, skipIntro: true, force2D: false);
                }
            }

            if(!PlaySpray && SoundEmitter.IsPlaying)
            {
                SoundEmitter.StopSound(false);
            }
        }

        bool EmitterPlay2D() => false;
    }
}
