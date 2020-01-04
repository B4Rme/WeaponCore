﻿using System.Collections.Generic;
using Sandbox.Engine.Physics;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using WeaponCore.Support;
using WeaponCore.Projectiles;
using static WeaponCore.Support.HitEntity.Type;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace WeaponCore.Support
{
    internal class ProInfo
    {
        internal readonly Target Target = new Target();
        internal readonly List<HitEntity> HitList = new List<HitEntity>();
        internal AvShot AvShot;
        internal WeaponSystem System;
        internal GridAi Ai;
        internal MyEntity PrimeEntity;
        internal MyEntity TriggerEntity;
        internal CompGroupOverrides Overrides;
        internal WeaponFrameCache WeaponCache;
        internal Vector3D ShooterVel;
        internal Vector3D Origin;
        internal Vector3D OriginUp;
        internal Vector3D Direction;
        internal int TriggerGrowthSteps;
        internal int WeaponId;
        internal int MuzzleId;
        internal int ObjectsHit;
        internal int Age;
        internal double DistanceTraveled;
        internal double PrevDistanceTraveled;
        internal double ProjectileDisplacement;
        internal float BaseDamagePool;
        internal float AreaEffectDamage;
        internal float DetonationDamage;
        internal float BaseHealthPool;
        internal bool IsShrapnel;
        internal bool EnableGuidance = true;
        internal bool LastHitShield;
        internal MatrixD TriggerMatrix = MatrixD.Identity;


        internal void InitVirtual(WeaponSystem system, GridAi ai, MyEntity primeEntity, MyEntity triggerEntity, Target target, int weaponId, int muzzleId, Vector3D origin, Vector3D direction)
        {
            System = system;
            Ai = ai;
            PrimeEntity = primeEntity;
            TriggerEntity = triggerEntity;
            Target.Entity = target.Entity;
            Target.Projectile = target.Projectile;
            Target.FiringCube = target.FiringCube;
            WeaponId = weaponId;
            MuzzleId = muzzleId;
            Direction = direction;
            Origin = origin;
        }

        internal void Clean()
        {
            Target.Reset();
            HitList.Clear();
            AvShot = null;
            System = null;
            PrimeEntity = null;
            TriggerEntity = null;
            Ai = null;
            WeaponCache = null;
            LastHitShield = false;
            IsShrapnel = false;
            TriggerGrowthSteps = 0;
            WeaponId = 0;
            MuzzleId = 0;
            Age = 0;
            ProjectileDisplacement = 0;
            EnableGuidance = true;
            Direction = Vector3D.Zero;
            Origin = Vector3D.Zero;
            ShooterVel = Vector3D.Zero;
            TriggerMatrix = MatrixD.Identity;
        }
    }

    internal struct VirtualProjectile
    {
        internal ProInfo Info;
        internal AvShot VisualShot;
    }

    internal class HitEntity
    {
        internal enum Type
        {
            Shield,
            Grid,
            Voxel,
            Destroyable,
            Stale,
            Projectile,
            Field,
            Effect,
        }

        public readonly List<IMySlimBlock> Blocks = new List<IMySlimBlock>();
        public MyEntity Entity;
        internal Projectile Projectile;
        public ProInfo Info;
        public LineD Beam;
        public bool Hit;
        public bool SphereCheck;
        public bool DamageOverTime;
        public BoundingSphereD PruneSphere;
        public Vector3D? HitPos;
        public double? HitDist;
        public Type EventType;

        public void Clean()
        {
            Entity = null;
            Projectile = null;

            Beam.Length = 0;
            Beam.Direction = Vector3D.Zero;
            Beam.From = Vector3D.Zero;
            Beam.To = Vector3D.Zero;
            Blocks.Clear();
            Hit = false;
            HitPos = null;
            HitDist = null;
            Info = null;
            EventType = Stale;
            PruneSphere = new BoundingSphereD();
            SphereCheck = false;
            DamageOverTime = false;
        }
    }

    internal struct Hit
    {
        internal IMySlimBlock Block;
        internal MyEntity Entity;
        internal Projectile Projectile;
        internal Vector3D HitPos;
        internal Vector3D HitVelocity;
    }

    internal class VoxelParallelHits
    {
        internal uint RequestTick;
        internal uint ResultTick;
        internal uint LastTick;
        internal IHitInfo HitInfo;
        private bool _start;
        private uint _startTick;
        private int _miss;
        private int _maxDelay;
        private bool _idle;
        private Vector3D _endPos = Vector3D.MinValue;

        internal bool Cached(LineD lineTest, ProInfo i)
        {
            double dist;
            Vector3D.DistanceSquared(ref _endPos, ref lineTest.To, out dist);

            _maxDelay = i.MuzzleId == -1 ? i.System.Barrels.Length : 1;

            var thisTick = (uint)(MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds * Session.TickTimeDiv);
            _start = thisTick - LastTick > _maxDelay || dist > 5;

            LastTick = thisTick;

            if (_start)
            {
                _startTick = thisTick;
                _endPos = lineTest.To;
            }

            var runTime = thisTick - _startTick;

            var fastPath = runTime > (_maxDelay * 3) + 1;
            var useCache = runTime > (_maxDelay * 3) + 2;
            if (fastPath)
            {
                if (_miss > 1)
                {
                    if (_idle && _miss % 120 == 0) _idle = false;
                    else _idle = true;

                    //Log.Line($"{t.System.WeaponName} - idle:{_idle} - miss:{_miss} - runtime:{runTime} - {_idle} - {thisTick}");
                    if (_idle) return true;
                }
                RequestTick = thisTick;
                MyAPIGateway.Physics.CastRayParallel(ref lineTest.From, ref lineTest.To, CollisionLayers.VoxelCollisionLayer, Results);
            }
            //if (!useCache) Log.Line($"not using cache: {t.System.WeaponName} - miss:{_miss} - runTime:{runTime} - dist:{dist} - newDir:{_endDir} - oldDir:{oldDir}");
            return useCache;
        }

        internal void Results(IHitInfo info)
        {
            ResultTick = (uint)(MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds * Session.TickTimeDiv);
            if (info == null)
            {
                _miss++;
                HitInfo = null;
                return;
            }

            var voxel = info.HitEntity as MyVoxelBase;
            if (voxel?.RootVoxel is MyPlanet)
            {
                HitInfo = info;
                _miss = 0;
                return;
            }
            _miss++;
            HitInfo = null;
        }

        internal bool NewResult(out IHitInfo cachedPlanetResult)
        {
            cachedPlanetResult = null;
            var thisTick = (uint)(MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds * Session.TickTimeDiv);

            if (HitInfo == null)
            {
                _miss++;
                return false;
            }

            if (thisTick > RequestTick + _maxDelay)
                return false;

            //Log.Line($"newResult: {thisTick} - {RequestTick} - {_maxDelay} - {RequestTick + _maxDelay} - {thisTick - (RequestTick + _maxDelay)}");
            cachedPlanetResult = HitInfo;
            return true;
        }
    }

    internal class WeaponFrameCache
    {
        internal bool VirtualHit;
        internal int Hits;
        internal double HitDistance;
        internal HitEntity HitEntity = new HitEntity();
        internal IMySlimBlock HitBlock;
        internal int VirutalId = -1;
        internal VoxelParallelHits[] VoxelHits;

        internal WeaponFrameCache(int size)
        {
            VoxelHits = new VoxelParallelHits[size];
            for (int i = 0; i < size; i++) VoxelHits[i] = new VoxelParallelHits();
        }
    }

    internal class Fragments
    {
        internal List<Fragment> Sharpnel = new List<Fragment>();

        internal void Init(Projectile p, MyConcurrentPool<Fragment> fragPool)
        {
            for (int i = 0; i < p.Info.System.Values.Ammo.Shrapnel.Fragments; i++)
            {
                var frag = fragPool.Get();

                frag.System = p.Info.System;
                frag.Ai = p.Info.Ai;
                frag.Target = p.Info.Target.Entity;
                frag.WeaponId = p.Info.WeaponId;
                frag.MuzzleId = p.Info.MuzzleId;
                frag.FiringCube = p.Info.Target.FiringCube;
                frag.Guidance = p.Info.EnableGuidance;
                frag.Origin = p.Position;
                frag.OriginUp = p.Info.OriginUp;
                frag.PredictedTargetPos = p.PredictedTargetPos;
                frag.Velocity = p.Velocity;
                var dirMatrix = Matrix.CreateFromDir(p.Direction);
                var shape = p.Info.System.Values.Ammo.Shrapnel.Shape;
                float neg;
                float pos;
                switch (shape)
                {
                    case Shrapnel.ShrapnelShape.Cone:
                        neg = 0;
                        pos = 15;
                        break;
                    case Shrapnel.ShrapnelShape.HalfMoon:
                        neg = 90;
                        pos = 90;
                        break;
                    default:
                        neg = 180;
                        pos = 180;
                        break;
                }
                var negValue = MathHelper.ToRadians(neg);
                var posValue = MathHelper.ToRadians(pos);
                var randomFloat1 = MyUtils.GetRandomFloat(-negValue, posValue);
                var randomFloat2 = MyUtils.GetRandomFloat(0.0f, MathHelper.TwoPi);

                var shrapnelDir = Vector3.TransformNormal(-new Vector3(
                    MyMath.FastSin(randomFloat1) * MyMath.FastCos(randomFloat2),
                    MyMath.FastSin(randomFloat1) * MyMath.FastSin(randomFloat2),
                    MyMath.FastCos(randomFloat1)), dirMatrix);

                frag.Direction = shrapnelDir;

                Sharpnel.Add(frag);
            }
        }

        internal void Spawn()
        {
            Session session = null;
            for (int i = 0; i < Sharpnel.Count; i++)
            {
                var frag = Sharpnel[i];
                session = frag.Ai.Session;
                Projectile p;
                frag.Ai.Session.Projectiles.ProjectilePool.AllocateOrCreate(out p);
                p.Info.System = frag.System;
                p.Info.Ai = frag.Ai;
                p.Info.Target.Entity = frag.Target;
                p.Info.Target.FiringCube = frag.FiringCube;
                p.Info.IsShrapnel = true;
                p.Info.EnableGuidance = frag.Guidance;
                p.Info.WeaponId = frag.WeaponId;
                p.Info.MuzzleId = frag.MuzzleId;
                p.Info.Origin = frag.Origin;
                p.Info.OriginUp = frag.OriginUp;
                p.PredictedTargetPos = frag.PredictedTargetPos;
                p.Direction = frag.Direction;
                p.State = Projectiles.Projectile.ProjectileState.Start;

                p.StartSpeed = frag.Velocity;
                frag.Ai.Session.Projectiles.FragmentPool.Return(frag);
            }

            session?.Projectiles.ShrapnelPool.Return(this);
            Sharpnel.Clear();
        }
    }

    internal class Fragment
    {
        public WeaponSystem System;
        public GridAi Ai;
        public MyEntity Target;
        public MyCubeBlock FiringCube;
        public Vector3D Origin;
        public Vector3D OriginUp;
        public Vector3D Direction;
        public Vector3D Velocity;
        public Vector3D PredictedTargetPos;
        public int WeaponId;
        public int MuzzleId;
        internal bool Guidance;
    }
}