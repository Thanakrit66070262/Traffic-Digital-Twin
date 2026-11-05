using UnityEngine;

namespace SmartFlow.Traffic
{
    // ระยะจาก pivot ถึงหน้ารถ/ท้ายรถตามแกน forward (Unity unit)
    // วัดจาก mesh bounds แทน Renderer.bounds เพราะ Renderer.bounds ของ prefab asset เป็นศูนย์
    // ทำให้วัดได้ทั้ง prefab (ก่อน spawn) และ instance ด้วยวิธีเดียวกัน
    public static class VehicleDimensions
    {
        // ใช้เมื่อหา mesh ไม่เจอ ≈ ครึ่งความยาวรถเก๋ง
        private const float FALLBACK_HALF_LENGTH_UNITS = 2.5f;
        private const int BOX_CORNERS = 8;

        public static void Measure(GameObject root, out float frontUnits, out float rearUnits)
        {
            Transform rootTransform = root.transform;
            float minZ = float.PositiveInfinity;
            float maxZ = float.NegativeInfinity;

            foreach (MeshFilter filter in root.GetComponentsInChildren<MeshFilter>(true))
                Accumulate(filter.sharedMesh, filter.transform, rootTransform, ref minZ, ref maxZ);
            foreach (SkinnedMeshRenderer skinned in root.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                Accumulate(skinned.sharedMesh, skinned.transform, rootTransform, ref minZ, ref maxZ);

            if (float.IsPositiveInfinity(minZ))
            {
                Debug.LogWarning($"[VehicleDimensions] '{root.name}' has no mesh; using fallback length.");
                frontUnits = FALLBACK_HALF_LENGTH_UNITS;
                rearUnits = FALLBACK_HALF_LENGTH_UNITS;
                return;
            }

            // InverseTransformPoint หารด้วย scale ของ root แล้ว — คูณกลับให้เป็น Unity unit ของโลก
            float scaleZ = rootTransform.lossyScale.z;
            frontUnits = maxZ * scaleZ;
            rearUnits = -minZ * scaleZ;
        }

        private static void Accumulate(Mesh mesh, Transform meshTransform, Transform rootTransform, ref float minZ, ref float maxZ)
        {
            if (mesh == null) return;

            Bounds bounds = mesh.bounds;
            for (int i = 0; i < BOX_CORNERS; i++)
            {
                Vector3 sign = new Vector3((i & 1) == 0 ? -1f : 1f, (i & 2) == 0 ? -1f : 1f, (i & 4) == 0 ? -1f : 1f);
                Vector3 corner = bounds.center + Vector3.Scale(bounds.extents, sign);
                float z = rootTransform.InverseTransformPoint(meshTransform.TransformPoint(corner)).z;
                minZ = Mathf.Min(minZ, z);
                maxZ = Mathf.Max(maxZ, z);
            }
        }
    }
}
