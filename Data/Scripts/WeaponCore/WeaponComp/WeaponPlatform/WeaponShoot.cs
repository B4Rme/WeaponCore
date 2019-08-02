﻿using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;
using WeaponCore.Projectiles;
using WeaponCore.Support;
using CollisionLayers = Sandbox.Engine.Physics.MyPhysics.CollisionLayers;

namespace WeaponCore.Platform
{
    public partial class Weapon
    {
        internal void Shoot()
        {
            var session = Comp.MyAi.MySession;
            var tick = session.Tick;
            var bps = System.Values.HardPoint.Loading.BarrelsPerShot;
            //DsDebugDraw.DrawLine(Comp.MyPivotPos, Comp.MyPivotPos + (Comp.MyPivotDir * 5000), Color.Purple, 0.1f);
            //if (this == Comp.TrackingWeapon) DsDebugDraw.DrawLine(Comp.MyPivotPos, TargetPos, Color.White, 0.1f);
            //else DsDebugDraw.DrawLine(Comp.MyPivotPos, TargetPos, Color.Black, 0.1f);

            if (System.BurstMode)
            {
                if (_shots > System.Values.HardPoint.Loading.ShotsInBurst)
                {
                    if (tick - _lastShotTick > System.Values.HardPoint.Loading.DelayAfterBurst) _shots = 1;
                    else
                    {
                        if (AvCapable && RotateEmitter != null && RotateEmitter.IsPlaying) StopRotateSound();
                        return;
                    }
                }
                _lastShotTick = tick;
            }

            if (AvCapable && (!PlayTurretAv || Comp.MyAi.MySession.Tick60))
                PlayTurretAv = Vector3D.DistanceSquared(session.CameraPos, Comp.MyPivotPos) < System.HardPointSoundMaxDistSqr;

            if (System.BarrelAxisRotation) MovePart(-1 * bps);

            if (ShotCounter == 0 && _newCycle) _rotationTime = 0;
            if (ShotCounter++ >= _ticksPerShot - 1) ShotCounter = 0;

            _newCycle = false;

            _ticksUntilShoot++;
            if (ShotCounter != 0) return;
            _shots++;

            if (!IsShooting) StartShooting();

            if (_ticksUntilShoot < System.DelayToFire) return;

            if (_shotsInCycle++ == _numOfBarrels - 1)
            {
                _shotsInCycle = 0;
                _newCycle = true;
            }

            if (!Comp.Gunner  && !Casting && tick - Comp.LastRayCastTick > 59) ShootRayCheck();

            var isStatic = Comp.Physics.IsStatic;
            for (int i = 0; i < bps; i++)
            {
                var current = NextMuzzle;
                var muzzle = Muzzles[current];
                var lastTick = muzzle.LastUpdateTick;
                var recentMovement = lastTick >= _posChangedTick && lastTick - _posChangedTick < 10;
                if (recentMovement || _posChangedTick > lastTick)
                {
                    var dummy = Dummies[current];
                    var newInfo = dummy.Info;
                    muzzle.Direction = newInfo.Direction;
                    muzzle.Position = newInfo.Position;
                    muzzle.LastUpdateTick = tick;
                }

                if (!System.EnergyAmmo)
                {
                    if (CurrentAmmo == 0) continue;
                    CurrentAmmo--;
                }

                if (System.HasBackKickForce && !isStatic)
                    Comp.MyGrid.Physics.AddForce(MyPhysicsForceType.APPLY_WORLD_IMPULSE_AND_WORLD_ANGULAR_IMPULSE, -muzzle.Direction * System.Values.Ammo.BackKickForce, muzzle.Position, Vector3D.Zero);

                muzzle.LastShot = tick;
                if (PlayTurretAv) BarrelAvUpdater.Add(muzzle, tick, true);
                lock (session.Projectiles.Wait[session.ProCounter])
                {
                    for (int j = 0; j < System.Values.HardPoint.Loading.TrajectilesPerBarrel; j++)
                    {
                        if (System.Values.HardPoint.DeviateShotAngle > 0)
                        {
                            var dirMatrix = Matrix.CreateFromDir(muzzle.Direction);
                            var randomFloat1 = MyUtils.GetRandomFloat(-System.Values.HardPoint.DeviateShotAngle, System.Values.HardPoint.DeviateShotAngle);
                            var randomFloat2 = MyUtils.GetRandomFloat(0.0f, 6.283185f);

                            muzzle.DeviatedDir = Vector3.TransformNormal(-new Vector3(
                                    MyMath.FastSin(randomFloat1) * MyMath.FastCos(randomFloat2),
                                    MyMath.FastSin(randomFloat1) * MyMath.FastSin(randomFloat2),
                                    MyMath.FastCos(randomFloat1)), dirMatrix);
                        }
                        else muzzle.DeviatedDir = muzzle.Direction;

                        session.Projectiles.ProjectilePool[session.ProCounter].AllocateOrCreate(out var pro);
                        pro.System = System;
                        pro.FiringCube = Comp.MyCube;
                        pro.Origin = muzzle.Position;
                        pro.OriginUp = Comp.MyPivotUp;
                        pro.PredictedTargetPos = TargetPos;
                        pro.Direction = muzzle.DeviatedDir;
                        pro.State = Projectile.ProjectileState.Start;
                        pro.Target = Target.Entity;
                        pro.Ai = Comp.MyAi;
                        pro.WeaponId = WeaponId;
                        pro.MuzzleId = muzzle.MuzzleId;
                        pro.IsBeamWeapon = System.IsBeamWeapon;

                        if (System.ModelId != -1)
                        {
                            session.Projectiles.EntityPool[session.ProCounter][System.ModelId].AllocateOrCreate(out var ent);
                            if (!ent.InScene)
                            {
                                ent.InScene = true;
                                ent.Render.AddRenderObjects();
                            }
                            pro.Entity = ent;
                        }
                        if (session.ProCounter++ >= session.Projectiles.Wait.Length - 1) session.ProCounter = 0;
                    }
                }

                CurrentHeat += System.HeatPShot;

                if(CurrentHeat > System.MaxHeat) Overheated = true;

                if (i == bps) NextMuzzle++;

                NextMuzzle = (NextMuzzle + (System.Values.HardPoint.Loading.SkipBarrels + 1)) % _numOfBarrels;
            }
        }

        private void ShootRayCheck()
        {
            Comp.LastRayCastTick = Comp.MyAi.MySession.Tick;
            var masterWeapon = TrackTarget ? this : Comp.TrackingWeapon;
            if (Target.Entity == null || Target.Entity.MarkedForClose)
            {
                Log.Line($"{System.WeaponName} - ShootRayCheckFail - target null or marked");
                masterWeapon.TargetExpired = true;
                if (masterWeapon != this) TargetExpired = true;
                return;
            }

            var targetPos = Target.Entity.PositionComp.WorldMatrix.Translation;
            if (Vector3D.DistanceSquared(targetPos, Comp.MyPivotPos) > System.MaxTrajectorySqr)
            {
                Log.Line($"{System.WeaponName} - ShootRayCheck Fail - out of range");
                masterWeapon.TargetExpired = true;
                if (masterWeapon !=  this) TargetExpired = true;
                return;
            }
            if (!TrackingAi && !ValidTarget(this, Target.Entity))
            {
                Log.Line($"{System.WeaponName} - ShootRayCheck Fail - not trackingAi and notValid target");
                masterWeapon.TargetExpired = true;
                if (masterWeapon != this) TargetExpired = true;
                return;
            }
            Casting = true;
            MyAPIGateway.Physics.CastRayParallel(ref Comp.MyPivotPos, ref targetPos, CollisionLayers.DefaultCollisionLayer, ShootRayCheckCallBack);
        }

        public void MovePart(int time)
        {
            BarrelMove = true;
            double radiansPerShot;
            if(System.DegROF && CurrentHeat > (System.MaxHeat *.8)) _timePerShot = (3600d / System.Values.HardPoint.Loading.RateOfFire) / (CurrentHeat/System.MaxHeat);
            if (_timePerShot > 0.999999 && _timePerShot < 1.000001) radiansPerShot = 0.06666666666;
            else  radiansPerShot = 2 * Math.PI / _numOfBarrels;
            var radians = radiansPerShot / _timePerShot;
            var axis = System.Values.HardPoint.RotateBarrelAxis;
            MatrixD rotationMatrix;
            if (axis == 1) rotationMatrix = MatrixD.CreateRotationX(radians * _rotationTime);
            else if (axis == 2 ) rotationMatrix = MatrixD.CreateRotationY(radians * _rotationTime);
            else if (axis == 3) rotationMatrix = MatrixD.CreateRotationZ(radians * _rotationTime);
            else return;

            _rotationTime += time;
            rotationMatrix.Translation = _localTranslation;
            EntityPart.PositionComp.LocalMatrix = rotationMatrix;
            BarrelMove = false;
            if (PlayTurretAv && RotateEmitter != null && !RotateEmitter.IsPlaying)
                StartRotateSound();
        }

        public void ShootRayCheckCallBack(IHitInfo hitInfo)
        {
            Casting = false;
            var masterWeapon = TrackTarget ? this : Comp.TrackingWeapon;
            if (hitInfo?.HitEntity == null || (hitInfo.HitEntity != Target.Entity && hitInfo.HitEntity != Target.Entity.Parent))
            {
                if (hitInfo?.HitEntity == null && DelayCeaseFire)
                {
                    Log.Line($"{System.WeaponName} - ShootRayCheck Sucess - due to null DelayCeaseFire");
                    return;
                }
                if ((hitInfo?.HitEntity != null))
                {
                    var rootAsGrid = hitInfo.HitEntity as MyCubeGrid;
                    var parentAsGrid = hitInfo.HitEntity?.Parent as MyCubeGrid;
                    if (hitInfo.HitEntity.Parent != null) Log.Line("parent is not null!");
                    if (rootAsGrid == null && parentAsGrid == null)
                    {
                        Log.Line($"{System.WeaponName} - ShootRayCheck Success - junk: {((MyEntity)hitInfo.HitEntity).DebugName}");
                        return;
                    }

                    var grid = parentAsGrid ?? rootAsGrid;
                    if (grid == Comp.MyGrid)
                    {
                        Log.Line($"{System.WeaponName} - ShootRayCheck Sucess - own grid: {grid?.DebugName}");
                        return;
                    }

                    if (!GridTargetingAi.GridEnemy(Comp.MyCube, grid))
                    {
                        if (!grid.IsInSameLogicalGroupAs(Comp.MyGrid))
                        {
                            Log.Line($"{System.WeaponName} - ShootRayCheck fail - friendly grid: {grid?.DebugName} - {grid?.DebugName}");
                            masterWeapon.TargetExpired = true;
                            if (masterWeapon != this) TargetExpired = true;
                        }
                        return;
                    }
                    Log.Line($"{System.WeaponName} - ShootRayCheck Success - non-friendly target in the way of primary target, shoot through: {((MyEntity)hitInfo.HitEntity).DebugName}");
                    return;
                }
                if (hitInfo?.HitEntity == null) Log.Line($"{System.WeaponName} - ShootRayCheck Fail - null");
                else if (hitInfo.HitEntity != null) Log.Line($"{System.WeaponName} - ShootRayCheck Fail - General: {((MyEntity)hitInfo.HitEntity).DebugName}");
                else Log.Line($"{System.WeaponName} - ShootRayCheck fail - Unknown");
                masterWeapon.TargetExpired = true;
                if (masterWeapon != this) TargetExpired = true;
            }
            else if (System.SortBlocks && hitInfo.HitEntity is MyCubeGrid grid && Target.Entity.GetTopMostParent() == grid)
            {
                var maxChange = hitInfo.HitEntity.PositionComp.LocalAABB.HalfExtents.Min();
                var targetPos = Target.Entity.PositionComp.WorldMatrix.Translation;
                //var distanceToTarget = Vector3D.Distance(hitInfo.Position, targetPos);
                var weaponPos = Comp.MyPivotPos;

                double rayDist;
                Vector3D.Distance(ref weaponPos, ref targetPos, out rayDist);
                var newHitShortDist = rayDist * (1 - hitInfo.Fraction);
                var distanceToTarget = rayDist * hitInfo.Fraction;

                var shortDistExceed = newHitShortDist - Target.HitShortDist > maxChange;
                var escapeDistExceed = distanceToTarget - Target.OrigDistance > Target.OrigDistance;
                if (shortDistExceed || escapeDistExceed)
                {
                    masterWeapon.TargetExpired = true;
                    if (masterWeapon != this) TargetExpired = true;
                    if (shortDistExceed) Log.Line($"{System.WeaponName} - ShootRayCheck fail - Distance to sorted block exceeded");
                    else Log.Line($"{System.WeaponName} - ShootRayCheck fail - Target distance to escape has been met");
                }
            }
        }
    }
}
