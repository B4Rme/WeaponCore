﻿using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Game.ModAPI.Interfaces;
using VRageMath;
using WeaponCore.Support;
using static WeaponCore.Support.HitEntity.Type;
using static WeaponCore.Support.WeaponDefinition.AmmoDef.AreaDamageDef.AreaEffectType;
using static WeaponCore.Support.WeaponDefinition.AmmoDef.DamageScaleDef;

namespace WeaponCore.Projectiles
{
    public partial class Projectiles
    {
        internal bool GetAllEntitiesInLine(Projectile p, LineD beam)
        {
            var shieldByPass = p.Info.AmmoDef.DamageScales.Shields.Type == ShieldDef.ShieldType.Bypass;
            var shieldFullBypass = shieldByPass && p.Info.AmmoDef.Const.ShieldBypassMod >= 1;
            var genericFields = p.Info.EwarActive && (p.Info.AmmoDef.Const.AreaEffect == DotField || p.Info.AmmoDef.Const.AreaEffect == PushField || p.Info.AmmoDef.Const.AreaEffect == PullField);
            var ai = p.Info.Ai;
            var found = false;
            var lineCheck = p.Info.AmmoDef.Const.CollisionIsLine && !p.Info.EwarActive && !p.Info.TriggeredPulse;
            var planetBeam = beam;
            planetBeam.To = p.Info.AmmoDef.Const.IsBeamWeapon && p.MaxTrajectory > 1500 ? beam.From + (beam.Direction * 1500) : beam.To;
            bool projetileInShield = false;
            for (int i = 0; i < p.SegmentList.Count; i++)
            {
                var ent = p.SegmentList[i].Element;
                var grid = ent as MyCubeGrid;
                var destroyable = ent as IMyDestroyableObject;
                var voxel = ent as MyVoxelBase;
                if (ent is IMyCharacter && p.Info.EwarActive && !genericFields) continue;
                if (grid != null && p.SmartsOn && p.Info.Ai.MyGrid.IsSameConstructAs(grid) || ent.MarkedForClose || !ent.InScene || ent == p.Info.Ai.MyShield) continue;

                if (!shieldFullBypass && !p.ShieldBypassed || p.Info.EwarActive && (p.Info.AmmoDef.Const.AreaEffect == DotField && p.Info.AmmoDef.Const.AreaEffect == EmpField))
                {
                    var shieldInfo = p.Info.Ai.Session.SApi?.MatchEntToShieldFastExt(ent, true);
                    if (shieldInfo != null)
                    {
                        double? dist = null;
                        if (ent.Physics != null && ent.Physics.IsPhantom)
                        {
                            dist = MathFuncs.IntersectEllipsoid(shieldInfo.Value.Item3.Item1, shieldInfo.Value.Item3.Item2, new RayD(beam.From, beam.Direction));
                            if (p.Info.Target.IsProjectile && Vector3D.Transform(p.Info.Target.Projectile.Position, shieldInfo.Value.Item3.Item1).LengthSquared() <= 1)
                                projetileInShield = true;
                        }

                        if (dist != null && (dist.Value < beam.Length || p.Info.EwarActive) && !p.Info.Ai.MyGrid.IsSameConstructAs(shieldInfo.Value.Item1.CubeGrid))
                        {
                            var hitEntity = HitEntityPool.Get();
                            hitEntity.Clean();
                            hitEntity.Info = p.Info;
                            found = true;
                            if (shieldByPass) p.ShieldBypassed = true;
                            hitEntity.Entity = (MyEntity)shieldInfo.Value.Item1;
                            hitEntity.Intersection = beam;
                            hitEntity.EventType = Shield;
                            hitEntity.SphereCheck = !lineCheck;
                            hitEntity.PruneSphere = p.PruneSphere;
                            hitEntity.HitPos = beam.From + (beam.Direction * dist.Value);
                            hitEntity.HitDist = dist;

                            p.Info.HitList.Add(hitEntity);
                        }
                        else continue;
                    }
                }

                if ((ent == ai.MyPlanet && (p.LinePlanetCheck || p.DynamicGuidance || p.CachedPlanetHit)) || ent.Physics != null && !ent.Physics.IsPhantom && !ent.IsPreview && (grid != null || voxel != null || destroyable != null))
                {
                    var extFrom = beam.From - (beam.Direction * (ent.PositionComp.WorldVolume.Radius * 2));
                    var extBeam = new LineD(extFrom, beam.To);

                    var rotMatrix = Quaternion.CreateFromRotationMatrix(ent.WorldMatrix);
                    var obb = new MyOrientedBoundingBoxD(ent.PositionComp.WorldAABB.Center, ent.PositionComp.LocalAABB.HalfExtents, rotMatrix);
                    if (lineCheck && obb.Intersects(ref extBeam) == null || !lineCheck && !obb.Intersects(ref p.PruneSphere)) continue;
                    Vector3D? voxelHit = null;
                    if (voxel != null)
                    {
                        if (voxel.RootVoxel != voxel) continue;
                        if (voxel == ai.MyPlanet)
                        {
                            if (p.CachedPlanetHit)
                            {
                                IHitInfo cachedPlanetResult;
                                if (p.Info.WeaponCache.VoxelHits[p.CachedId].NewResult(out cachedPlanetResult))
                                    voxelHit = cachedPlanetResult.Position;
                                else
                                    continue;
                            }

                            if (p.LinePlanetCheck)
                            {
                                var check = false;
                                var surfacePos = ai.MyPlanet.GetClosestSurfacePointGlobal(ref p.Position);
                                var planetCenter = ai.MyPlanet.PositionComp.WorldAABB.Center;
                                double surfaceToCenter;
                                Vector3D.DistanceSquared(ref surfacePos, ref planetCenter, out surfaceToCenter);
                                double endPointToCenter;
                                Vector3D.DistanceSquared(ref p.Position, ref planetCenter, out endPointToCenter);
                                double startPointToCenter;
                                Vector3D.DistanceSquared(ref p.Info.Origin, ref planetCenter, out startPointToCenter);

                                var prevEndPointToCenter = p.PrevEndPointToCenterSqr;
                                Vector3D.DistanceSquared(ref surfacePos, ref p.Position, out p.PrevEndPointToCenterSqr);

                                if (surfaceToCenter > endPointToCenter || p.PrevEndPointToCenterSqr <= (planetBeam.Length * planetBeam.Length) || endPointToCenter > startPointToCenter && prevEndPointToCenter > p.DistanceToTravelSqr || surfaceToCenter > Vector3D.DistanceSquared(planetCenter, p.LastPosition)) check = true;
                                if (check)
                                {
                                    using (voxel.Pin())
                                        voxel.GetIntersectionWithLine(ref planetBeam, out voxelHit);
                                }
                            }
                        }
                        else
                            using (voxel.Pin())
                                voxel.GetIntersectionWithLine(ref beam, out voxelHit);

                        if (!voxelHit.HasValue)
                            continue;
                    }
                    var hitEntity = HitEntityPool.Get();
                    hitEntity.Clean();
                    hitEntity.Info = p.Info;
                    hitEntity.Entity = ent;
                    hitEntity.Intersection = beam;
                    hitEntity.SphereCheck = !lineCheck;
                    hitEntity.PruneSphere = p.PruneSphere;

                    if (voxelHit != null)
                    {
                        var hitPos = voxelHit.Value;
                        hitEntity.HitPos = hitPos;

                        double dist;
                        Vector3D.Distance(ref beam.From, ref hitPos, out dist);
                        hitEntity.HitDist = dist;
                    }

                    if (grid != null)
                    {
                        if (!p.Info.AmmoDef.Const.IsBeamWeapon && beam.Length <= grid.GridSize * 2 && grid.IsSameConstructAs(p.Info.Target.FiringCube.CubeGrid))
                        {
                            MyCube cube;
                            if (!(grid.TryGetCube(grid.WorldToGridInteger(p.Position), out cube) && cube.CubeBlock != p.Info.Target.FiringCube.SlimBlock || grid.TryGetCube(grid.WorldToGridInteger(p.LastPosition), out cube) && cube.CubeBlock != p.Info.Target.FiringCube.SlimBlock))
                                continue;
                        }
                        if (!(p.Info.EwarActive && p.Info.AmmoDef.Const.EwarEffect))
                            hitEntity.EventType = Grid;
                        else if (!p.Info.AmmoDef.Const.Pulse)
                            hitEntity.EventType = Effect;
                        else
                            hitEntity.EventType = Field;

                        if (p.Info.AmmoDef.Const.AreaEffect == DotField)
                            hitEntity.DamageOverTime = true;
                    }
                    else if (destroyable != null)
                        hitEntity.EventType = Destroyable;
                    else if (voxel != null)
                        hitEntity.EventType = Voxel;
                    found = true;
                    p.Info.HitList.Add(hitEntity);
                }
            }

            if (p.Info.Target.IsProjectile && !p.Info.AmmoDef.Const.EwarEffect && !projetileInShield)
            {
                var detonate = p.State == Projectile.ProjectileState.Detonate;
                var hitTolerance = detonate ? p.Info.AmmoDef.AreaEffect.Detonation.DetonationRadius : p.Info.AmmoDef.Const.AreaEffectSize > p.Info.AmmoDef.Const.CollisionSize ? p.Info.AmmoDef.Const.AreaEffectSize : p.Info.AmmoDef.Const.CollisionSize;
                var useLine = p.Info.AmmoDef.Const.CollisionIsLine && !detonate && p.Info.AmmoDef.Const.AreaEffectSize <= 0;

                var sphere = new BoundingSphereD(p.Info.Target.Projectile.Position, p.Info.Target.Projectile.Info.AmmoDef.Const.CollisionSize);
                sphere.Include(new BoundingSphereD(p.Info.Target.Projectile.LastPosition, 1));
                var rayCheck = useLine && sphere.Intersects(new RayD(p.LastPosition, p.Info.Direction)) != null;
                var testSphere = p.PruneSphere;
                testSphere.Radius = hitTolerance;

                if (rayCheck || sphere.Intersects(testSphere))
                    found = ProjectileHit(p, p.Info.Target.Projectile, lineCheck, ref beam);
            }
            p.SegmentList.Clear();

            return found && GenerateHitInfo(p);
        }

        internal bool ProjectileHit(Projectile attacker, Projectile target, bool lineCheck, ref LineD beam)
        {
            var hitEntity = HitEntityPool.Get();
            hitEntity.Clean();
            hitEntity.Info = attacker.Info;
            hitEntity.EventType = HitEntity.Type.Projectile;
            hitEntity.Hit = true;
            hitEntity.Projectile = target;
            hitEntity.SphereCheck = !lineCheck;
            hitEntity.PruneSphere = attacker.PruneSphere;
            double dist;
            Vector3D.Distance(ref beam.From, ref target.Position, out dist);
            hitEntity.HitDist = dist;

            hitEntity.Intersection = new LineD(attacker.LastPosition, attacker.LastPosition + (attacker.Info.Direction * dist));
            hitEntity.HitPos = hitEntity.Intersection.To;

            attacker.Info.HitList.Add(hitEntity);
            return true;
        }

        internal bool GenerateHitInfo(Projectile p)
        {
            var count = p.Info.HitList.Count;
            if (count > 1) p.Info.HitList.Sort((x, y) => GetEntityCompareDist(x, y, V3Pool.Get(), p.Info));
            else GetEntityCompareDist(p.Info.HitList[0], null, V3Pool.Get(), p.Info);

            var pulseTrigger = false;
            for (int i = p.Info.HitList.Count - 1; i >= 0; i--)
            {
                var ent = p.Info.HitList[i];
                if (!ent.Hit)
                {
                    if (ent.PulseTrigger) pulseTrigger = true;
                    p.Info.HitList.RemoveAtFast(i);
                    HitEntityPool.Return(ent);
                }
                else break;
            }

            if (pulseTrigger)
            {
                p.Info.TriggeredPulse = true;
                p.DistanceToTravelSqr = p.Info.DistanceTraveled * p.Info.DistanceTraveled;
                p.Velocity = Vector3D.Zero;
                p.Hit.HitPos = p.Position + p.Info.Direction * p.Info.AmmoDef.Const.EwarTriggerRange;
                p.Info.HitList.Clear();
                return false;
            }

            var finalCount = p.Info.HitList.Count;
            if (finalCount > 0)
            {
                var hitEntity = p.Info.HitList[0];
                p.LastHitPos = hitEntity.HitPos;
                p.LastHitEntVel = hitEntity.Projectile?.Velocity ?? hitEntity.Entity?.Physics?.LinearVelocity ?? Vector3D.Zero;
                p.Info.LastHitShield = hitEntity.EventType == Shield;

                IMySlimBlock hitBlock = null;
                if (p.Info.AmmoDef.Const.VirtualBeams && hitEntity.Entity is MyCubeGrid)
                    hitBlock = hitEntity.Blocks[0];
                p.Hit = new Hit { Block = hitBlock, Entity = hitEntity.Entity, Projectile = null, HitPos = p.LastHitPos ?? Vector3D.Zero, HitVelocity = p.LastHitEntVel ?? Vector3D.Zero };
                if (p.EnableAv) p.Info.AvShot.Hit = p.Hit;

                return true;
            }
            return false;
        }

        internal int GetEntityCompareDist(HitEntity x, HitEntity y, List<Vector3I> slims, ProInfo info)
        {
            var xDist = double.MaxValue;
            var yDist = double.MaxValue;
            var beam = x.Intersection;
            var count = y != null ? 2 : 1;
            var eWarPulse = info.AmmoDef.Const.Ewar && info.AmmoDef.Const.Pulse;
            var triggerEvent = eWarPulse && !info.TriggeredPulse && info.AmmoDef.Const.EwarTriggerRange > 0;
            for (int i = 0; i < count; i++)
            {
                var isX = i == 0;

                MyEntity ent;
                HitEntity hitEnt;
                if (isX)
                {
                    hitEnt = x;
                    ent = hitEnt.Entity;
                }
                else
                {
                    hitEnt = y;
                    ent = hitEnt.Entity;
                }

                var dist = double.MaxValue;


                var shield = ent as IMyTerminalBlock;
                var grid = ent as MyCubeGrid;
                var voxel = ent as MyVoxelBase;
                if (triggerEvent && (info.Ai.Targets.ContainsKey(ent) || shield != null))
                    hitEnt.PulseTrigger = true;
                else if (hitEnt.Projectile != null)
                    dist = hitEnt.HitDist.Value;
                else if (shield != null)
                {
                    hitEnt.Hit = true;
                    dist = hitEnt.HitDist.Value;
                }
                else if (grid != null)
                {
                    if (hitEnt.Hit)
                    {
                        dist = Vector3D.Distance(hitEnt.Intersection.From, hitEnt.HitPos.Value);
                        hitEnt.HitDist = dist;
                    }
                    else
                    {
                        if (hitEnt.SphereCheck || info.EwarActive)
                        {
                            var ewarActive = hitEnt.EventType == Field || hitEnt.EventType == Effect;

                            var hitPos = !ewarActive ? hitEnt.PruneSphere.Center + (hitEnt.Intersection.Direction * hitEnt.PruneSphere.Radius) : hitEnt.PruneSphere.Center;
                            if (grid.IsSameConstructAs(hitEnt.Info.Ai.MyGrid) && Vector3D.DistanceSquared(hitPos, hitEnt.Info.Origin) <= grid.GridSize * grid.GridSize)
                                continue;

                            if (!ewarActive)
                                GetAndSortBlocksInSphere(hitEnt.Info.AmmoDef, hitEnt.Info.Ai, grid, hitEnt.PruneSphere, false, hitEnt.Blocks);

                            if (hitEnt.Blocks.Count > 0 || ewarActive)
                            {
                                dist = 0;
                                hitEnt.HitDist = dist;
                                hitEnt.Hit = true;
                                hitEnt.HitPos = hitPos;

                            }
                        }
                        else
                        {
                            grid.RayCastCells(beam.From, beam.To, slims, null, true, true);
                            var closestBlockFound = false;
                            var rotMatrix = Quaternion.CreateFromRotationMatrix(grid.PositionComp.WorldMatrixRef);
                            var hitSelf = grid.IsSameConstructAs(hitEnt.Info.Target.FiringCube.CubeGrid);
                            for (int j = 0; j < slims.Count; j++)
                            {
                                var firstBlock = grid.GetCubeBlock(slims[j]) as IMySlimBlock;
                                if (firstBlock != null && !firstBlock.IsDestroyed && firstBlock != hitEnt.Info.Target.FiringCube.SlimBlock)
                                {
                                    hitEnt.Blocks.Add(firstBlock);
                                    if (closestBlockFound) continue;
                                    MyOrientedBoundingBoxD obb;
                                    var fat = firstBlock.FatBlock;
                                    if (fat != null)
                                        obb = new MyOrientedBoundingBoxD(fat.Model.BoundingBox, fat.PositionComp.WorldMatrixRef);
                                    else
                                    {
                                        Vector3D center;
                                        firstBlock.ComputeWorldCenter(out center);
                                        Vector3 halfExt;
                                        firstBlock.ComputeScaledHalfExtents(out halfExt);
                                        var blockBox = new BoundingBoxD(-halfExt, halfExt);
                                        obb = new MyOrientedBoundingBoxD(center, blockBox.HalfExtents, rotMatrix);
                                    }

                                    var hitDist = obb.Intersects(ref beam) ?? Vector3D.Distance(beam.From, obb.Center);
                                    var hitPos = beam.From + (beam.Direction * hitDist);
                                    if (hitSelf)
                                    {
                                        if (Vector3D.DistanceSquared(hitPos, hitEnt.Info.Origin) <= grid.GridSize * 3) {
                                            hitEnt.Blocks.Clear();
                                        }
                                        else {
                                            dist = hitDist;
                                            hitEnt.HitDist = dist;
                                            hitEnt.Hit = true;
                                            hitEnt.HitPos = hitPos;
                                        }
                                        break;
                                    }

                                    dist = hitDist;
                                    hitEnt.HitDist = dist;
                                    hitEnt.Hit = true;
                                    hitEnt.HitPos = hitPos;
                                    closestBlockFound = true;
                                }
                            }
                        }
                    }
                }
                else if (voxel != null)
                {
                    hitEnt.Hit = true;
                    dist = hitEnt.HitDist.Value;
                    hitEnt.HitDist = dist;
                }
                else if (ent is IMyDestroyableObject)
                {
                    if (hitEnt.Hit) dist = Vector3D.Distance(hitEnt.Intersection.From, hitEnt.HitPos.Value);
                    else
                    {
                        if (hitEnt.SphereCheck || info.EwarActive)
                        {
                            var ewarActive = hitEnt.EventType == Field || hitEnt.EventType == Effect;

                            dist = 0;
                            hitEnt.HitDist = dist;
                            hitEnt.Hit = true;
                            var hitPos = !ewarActive ? hitEnt.PruneSphere.Center + (hitEnt.Intersection.Direction * hitEnt.PruneSphere.Radius) : hitEnt.PruneSphere.Center;
                            hitEnt.HitPos = hitPos;
                        }
                        else
                        {
                            var rotMatrix = Quaternion.CreateFromRotationMatrix(ent.PositionComp.WorldMatrixRef);
                            var obb = new MyOrientedBoundingBoxD(ent.PositionComp.WorldAABB.Center, ent.PositionComp.LocalAABB.HalfExtents, rotMatrix);
                            dist = obb.Intersects(ref beam) ?? double.MaxValue;
                            if (dist < double.MaxValue)
                            {
                                hitEnt.Hit = true;
                                hitEnt.HitPos = beam.From + (beam.Direction * dist);
                                hitEnt.HitDist = dist;
                            }
                        }
                    }
                }

                if (isX) xDist = dist;
                else yDist = dist;
            }
            V3Pool.Return(slims);
            return xDist.CompareTo(yDist);
        }

        internal static void GetAndSortBlocksInSphere(WeaponDefinition.AmmoDef ammoDef, GridAi ai, MyCubeGrid grid, BoundingSphereD sphere, bool fatOnly, List<IMySlimBlock> blocks)
        {
            var matrixNormalizedInv = grid.PositionComp.WorldMatrixNormalizedInv;
            Vector3D result;
            Vector3D.Transform(ref sphere.Center, ref matrixNormalizedInv, out result);
            var localSphere = new BoundingSphere(result, (float)sphere.Radius);
            var fieldType = ammoDef.AreaEffect.AreaEffect;
            var hitPos = sphere.Center;
            if (fatOnly)
            {
                foreach (var cube in ai.Session.GridToFatMap[grid].MyCubeBocks)
                {
                    if (!(cube is IMyTerminalBlock)) continue;
                    switch (fieldType)
                    {
                        case JumpNullField:
                            if (!(cube is MyJumpDrive)) continue;
                            break;
                        case EnergySinkField:
                            if (!(cube is IMyPowerProducer)) continue;
                            break;
                        case AnchorField:
                            if (!(cube is MyThrust)) continue;
                            break;
                        case NavField:
                            if (!(cube is MyGyro)) continue;
                            break;
                        case OffenseField:
                            if (!(cube is IMyGunBaseUser)) continue;
                            break;
                        case EmpField:
                        case DotField:
                            break;
                        default: continue;
                    }
                    var block = cube.SlimBlock as IMySlimBlock;
                    if (!new BoundingBox(block.Min * grid.GridSize - grid.GridSizeHalf, block.Max * grid.GridSize + grid.GridSizeHalf).Intersects(localSphere))
                        continue;
                    blocks.Add(block);
                }
            }
            else
            {
                //usage:
                //var dict = (Dictionary<Vector3I, IMySlimBlock>)GetHackDict((IMySlimBlock) null);
                var tmpList = ai.Session.SlimPool.Get();
                Session.GetBlocksInsideSphereFast(grid, ref sphere, true, tmpList);

                for (int i = 0; i < tmpList.Count; i++)
                    blocks.Add(tmpList[i]);

                ai.Session.SlimPool.Return(tmpList);
            }

            blocks.Sort((a, b) =>
            {
                var aPos = grid.GridIntegerToWorld(a.Position);
                var bPos = grid.GridIntegerToWorld(b.Position);
                return Vector3D.DistanceSquared(aPos, hitPos).CompareTo(Vector3D.DistanceSquared(bPos, hitPos));
            });
        }
        public static object GetHackDict<TVal>(TVal valueType) => new Dictionary<Vector3I, TVal>();

        /*
        private static void PrefetchVoxelPhysicsIfNeeded(Projectile p)
        {
            var ray = new LineD(p.Origin, p.Origin + p.Direction * p.MaxTrajectory, p.MaxTrajectory);
            var lineD = new LineD(new Vector3D(Math.Floor(ray.From.X) * 0.5, Math.Floor(ray.From.Y) * 0.5, Math.Floor(ray.From.Z) * 0.5), new Vector3D(Math.Floor(p.Direction.X * 50.0), Math.Floor(p.Direction.Y * 50.0), Math.Floor(p.Direction.Z * 50.0)));
            if (p.VoxelRayCache.IsItemPresent(lineD.GetHash(), (int)MyAPIGateway.Session.ElapsedPlayTime.TotalMilliseconds, true))
                return;
            using (MyUtils.ReuseCollection(ref p.EntityRaycastResult))
            {
                MyGamePruningStructure.GetAllEntitiesInRay(ref ray, p.EntityRaycastResult, MyEntityQueryType.Static);
                foreach (var segmentOverlapResult in p.EntityRaycastResult)
                    (segmentOverlapResult.Element as MyPlanet)?.PrefetchShapeOnRay(ref ray);
            }
        }
        */
    }
}
