﻿using System;
using System.Collections.Generic;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ModAPI;
using VRageMath;
using WeaponCore.Support;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using static WeaponCore.Projectiles.Projectiles;
namespace WeaponCore
{
    public partial class Session
    {
        private void DrawLists(List<DrawProjectile> drawList)
        {
            var sFound = false;
            for (int i = 0; i < drawList.Count; i++)
            {
                var p = drawList[i];
                var wDef = p.Weapon.WeaponType;
                var line = p.Projectile;
                if (p.Shrink)
                {
                    sFound = true;
                    var shrink = _shrinkPool.Get();
                    shrink.Init(wDef, line, p.ReSizeSteps, p.LineReSizeLen);
                    _shrinking.Add(shrink);
                }

                if (InTurret)
                {
                    var matrix = MatrixD.CreateFromDir(line.Direction);
                    matrix.Translation = line.From;
                    TransparentRenderExt.DrawTransparentCylinder(ref matrix, wDef.GraphicDef.ProjectileWidth, wDef.GraphicDef.ProjectileWidth, (float)line.Length, 6, wDef.GraphicDef.ProjectileColor, wDef.GraphicDef.ProjectileColor, wDef.GraphicDef.ProjectileMaterial, wDef.GraphicDef.ProjectileMaterial, 0f, BlendTypeEnum.Standard, BlendTypeEnum.Standard, false);
                }
                else MyTransparentGeometry.AddLocalLineBillboard(wDef.GraphicDef.ProjectileMaterial, wDef.GraphicDef.ProjectileColor, line.From, 0, line.Direction, (float)line.Length, wDef.GraphicDef.ProjectileWidth);
            }
            drawList.Clear();
            if (sFound) _shrinking.ApplyAdditions();
        }

        private void Shrink()
        {
            var sRemove = false;
            foreach (var s in _shrinking)
            {
                var line = s.GetLine();
                if (line.HasValue)
                {
                    if (InTurret)
                    {
                        var matrix = MatrixD.CreateFromDir(line.Value.Direction);
                        matrix.Translation = line.Value.From;
                        TransparentRenderExt.DrawTransparentCylinder(ref matrix, s.WepDef.GraphicDef.ProjectileWidth, s.WepDef.GraphicDef.ProjectileWidth, (float)line.Value.Length, 6, s.WepDef.GraphicDef.ProjectileColor, s.WepDef.GraphicDef.ProjectileColor, s.WepDef.GraphicDef.ProjectileMaterial, s.WepDef.GraphicDef.ProjectileMaterial, 0f, BlendTypeEnum.Standard, BlendTypeEnum.Standard, false);
                    }
                    else MyTransparentGeometry.AddLocalLineBillboard(s.WepDef.GraphicDef.ProjectileMaterial, s.WepDef.GraphicDef.ProjectileColor, line.Value.From, 0, line.Value.Direction, (float)line.Value.Length, s.WepDef.GraphicDef.ProjectileWidth);
                }
                else
                {
                    _shrinking.Remove(s);
                    sRemove = true;
                }
            }
            if (sRemove) _shrinking.ApplyRemovals();
        }

        private void UpdateWeaponPlatforms()
        {
            foreach (var aiPair in GridTargetingAIs)
            {
                var grid = aiPair.Key;
                var ai = aiPair.Value;
                foreach (var basePair in ai.WeaponBase)
                {

                    var myCube = basePair.Key;
                    var weapon = basePair.Value;
                    if (!weapon.MainInit || !weapon.State.Value.Online) continue;

                    for (int j = 0; j < weapon.Platform.Weapons.Length; j++)
                    {
                        var w = weapon.Platform.Weapons[j];
                        w.Gunner = ControlledEntity == weapon.MyCube;
                        if (!w.Gunner)
                        {
                            if (w.Target != null)
                            {
                                //DsDebugDraw.DrawLine(w.EntityPart.PositionComp.WorldAABB.Center, w.Target.PositionComp.WorldAABB.Center, Color.Black, 0.01f);
                            }
                            if (w.TrackTarget && w.SeekTarget) ai.SelectTarget(ref w.Target, w);
                            if (w.TurretMode && w.Target != null) w.Rotate(w.WeaponType.TurretDef.RotateSpeed);
                            if (w.TrackTarget && w.ReadyToTrack)
                            {
                                //logic.Turret.TrackTarget(w.Target);
                                //logic.Turret.EnableIdleRotation = false;
                            }
                        }
                        else
                        {
                            InTurret = true;
                            if (MouseButtonPressed)
                            {
                                var currentAmmo = weapon.Gun.GunBase.CurrentAmmo;
                                if (currentAmmo <= 1) weapon.Gun.GunBase.CurrentAmmo += 1;
                            }
                        }
                        if (w.ReadyToShoot && !w.Gunner || w.Gunner && (j == 0 && MouseButtonLeft || j == 1 && MouseButtonRight)) w.Shoot();
                    }
                }
            }
        }

        private void DrawBeam(DrawProjectile pInfo)
        {
            var cameraPos = MyAPIGateway.Session.Camera.Position;
            var beamScaledDir = pInfo.Projectile.From - pInfo.Projectile.To;
            var beamCenter = pInfo.Projectile.From + -(beamScaledDir * 0.5f);
            var distToBeam = Vector3D.DistanceSquared(cameraPos, beamCenter);
            if (distToBeam > 25000000) return;
            var beamSphereRadius = pInfo.Projectile.Length * 0.5;
            var beamSphere = new BoundingSphereD(beamCenter, beamSphereRadius);
            if (MyAPIGateway.Session.Camera.IsInFrustum(ref beamSphere))
            {
                var matrix = MatrixD.CreateFromDir(pInfo.Projectile.Direction);
                matrix.Translation = pInfo.Projectile.From;

                var radius = 0.15f;
                if (Tick % 6 == 0) radius = 0.14f;
                var weapon = pInfo.Weapon;
                var beamSlot = weapon.BeamSlot;
                var material = weapon.WeaponType.GraphicDef.ProjectileMaterial;
                var trailColor = weapon.WeaponType.GraphicDef.ProjectileColor;
                var particleColor = weapon.WeaponType.GraphicDef.ParticleColor;
                if (pInfo.PrimeProjectile && Tick > beamSlot[pInfo.ProjectileId] && pInfo.HitPos != Vector3D.Zero)
                {
                    beamSlot[pInfo.ProjectileId] = Tick + 20;
                    BeamParticleStart(pInfo.Entity, pInfo.HitPos, particleColor);
                }

                if (distToBeam < 1000000)
                {
                    if (distToBeam > 250000) radius *= 1.5f;
                    TransparentRenderExt.DrawTransparentCylinder(ref matrix, radius, radius, (float)pInfo.Projectile.Length, 6, trailColor, trailColor, WarpMaterial, WarpMaterial, 0f, BlendTypeEnum.Standard, BlendTypeEnum.Standard, false);
                }
                else MySimpleObjectDraw.DrawLine(pInfo.Projectile.From, pInfo.Projectile.To, material, ref trailColor, 2f);
            }
        }

        private MyParticleEffect _effect1 = new MyParticleEffect();

        internal void BeamParticleStart(IMyEntity ent, Vector3D pos, Vector4 color)
        {
            color = new Vector4(255, 10, 0, 1f); // comment out to use beam color
            var dist = Vector3D.Distance(MyAPIGateway.Session.Camera.Position, pos);
            var logOfPlayerDist = Math.Log(dist);

            var mainParticle = 32;

            var size = 20;
            var radius = size / logOfPlayerDist;
            var vel = ent.Physics.LinearVelocity;
            var matrix = MatrixD.CreateTranslation(pos);
            MyParticlesManager.TryCreateParticleEffect(mainParticle, out _effect1, ref matrix, ref pos, uint.MaxValue, true); // 15, 16, 24, 25, 28, (31, 32) 211 215 53

            if (_effect1 == null) return;
            _effect1.Loop = false;
            _effect1.DurationMax = 0.3333333332f;
            _effect1.DurationMin = 0.3333333332f;
            _effect1.UserColorMultiplier = color;
            _effect1.UserRadiusMultiplier = (float)radius;
            _effect1.UserEmitterScale = 1;
            _effect1.Velocity = vel;
            _effect1.Play();
        }

        private void BoltParticleStart(IMyEntity ent, Vector3D pos, Vector4 color, Vector3D speed)
        {
            var dist = Vector3D.Distance(MyAPIGateway.Session.Camera.Position, pos);
            var logOfPlayerDist = Math.Log(dist);

            var mainParticle = 32;

            var size = 10;
            var radius = size / logOfPlayerDist;
            var vel = ent.Physics.LinearVelocity;
            var matrix = MatrixD.CreateTranslation(pos);
            MyParticlesManager.TryCreateParticleEffect(mainParticle, out _effect1, ref matrix, ref pos, uint.MaxValue, true); // 15, 16, 24, 25, 28, (31, 32) 211 215 53

            if (_effect1 == null) return;
            _effect1.Loop = false;
            _effect1.DurationMax = 0.3333333332f;
            _effect1.DurationMin = 0.3333333332f;
            _effect1.UserColorMultiplier = color;
            _effect1.UserRadiusMultiplier = (float)radius;
            _effect1.UserEmitterScale = 1;
            _effect1.Velocity = speed;
            _effect1.Play();
        }
    }
}