using VRage.ModAPI;
using VRageMath;

namespace Digi.PaintGun.Features.Tool
{
    public struct DetectionInfo
    {
        public readonly IMyEntity Entity;
        public readonly Vector3D DetectionPoint;

        public DetectionInfo(IMyEntity entity, Vector3D detectionPoint)
        {
            Entity = entity;
            DetectionPoint = detectionPoint;
        }
    }
}