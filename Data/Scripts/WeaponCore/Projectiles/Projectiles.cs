﻿using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRageMath;
using WeaponCore.Support;
using static WeaponCore.Projectiles.Projectile;
using static WeaponCore.Support.AvShot;
namespace WeaponCore.Projectiles
{
    public partial class Projectiles
    {
        private const float StepConst = MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
        internal readonly Session Session;
        internal readonly MyConcurrentPool<Fragments> ShrapnelPool = new MyConcurrentPool<Fragments>(32);
        internal readonly MyConcurrentPool<Fragment> FragmentPool = new MyConcurrentPool<Fragment>(32);
        internal readonly List<Fragments> ShrapnelToSpawn = new List<Fragments>(32);

        internal readonly MyConcurrentPool<List<MyEntity>> CheckPool = new MyConcurrentPool<List<MyEntity>>(30);
        internal readonly Stack<Projectile> ProjectilePool = new Stack<Projectile>(2048);

        internal readonly CachingHashSet<Projectile> ActiveProjetiles = new CachingHashSet<Projectile>();
        internal readonly MyConcurrentPool<HitEntity> HitEntityPool = new MyConcurrentPool<HitEntity>(32);
        internal readonly MyConcurrentPool<ProInfo> VirtInfoPool = new MyConcurrentPool<ProInfo>(256);
        internal readonly List<Projectile> CleanUp = new List<Projectile>(32);

        internal readonly MyConcurrentPool<List<Vector3I>> V3Pool = new MyConcurrentPool<List<Vector3I>>(32);
        internal readonly List<DeferedAv> DeferedAvDraw = new List<DeferedAv>();
        internal ulong CurrentProjectileId;

        internal Projectiles(Session session)
        {
            Session = session;
        }


        internal void Update() // Methods highly inlined due to keen's mod profiler
        {
            if (!Session.DedicatedServer) 
                DeferedAvStateUpdates(Session);

            Clean();
            SpawnFragments();

            ActiveProjetiles.ApplyChanges();

            UpdateState();
            CheckHits();
            if (!Session.DedicatedServer) 
                UpdateAv();
        }

        internal void Clean()
        {
            for (int j = 0; j < CleanUp.Count; j++)
            {
                var p = CleanUp[j];
                for (int i = 0; i < p.VrPros.Count; i++)
                {
                    var virtInfo = p.VrPros[i];
                    virtInfo.Info.Clean(Session.Tick);
                    VirtInfoPool.Return(virtInfo.Info);
                }
                p.VrPros.Clear();

                if (p.DynamicGuidance)
                    DynTrees.UnregisterProjectile(p);

                p.PruningProxyId = -1;

                p.Info.Clean(Session.Tick);
                ProjectilePool.Push(p);
                ActiveProjetiles.Remove(p);
            }

            CleanUp.Clear();
        }

        internal void PrepFragmentEntities()
        {
            for (int i = 0; i < Session.FragmentsNeedingEntities.Count; i++)
            {
                var frag = Session.FragmentsNeedingEntities[i];
                if (frag.AmmoDef.Const.PrimeModel && frag.PrimeEntity == null) frag.PrimeEntity = frag.AmmoDef.Const.PrimeEntityPool.Get();
                if (frag.AmmoDef.Const.TriggerModel && frag.TriggerEntity == null) frag.TriggerEntity =  Session.TriggerEntityPool.Get();
            }
            Session.FragmentsNeedingEntities.Clear();
        }

        private void SpawnFragments()
        {
            for (int j = 0; j < ShrapnelToSpawn.Count; j++)
                ShrapnelToSpawn[j].Spawn();
            ShrapnelToSpawn.Clear();
        }

        private void UpdateState()
        {
            foreach (var p in ActiveProjetiles)
            {
                p.Info.Age++;
                p.Active = false;
                switch (p.State)
                {
                    case ProjectileState.Destroy:
                        p.DestroyProjectile();
                        continue;
                    case ProjectileState.Dead:
                        continue;
                    case ProjectileState.Start:
                        p.Start();
                        break;
                    case ProjectileState.OneAndDone:
                    case ProjectileState.Depleted:
                    case ProjectileState.Detonate:
                        p.ProjectileClose();
                        continue;
                }
                if (p.Info.Target.IsProjectile)
                    if (p.Info.Target.Projectile.State != ProjectileState.Alive)
                        p.UnAssignProjectile(true);

                if (p.FeelsGravity)
                {
                    var totalGravity = MyParticlesManager.CalculateGravityInPoint(p.Position);
                    p.Velocity += (totalGravity * p.Info.AmmoDef.Trajectory.GravityMultiplier) * Projectile.StepConst;
                    Vector3D.Normalize(ref p.Velocity, out p.Info.Direction);
                }

                if (p.AccelLength > 0 && !p.Info.TriggeredPulse)
                {
                    if (p.SmartsOn) p.RunSmart();
                    else
                    {
                        var accel = true;
                        Vector3D newVel;
                        if (p.FieldTime > 0)
                        {
                            var distToMax = p.MaxTrajectory - p.Info.DistanceTraveled;

                            var stopDist = p.VelocityLengthSqr / 2 / (p.StepPerSec);
                            if (distToMax <= stopDist)
                                accel = false;

                            newVel = accel ? p.Velocity + p.AccelVelocity : p.Velocity - p.AccelVelocity;
                            p.VelocityLengthSqr = newVel.LengthSquared();

                            if (accel && p.VelocityLengthSqr > p.MaxSpeedSqr) newVel = p.Info.Direction * p.MaxSpeed;
                            else if (!accel && distToMax <= 0)
                            {
                                newVel = Vector3D.Zero;
                                p.VelocityLengthSqr = 0;
                            }
                        }
                        else
                        {
                            newVel = p.Velocity + p.AccelVelocity;
                            p.VelocityLengthSqr = newVel.LengthSquared();
                            if (p.VelocityLengthSqr > p.MaxSpeedSqr) newVel = p.Info.Direction * p.MaxSpeed;
                        }

                        p.Velocity = newVel;
                    }
                }

                if (p.State == ProjectileState.OneAndDone)
                {
                    p.LastPosition = p.Position;
                    var beamEnd = p.Position + (p.Info.Direction * p.MaxTrajectory);
                    p.TravelMagnitude = p.Position - beamEnd;
                    p.Position = beamEnd;
                }
                else
                {
                    if (p.ConstantSpeed || p.VelocityLengthSqr > 0)
                        p.LastPosition = p.Position;

                    p.TravelMagnitude = p.Velocity * StepConst;
                    p.Position += p.TravelMagnitude;
                }

                p.Info.PrevDistanceTraveled = p.Info.DistanceTraveled;
                p.Info.DistanceTraveled += Math.Abs(Vector3D.Dot(p.Info.Direction, p.Velocity * StepConst));
                if (p.ModelState == EntityState.Exists)
                {
                    var up = MatrixD.Identity.Up;
                    MatrixD matrix;
                    MatrixD.CreateWorld(ref p.Position, ref p.Info.VisualDir, ref up, out matrix);
                    if (p.Info.AmmoDef.Const.PrimeModel)
                        p.Info.AvShot.PrimeMatrix = matrix;
                    if (p.Info.AmmoDef.Const.TriggerModel && p.Info.TriggerGrowthSteps < p.Info.AmmoDef.Const.AreaEffectSize)
                        p.Info.TriggerMatrix = matrix;

                    if (p.EnableAv && p.AmmoEffect != null && p.Info.AmmoDef.Const.AmmoParticle && p.Info.AmmoDef.Const.PrimeModel)
                    {
                        var offVec = p.Position + Vector3D.Rotate(p.Info.AmmoDef.AmmoGraphics.Particles.Ammo.Offset, p.Info.AvShot.PrimeMatrix);
                        p.AmmoEffect.WorldMatrix = p.Info.AvShot.PrimeMatrix;
                        p.AmmoEffect.SetTranslation(ref offVec);
                    }
                }
                else if (!p.ConstantSpeed && p.EnableAv && p.AmmoEffect != null && p.Info.AmmoDef.Const.AmmoParticle)
                    p.AmmoEffect.Velocity = p.Velocity;

                if (p.DynamicGuidance)
                {
                    if (p.PruningProxyId != -1)
                    {
                        var sphere = new BoundingSphereD(p.Position, p.Info.AmmoDef.Const.AreaEffectSize);
                        BoundingBoxD result;
                        BoundingBoxD.CreateFromSphere(ref sphere, out result);
                        Session.ProjectileTree.MoveProxy(p.PruningProxyId, ref result, p.Velocity);
                    }
                }

                if (p.State != ProjectileState.OneAndDone)
                {
                    if (!p.SmartsOn && p.Info.Age > p.Info.AmmoDef.Const.MaxLifeTime)
                    {
                        p.DistanceToTravelSqr = p.Info.DistanceTraveled * p.Info.DistanceTraveled;
                        p.EarlyEnd = true;
                    }

                    if (p.Info.DistanceTraveled * p.Info.DistanceTraveled >= p.DistanceToTravelSqr)
                    {
                        p.AtMaxRange = true;
                        if (p.FieldTime > 0)
                        {
                            p.FieldTime--;
                            if (p.Info.AmmoDef.Const.IsMine && !p.MineSeeking && !p.MineActivated)
                            {
                                if (p.EnableAv) p.Info.AvShot.Cloaked = p.Info.AmmoDef.Trajectory.Mines.Cloak;
                                p.MineSeeking = true;
                            }
                        }
                    }
                }
                else p.AtMaxRange = true;
                if (p.Info.AmmoDef.Const.Ewar)
                    p.RunEwar();

                p.Active = true;
            }
        }

        private void CheckHits()
        {
            foreach (var p in ActiveProjetiles)
            {
                p.Miss = false;

                if (!p.Active || (int)p.State > 3) continue;
                var triggerRange = p.Info.AmmoDef.Const.EwarTriggerRange > 0 && !p.Info.TriggeredPulse ? p.Info.AmmoDef.Const.EwarTriggerRange : 0;
                var useEwarSphere = triggerRange > 0 || p.Info.EwarActive;
                var beam = useEwarSphere ? new LineD(p.Position + (-p.Info.Direction * p.Info.AmmoDef.Const.EwarTriggerRange), p.Position + (p.Info.Direction * p.Info.AmmoDef.Const.EwarTriggerRange)) : new LineD(p.LastPosition, p.Position);
                if ((p.FieldTime <= 0 && p.State != ProjectileState.OneAndDone && p.Info.DistanceTraveled * p.Info.DistanceTraveled >= p.DistanceToTravelSqr))
                {
                    var dInfo = p.Info.AmmoDef.AreaEffect.Detonation;

                    p.PruneSphere.Center = p.Position;
                    p.PruneSphere.Radius = dInfo.DetonationRadius;

                    if (p.MoveToAndActivate || dInfo.DetonateOnEnd && (!dInfo.ArmOnlyOnHit || p.Info.ObjectsHit > 0))
                    {
                        var checkList = CheckPool.Get();
                        MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref p.PruneSphere, checkList,
                            p.PruneQuery);
                        for (int i = 0; i < checkList.Count; i++)
                            p.SegmentList.Add(new MyLineSegmentOverlapResult<MyEntity>
                            { Distance = 0, Element = checkList[i] });
                        if (p.Info.System.TrackProjectile)
                            foreach (var lp in p.Info.Ai.LiveProjectile)
                                if (p.PruneSphere.Contains(lp.Position) != ContainmentType.Disjoint && lp != p.Info.Target.Projectile)
                                    ProjectileHit(p, lp, p.Info.AmmoDef.Const.CollisionIsLine, ref beam);

                        checkList.Clear();
                        CheckPool.Return(checkList);
                        p.State = ProjectileState.Detonate;
                        p.ForceHitParticle = true;
                    }
                    else
                        p.State = ProjectileState.Detonate;
                }

                var sphere = false;
                var line = false;

                if (p.MineSeeking && !p.MineTriggered)
                    p.SeekEnemy();
                else if (useEwarSphere)
                {
                    if (p.Info.EwarActive) {
                        p.PruneSphere = new BoundingSphereD(p.Position, 0).Include(new BoundingSphereD(p.LastPosition, 0));
                        var currentRadius = p.Info.TriggerGrowthSteps < p.Info.AmmoDef.Const.AreaEffectSize ? p.Info.TriggerMatrix.Scale.AbsMax() : p.Info.AmmoDef.Const.AreaEffectSize;
                        if (p.PruneSphere.Radius < currentRadius) {
                            p.PruneSphere.Center = p.Position;
                            p.PruneSphere.Radius = currentRadius;
                        }
                    }
                    else
                    {
                        p.PruneSphere = new BoundingSphereD(p.Position, triggerRange);
                    }

                    sphere = true;
                }
                else if (p.Info.AmmoDef.Const.CollisionIsLine)
                {
                    p.PruneSphere.Center = p.Position;
                    p.PruneSphere.Radius = p.Info.AmmoDef.Const.CollisionSize;
                    if (p.Info.AmmoDef.Const.IsBeamWeapon || p.PruneSphere.Contains(new BoundingSphereD(p.Info.Origin, p.DeadZone)) == ContainmentType.Disjoint)
                        line = true;
                }
                else
                {
                    p.PruneSphere = new BoundingSphereD(p.Position, 0).Include(new BoundingSphereD(p.LastPosition, 0));
                    if (p.PruneSphere.Radius < p.Info.AmmoDef.Const.CollisionSize)
                    {
                        p.PruneSphere.Center = p.Position;
                        p.PruneSphere.Radius = p.Info.AmmoDef.Const.CollisionSize;
                    }
                }

                if (sphere)
                {
                    if (p.DynamicGuidance && p.PruneQuery == MyEntityQueryType.Dynamic && p.Info.Ai.Session.Tick60) 
                        p.CheckForNearVoxel(60);

                    var checkList = CheckPool.Get();
                    MyGamePruningStructure.GetAllTopMostEntitiesInSphere(ref p.PruneSphere, checkList, p.PruneQuery);
                    for (int i = 0; i < checkList.Count; i++)
                        p.SegmentList.Add(new MyLineSegmentOverlapResult<MyEntity> { Distance = 0, Element = checkList[i] });

                    checkList.Clear();
                    CheckPool.Return(checkList);
                }
                else if (line)
                {
                    if (p.DynamicGuidance && p.PruneQuery == MyEntityQueryType.Dynamic && p.Info.Ai.Session.Tick60) p.CheckForNearVoxel(60);
                    MyGamePruningStructure.GetTopmostEntitiesOverlappingRay(ref beam, p.SegmentList, p.PruneQuery);
                }

                if (p.Info.Target.IsProjectile || p.SegmentList.Count > 0)
                {
                    if (GetAllEntitiesInLine(p, beam) && p.Intersected())
                        continue;
                }

                p.Miss = true;
                p.Info.HitList.Clear();
            }
        }

        private void UpdateAv()
        {
            foreach (var p in ActiveProjetiles)
            {
                if (!p.Miss || (int)p.State > 3) continue;
                if (p.Info.MuzzleId == -1)
                {
                    p.CreateFakeBeams(true);
                    continue;
                }
                if (!p.EnableAv) continue;

                if (p.SmartsOn)
                {
                    if (p.EnableAv && Vector3D.Dot(p.Info.VisualDir, p.AccelDir) < Session.VisDirToleranceCosine)
                    {
                        p.VisualStep += 0.0025;
                        if (p.VisualStep > 1) p.VisualStep = 1;

                        Vector3D lerpDir;
                        Vector3D.Lerp(ref p.Info.VisualDir, ref p.AccelDir, p.VisualStep, out lerpDir);
                        Vector3D.Normalize(ref lerpDir, out p.Info.VisualDir);
                    }
                    else if (p.EnableAv && Vector3D.Dot(p.Info.VisualDir, p.AccelDir) >= Session.VisDirToleranceCosine)
                    {
                        p.Info.VisualDir = p.AccelDir;
                        p.VisualStep = 0;
                    }
                }
                else if (p.FeelsGravity) p.Info.VisualDir = p.Info.Direction;

                if (p.LineOrNotModel)
                {
                    if (p.State == ProjectileState.OneAndDone)
                        DeferedAvDraw.Add(new DeferedAv { Info = p.Info, StepSize = 0, VisualLength = p.MaxTrajectory, TracerFront = p.Position});
                    else if (p.ModelState == EntityState.None && p.Info.AmmoDef.Const.AmmoParticle && !p.Info.AmmoDef.Const.DrawLine)
                    {
                        if (p.AtMaxRange) p.ShortStepAvUpdate(true, false);
                        else
                            DeferedAvDraw.Add(new DeferedAv { Info = p.Info, StepSize = p.Info.DistanceTraveled - p.Info.PrevDistanceTraveled, VisualLength = p.Info.AmmoDef.Const.CollisionSize, TracerFront = p.Position});
                    }
                    else
                    {
                        p.Info.ProjectileDisplacement += Math.Abs(Vector3D.Dot(p.Info.Direction, (p.Velocity - p.StartSpeed) * StepConst));
                        var displaceDiff = p.Info.ProjectileDisplacement - p.TracerLength;
                        if (p.Info.ProjectileDisplacement < p.TracerLength && Math.Abs(displaceDiff) > 0.0001)
                        {
                            if (p.AtMaxRange) p.ShortStepAvUpdate(false, false);
                            else
                            {
                                DeferedAvDraw.Add(new DeferedAv { Info = p.Info, StepSize = p.Info.DistanceTraveled - p.Info.PrevDistanceTraveled, VisualLength = p.Info.ProjectileDisplacement, TracerFront = p.Position});
                            }
                        }
                        else
                        {
                            if (p.AtMaxRange) p.ShortStepAvUpdate(false, false);
                            else
                                DeferedAvDraw.Add(new DeferedAv { Info = p.Info, StepSize = p.Info.DistanceTraveled - p.Info.PrevDistanceTraveled, VisualLength = p.TracerLength, TracerFront = p.Position });
                        }
                    }
                }

                if (p.Info.ModelOnly)
                    DeferedAvDraw.Add(new DeferedAv { Info = p.Info, StepSize = p.Info.DistanceTraveled - p.Info.PrevDistanceTraveled, VisualLength = p.TracerLength, TracerFront = p.Position});

                if (p.Info.AmmoDef.Const.AmmoParticle)
                {
                    p.TestSphere.Center = p.Position;
                    if (p.Info.AvShot.OnScreen != Screen.None || Session.Camera.IsInFrustum(ref p.TestSphere))
                    {
                        if (!p.Info.AmmoDef.Const.IsBeamWeapon && !p.ParticleStopped && p.AmmoEffect != null && p.Info.AmmoDef.Const.AmmoParticleShrinks)
                            p.AmmoEffect.UserScale = MathHelper.Clamp(MathHelper.Lerp(p.BaseAmmoParticleScale, 0, p.Info.AvShot.DistanceToLine / p.Info.AmmoDef.AmmoGraphics.Particles.Hit.Extras.MaxDistance), 0.05f, p.BaseAmmoParticleScale);

                        if ((p.ParticleStopped || p.ParticleLateStart))
                            p.PlayAmmoParticle();
                    }
                    else if (!p.ParticleStopped && p.AmmoEffect != null)
                        p.DisposeAmmoEffect(false, true);
                }
            }
        }
    }
}
