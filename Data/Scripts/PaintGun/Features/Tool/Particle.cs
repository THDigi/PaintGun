using VRage.Utils;
using VRageMath;

namespace Digi.PaintGun.Features.Tool
{
    public class Particle
    {
        public Vector3 RelativePosition;
        public Vector3 VelocityPerTick;
        public Color Color;
        public float Radius;
        public float Angle;
        public short Life;

        public Particle() { }

        /// <summary>
        /// Called when retrieved from pool.
        /// </summary>
        public void Init(MatrixD matrix, Color color)
        {
            RelativePosition = Vector3.Zero;
            VelocityPerTick = (matrix.Forward * 0.5) / (float)Constants.TICKS_PER_SECOND;
            Color = color;
            Radius = 0.01f;
            Angle = MyUtils.GetRandomFloat(-1, 1) * 45;
            Life = 30;
        }

        /// <summary>
        /// Called when returned to pool.
        /// </summary>
        public void Clear()
        {
        }
    }
}