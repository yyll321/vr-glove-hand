using UnityEngine;

namespace UnderwaterBaseline
{
    /// <summary>
    /// A bounded, low-resolution Eulerian water volume. It uses semi-Lagrangian
    /// advection, viscosity smoothing and a small pressure projection. The field is
    /// deliberately budgeted for a desktop/PICO preview, rather than presented as a
    /// CFD solver or a replacement for measured flow data.
    /// </summary>
    [DefaultExecutionOrder(80)]
    public sealed class UnderwaterFluidVolume : MonoBehaviour
    {
        public Bounds worldBounds = new Bounds(new Vector3(62, 5, 0), new Vector3(25, 10, 50));
        public UnderwaterPresence ambientSource;
        public Vector3 ambientCurrent = new Vector3(0.12f, 0, 0.04f);
        [Range(4, 32)] public int resolutionX = 24;
        [Range(4, 20)] public int resolutionY = 8;
        [Range(4, 32)] public int resolutionZ = 16;
        [Range(0, 2)] public float viscosity = 0.22f;
        [Range(0, 2)] public float densityDecay = 0.08f;
        [Range(0, 1)] public float ambientBlend = 0.35f;
        [Range(1, 30)] public float simulationHz = 12;
        [Range(1, 8)] public int pressureIterations = 3;
        public bool simulate = true;
        public bool drawGizmos;
        public Collider ground;
        public Collider[] obstacles = System.Array.Empty<Collider>();
        [Min(0.1f)] public float maximumSpeed = 1.5f;
        public bool activeRegionOnly=true;

        Vector3[] velocity, nextVelocity;
        float[] pressure, nextPressure, density, nextDensity, divergence;
        bool[] solid;
        Vector3 min, cell;
        float accumulator;
        public int CellCount => Mathf.Max(0, resolutionX * resolutionY * resolutionZ);
        public float LastStepMilliseconds { get; private set; }
        public int Steps { get; private set; }
        public float EffectiveHz
        {
            get
            {
                int tier=ambientSource && ambientSource.environment?ambientSource.environment.quality:2;
                return Mathf.Min(simulationHz,tier==0?4:tier==1?7:10);
            }
        }

        void Awake() { Allocate(); }

        void OnValidate()
        {
            resolutionX = Mathf.Clamp(resolutionX, 4, 32);
            resolutionY = Mathf.Clamp(resolutionY, 4, 20);
            resolutionZ = Mathf.Clamp(resolutionZ, 4, 32);
            simulationHz = Mathf.Clamp(simulationHz, 1, 30);
            pressureIterations = Mathf.Clamp(pressureIterations, 1, 8);
            // Physics queries are deferred until FixedUpdate on Unity's main thread.
            if (Application.isPlaying) velocity=null;
        }

        void Allocate()
        {
            int count = CellCount;
            if (count <= 0) return;
            velocity = new Vector3[count]; nextVelocity = new Vector3[count];
            pressure = new float[count]; nextPressure = new float[count]; divergence = new float[count];
            density = new float[count]; nextDensity = new float[count];solid=new bool[count];accumulator=0;
            min = worldBounds.min;
            cell = new Vector3(
                Mathf.Max(0.01f, worldBounds.size.x / Mathf.Max(1, resolutionX - 1)),
                Mathf.Max(0.01f, worldBounds.size.y / Mathf.Max(1, resolutionY - 1)),
                Mathf.Max(0.01f, worldBounds.size.z / Mathf.Max(1, resolutionZ - 1)));
            // Start from the slowly varying background current so the volume represents
            // perturbations to an established flow, not water that begins at rest.
            for (int z = 0; z < resolutionZ; z++) for (int x = 0; x < resolutionX; x++)
            {
                Vector3 column=CellWorld(x,0,z);float floor=worldBounds.min.y;
                if(ground && ground.Raycast(new Ray(new Vector3(column.x,worldBounds.max.y+1,column.z),Vector3.down),out var hit,worldBounds.size.y+3))floor=hit.point.y;
                for(int y=0;y<resolutionY;y++)
                {
                    Vector3 p=CellWorld(x,y,z);int i=Index(x,y,z);solid[i]=p.y<floor+0.02f;
                    foreach(var obstacle in obstacles)
                        if(obstacle && obstacle.enabled && obstacle.gameObject.activeInHierarchy && IsSolidPoint(obstacle,p)){solid[i]=true;break;}
                    velocity[i]=solid[i]?Vector3.zero:ambientSource?ambientSource.AmbientCurrentAt(p,Time.time):ambientCurrent;
                }
            }
        }

        // Flow cells use a conservative vertical envelope for nonconvex rocks.
        // ClosestPoint is unsupported for nonconvex MeshColliders. Radar still
        // queries the actual triangle surface, independently of this coarse mask.
        static bool IsSolidPoint(Collider obstacle,Vector3 point)
        {
            Bounds bounds=obstacle.bounds;
            if(!bounds.Contains(point))return false;
            if(obstacle is MeshCollider mesh && !mesh.convex)
            {
                float span=bounds.size.y+0.2f;
                var top=new Ray(new Vector3(point.x,bounds.max.y+0.1f,point.z),Vector3.down);
                var bottom=new Ray(new Vector3(point.x,bounds.min.y-0.1f,point.z),Vector3.up);
                return obstacle.Raycast(top,out var upper,span) && obstacle.Raycast(bottom,out var lower,span) &&
                    point.y<=upper.point.y+0.02f && point.y>=lower.point.y-0.02f;
            }
            return (obstacle.ClosestPoint(point)-point).sqrMagnitude<0.000001f;
        }

        void FixedUpdate()
        {
            if (!simulate) return;
            if(activeRegionOnly && ambientSource && ambientSource.motor && ambientSource.motor.origin && ambientSource.motor.origin.Camera &&
                !Contains(ambientSource.motor.origin.Camera.transform.position)) {accumulator=0;return;}
            if (velocity == null || velocity.Length != CellCount) Allocate();
            if (velocity == null) return;
            accumulator += Time.fixedDeltaTime;
            float step = 1f / Mathf.Max(1, EffectiveHz);
            // At most one bounded grid update per physics tick; never replay a long
            // catch-up backlog when the Editor or Live Preview stalls.
            if (accumulator >= step)
            {
                Step(step, Time.time);
                accumulator = Mathf.Min(accumulator-step,step);
            }
        }

        public Vector3 SampleVelocity(Vector3 world)
        {
            if (velocity == null || velocity.Length == 0 || !worldBounds.Contains(world))
                return ambientSource ? ambientSource.AmbientCurrentAt(world, Time.time) : ambientCurrent;
            return SampleVector(velocity, world);
        }

        public bool Contains(Vector3 world) => worldBounds.Contains(world);

        public float SampleDensity(Vector3 world)
        {
            if (density == null || density.Length == 0 || !worldBounds.Contains(world)) return 0;
            return SampleScalar(density, world);
        }

        public void AddImpulse(Vector3 world, Vector3 impulse, float amount = 1)
        {
            if (velocity == null || !worldBounds.Contains(world)) return;
            Grid(world, out int x, out int y, out int z);
            int radius = Mathf.Clamp(Mathf.CeilToInt(amount), 1, 3);
            for (int dz = -radius; dz <= radius; dz++) for (int dy = -radius; dy <= radius; dy++) for (int dx = -radius; dx <= radius; dx++)
            {
                int xx = x + dx, yy = y + dy, zz = z + dz;
                if (!Inside(xx, yy, zz)) continue;
                float distance = new Vector3(dx, dy, dz).magnitude / radius;
                if (distance > 1) continue;
                float weight = 1 - distance;
                int i = Index(xx, yy, zz);
                if(solid[i])continue;
                velocity[i] = Vector3.ClampMagnitude(velocity[i]+impulse*weight,maximumSpeed);
                density[i] = Mathf.Min(1, density[i] + Mathf.Abs(impulse.y) * 0.008f * weight);
            }
        }

        void Step(float dt, float time)
        {
            float start = Time.realtimeSinceStartup;
            for (int z = 0; z < resolutionZ; z++) for (int y = 0; y < resolutionY; y++) for (int x = 0; x < resolutionX; x++)
            {
                int i = Index(x, y, z); Vector3 p = CellWorld(x, y, z);
                if(solid[i]){nextVelocity[i]=Vector3.zero;nextDensity[i]=0;continue;}
                Vector3 ambient = ambientSource ? ambientSource.AmbientCurrentAt(p, time) : ambientCurrent;
                Vector3 back = p - velocity[i] * dt;
                Vector3 advected = SampleVector(velocity, back);
                // These samples lie exactly on grid nodes. Avoid six redundant
                // trilinear interpolations and world/grid conversions per cell.
                Vector3 neighbour = (velocity[IndexClamped(x+1,y,z)]+velocity[IndexClamped(x-1,y,z)]+
                    velocity[IndexClamped(x,y+1,z)]+velocity[IndexClamped(x,y-1,z)]+
                    velocity[IndexClamped(x,y,z+1)]+velocity[IndexClamped(x,y,z-1)]) / 6f;
                nextVelocity[i] = Vector3.Lerp(advected, neighbour, Mathf.Clamp01(viscosity * dt));
                nextVelocity[i] = Vector3.Lerp(nextVelocity[i], ambient, Mathf.Clamp01(ambientBlend * dt));
                nextDensity[i] = SampleScalar(density, back) * Mathf.Exp(-densityDecay * dt);
            }
            Swap(ref velocity, ref nextVelocity); Swap(ref density, ref nextDensity);
            Project(dt);
            ApplyBoundaries();
            Steps++; LastStepMilliseconds = (Time.realtimeSinceStartup - start) * 1000;
        }

        void Project(float dt)
        {
            float invX2 = 1 / (cell.x * cell.x), invY2 = 1 / (cell.y * cell.y), invZ2 = 1 / (cell.z * cell.z);
            for (int z = 0; z < resolutionZ; z++) for (int y = 0; y < resolutionY; y++) for (int x = 0; x < resolutionX; x++)
            {
                int i = Index(x, y, z);
                divergence[i] = (velocity[IndexClamped(x+1,y,z)].x-velocity[IndexClamped(x-1,y,z)].x)*0.5f/cell.x +
                    (velocity[IndexClamped(x,y+1,z)].y-velocity[IndexClamped(x,y-1,z)].y)*0.5f/cell.y +
                    (velocity[IndexClamped(x,y,z+1)].z-velocity[IndexClamped(x,y,z-1)].z)*0.5f/cell.z;
                pressure[i] = 0;
            }
            for (int iteration = 0; iteration < pressureIterations; iteration++)
            {
                for (int z = 0; z < resolutionZ; z++) for (int y = 0; y < resolutionY; y++) for (int x = 0; x < resolutionX; x++)
                {
                    int i = Index(x, y, z);
                    if(solid[i]){nextPressure[i]=0;continue;}
                    float weighted = (pressure[IndexClamped(x + 1, y, z)] + pressure[IndexClamped(x - 1, y, z)]) * invX2 +
                        (pressure[IndexClamped(x, y + 1, z)] + pressure[IndexClamped(x, y - 1, z)]) * invY2 +
                        (pressure[IndexClamped(x, y, z + 1)] + pressure[IndexClamped(x, y, z - 1)]) * invZ2;
                    float denominator = 2 * (invX2 + invY2 + invZ2);
                    nextPressure[i] = (weighted - divergence[i] / Mathf.Max(0.0001f, dt)) / denominator;
                }
                Swap(ref pressure, ref nextPressure);
            }
            for (int z = 0; z < resolutionZ; z++) for (int y = 0; y < resolutionY; y++) for (int x = 0; x < resolutionX; x++)
            {
                int i = Index(x, y, z);
                Vector3 gradient = new Vector3(
                    (pressure[IndexClamped(x + 1, y, z)] - pressure[IndexClamped(x - 1, y, z)]) / (2 * cell.x),
                    (pressure[IndexClamped(x, y + 1, z)] - pressure[IndexClamped(x, y - 1, z)]) / (2 * cell.y),
                    (pressure[IndexClamped(x, y, z + 1)] - pressure[IndexClamped(x, y, z - 1)]) / (2 * cell.z));
                velocity[i] = solid[i]?Vector3.zero:Vector3.ClampMagnitude(velocity[i]-gradient*dt,maximumSpeed);
            }
        }

        void ApplyBoundaries()
        {
            for(int z=0;z<resolutionZ;z++)for(int y=0;y<resolutionY;y++)for(int x=0;x<resolutionX;x++)
            {
                int i=Index(x,y,z);if(solid[i]){velocity[i]=Vector3.zero;continue;}
                var v=velocity[i];
                if(x==0||x==resolutionX-1||solid[IndexClamped(x-1,y,z)]||solid[IndexClamped(x+1,y,z)])v.x=0;
                if(y==0||y==resolutionY-1||solid[IndexClamped(x,y-1,z)]||solid[IndexClamped(x,y+1,z)])v.y=0;
                if(z==0||z==resolutionZ-1||solid[IndexClamped(x,y,z-1)]||solid[IndexClamped(x,y,z+1)])v.z=0;
                velocity[i]=v;
            }
        }

        [ContextMenu("Reset Flow And Rebuild Static Boundaries")]
        public void ResetSimulation(){Allocate();}

        Vector3 SampleVector(Vector3[] values, Vector3 world)
        {
            GridFloat(world, out float gx, out float gy, out float gz);
            int x0 = Mathf.FloorToInt(gx), y0 = Mathf.FloorToInt(gy), z0 = Mathf.FloorToInt(gz);
            float tx = gx - x0, ty = gy - y0, tz = gz - z0; Vector3 value = Vector3.zero;
            for (int dz = 0; dz <= 1; dz++) for (int dy = 0; dy <= 1; dy++) for (int dx = 0; dx <= 1; dx++)
                value += values[IndexClamped(x0 + dx, y0 + dy, z0 + dz)] * ((dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty) * (dz == 0 ? 1 - tz : tz));
            return value;
        }

        float SampleScalar(float[] values, Vector3 world)
        {
            GridFloat(world, out float gx, out float gy, out float gz);
            int x0 = Mathf.FloorToInt(gx), y0 = Mathf.FloorToInt(gy), z0 = Mathf.FloorToInt(gz);
            float tx = gx - x0, ty = gy - y0, tz = gz - z0, value = 0;
            for (int dz = 0; dz <= 1; dz++) for (int dy = 0; dy <= 1; dy++) for (int dx = 0; dx <= 1; dx++)
                value += values[IndexClamped(x0 + dx, y0 + dy, z0 + dz)] * ((dx == 0 ? 1 - tx : tx) * (dy == 0 ? 1 - ty : ty) * (dz == 0 ? 1 - tz : tz));
            return value;
        }

        void GridFloat(Vector3 p, out float x, out float y, out float z)
        {
            Vector3 q = Vector3.Max(Vector3.zero, Vector3.Min(worldBounds.size, p - min));
            x = q.x / cell.x; y = q.y / cell.y; z = q.z / cell.z;
        }
        void Grid(Vector3 p, out int x, out int y, out int z) { GridFloat(p, out float fx, out float fy, out float fz); x = Mathf.RoundToInt(fx); y = Mathf.RoundToInt(fy); z = Mathf.RoundToInt(fz); }
        Vector3 CellWorld(int x, int y, int z) => min + new Vector3(Mathf.Clamp(x, 0, resolutionX - 1) * cell.x, Mathf.Clamp(y, 0, resolutionY - 1) * cell.y, Mathf.Clamp(z, 0, resolutionZ - 1) * cell.z);
        int Index(int x, int y, int z) => (z * resolutionY + y) * resolutionX + x;
        int IndexClamped(int x, int y, int z) => Index(Mathf.Clamp(x, 0, resolutionX - 1), Mathf.Clamp(y, 0, resolutionY - 1), Mathf.Clamp(z, 0, resolutionZ - 1));
        bool Inside(int x, int y, int z) => x >= 0 && x < resolutionX && y >= 0 && y < resolutionY && z >= 0 && z < resolutionZ;
        static void Swap<T>(ref T a, ref T b) { T t = a; a = b; b = t; }

        void OnDrawGizmosSelected()
        {
            if (!drawGizmos) return;
            Gizmos.color = new Color(0.1f, 0.6f, 1, 0.25f); Gizmos.DrawWireCube(worldBounds.center, worldBounds.size);
            if (velocity == null) return;
            int stride = Mathf.Max(1, resolutionX / 8);
            for (int z = 0; z < resolutionZ; z += stride) for (int y = 0; y < resolutionY; y += stride) for (int x = 0; x < resolutionX; x += stride)
            {
                Vector3 p = CellWorld(x, y, z), v = velocity[Index(x, y, z)]; Gizmos.DrawLine(p, p + v * 0.5f);
            }
        }
    }
}
