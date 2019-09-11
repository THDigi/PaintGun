using System;
using Digi.ComponentLib;
using Sandbox.ModAPI;
using VRageMath;

namespace Digi.PaintGun.Utilities
{
    public class DrawUtils : ModComponent
    {
        private MatrixD _viewProjInvCache;
        private bool _viewProjInvCompute = true;

        private float _scaleFovCache;
        private bool _scaleFovCompute = true;

        public DrawUtils(PaintGunMod main) : base(main)
        {
            if(Main.IsDedicatedServer)
                throw new Exception($"Why is {GetType().Name} initialized for DS?!");

            UpdateMethods = UpdateFlags.UPDATE_DRAW;
        }

        protected override void RegisterComponent()
        {
        }

        protected override void UnregisterComponent()
        {
        }

        protected override void UpdateDraw()
        {
            _viewProjInvCompute = true;
            _scaleFovCompute = true;
        }

        /// <summary>
        /// <para>Camera view matrix and projection matrix multiplied and inverted.</para>
        /// This ensures the math is done at most once per frame, if ResetDrawCaches() is correctly called at the start of Draw().
        /// </summary>
        public MatrixD ViewProjectionInv
        {
            get
            {
                if(_viewProjInvCompute)
                {
                    var cam = MyAPIGateway.Session.Camera;

                    // NOTE ProjectionMatrix needs recomputing because camera's m_fovSpring is set after ProjectionMatrix is computed, MyCamera.Update(float updateStepTime) and MyCamera.FovWithZoom
                    var aspectRatio = cam.ViewportSize.X / cam.ViewportSize.Y;
                    var safeNear = Math.Min(4f, cam.NearPlaneDistance); // MyCamera.GetSafeNear()
                    var projectionMatrix = MatrixD.CreatePerspectiveFieldOfView(cam.FovWithZoom, aspectRatio, safeNear, cam.FarPlaneDistance);
                    _viewProjInvCache = MatrixD.Invert(cam.ViewMatrix * projectionMatrix);
                    _viewProjInvCompute = false;
                }

                return _viewProjInvCache;
            }
        }

        /// <summary>
        /// <para>Tangent of (FOV with zoom / 2).</para>
        /// This ensures the math is done at most once per frame, if ResetDrawCaches() is correctly called at the start of Draw().
        /// </summary>
        public float ScaleFOV
        {
            get
            {
                if(_scaleFovCompute)
                {
                    var cam = MyAPIGateway.Session.Camera;
                    _scaleFovCache = (float)Math.Tan(cam.FovWithZoom * 0.5);
                    _scaleFovCompute = false;
                }

                return _scaleFovCache;
            }
        }

        /// <summary>
        /// Transforms screen coordinates to world coordinates.
        /// -1,-1 = bottom-left; 1,1 = top-right.
        /// </summary>
        public Vector3D HUDtoWorld(Vector2D hud)
        {
            double hudX = hud.X;
            double hudY = hud.Y;

            // Vector4D.Transform(new Vector4D(hudX, hudY, 0, 1), ref ViewProjectionInv, out ...)
            var matrix = ViewProjectionInv;
            double x = hudX * matrix.M11 + hudY * matrix.M21 + /* 0 * matrix.M31 + 1 * */ matrix.M41;
            double y = hudX * matrix.M12 + hudY * matrix.M22 + /* 0 * matrix.M32 + 1 * */ matrix.M42;
            double z = hudX * matrix.M13 + hudY * matrix.M23 + /* 0 * matrix.M33 + 1 * */ matrix.M43;
            double w = hudX * matrix.M14 + hudY * matrix.M24 + /* 0 * matrix.M34 + 1 * */ matrix.M44;
            return new Vector3D(x / w, y / w, z / w);
        }
    }
}