﻿using System;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.ModAPI;
using VRageMath;
using WeaponCore.Platform;
using WeaponCore.Support;
using BlendTypeEnum = VRageRender.MyBillboard.BlendTypeEnum;
using static WeaponCore.Projectiles.Projectiles;
namespace WeaponCore
{
    public partial class Session
    {
        private void GenerateBeams(Weapon weapon)
        {
            var barrels = weapon.Muzzles;
            var fireBeam = new FiredBeam(weapon, _linePool.Get());
            foreach (var m in barrels)
            {
                if (Tick == m.LastShot)
                {
                    fireBeam.Beams.Add(new LineD(m.Position, m.Direction));
                }
            }
            _projectiles.FiredBeams.Add(fireBeam);
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
                var material = weapon.WeaponType.PhysicalMaterial;
                var trailColor = weapon.WeaponType.TrailColor;
                var particleColor = weapon.WeaponType.ParticleColor;
                if (pInfo.PrimeProjectile && Tick > beamSlot[pInfo.ProjectileId] && pInfo.HitPos != Vector3D.Zero)
                {
                    beamSlot[pInfo.ProjectileId] = Tick + 20;
                    BeamParticleStart(pInfo.Entity, pInfo.HitPos, particleColor);
                }

                if (distToBeam < 1000000)
                {
                    if (distToBeam > 250000) radius *= 1.5f;
                    TransparentRenderExt.DrawTransparentCylinder(ref matrix, radius, radius, (float) pInfo.Projectile.Length, 6, trailColor, trailColor, WarpMaterial, WarpMaterial, 0f, BlendTypeEnum.Standard, BlendTypeEnum.Standard, false);
                }
                else MySimpleObjectDraw.DrawLine(pInfo.Projectile.From, pInfo.Projectile.To, material, ref trailColor, 2f);
            }
        }

        private void DrawBolt(DrawProjectile pInfo)
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

                var weapon = pInfo.Weapon;
                var beamSlot = weapon.BeamSlot;
                var material = weapon.WeaponType.PhysicalMaterial;
                var trailColor = weapon.WeaponType.TrailColor;

                var radius = 0.15f;
                if (Tick % 6 == 0) radius = 0.14f;

                if (pInfo.PrimeProjectile && Tick > beamSlot[pInfo.ProjectileId] && pInfo.HitPos != Vector3D.Zero)
                {
                    var particleColor = weapon.WeaponType.ParticleColor;
                    beamSlot[pInfo.ProjectileId] = Tick + 20;
                    BoltParticleStart(pInfo.Entity, pInfo.HitPos, particleColor, Vector3D.Zero);
                }
                MySimpleObjectDraw.DrawLine(pInfo.Projectile.From, pInfo.Projectile.To, material, ref trailColor, 0.1f);
            }
        }

        private void DrawMissile(DrawProjectile pInfo)
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
                var weapon = pInfo.Weapon;
                var material = weapon.WeaponType.PhysicalMaterial;
                var trailColor = weapon.WeaponType.TrailColor;
                MySimpleObjectDraw.DrawLine(pInfo.Projectile.From, pInfo.Projectile.To, material, ref trailColor, 0.2f);
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
            _effect1.UserRadiusMultiplier = (float) radius;
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
