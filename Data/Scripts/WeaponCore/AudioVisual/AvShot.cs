﻿using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Utils;
using VRageMath;
namespace WeaponCore.Support
{
    internal class AvShot
    {
        internal WeaponSystem System;
        internal GridAi Ai;
        internal MyEntity PrimeEntity;
        internal MyEntity TriggerEntity;
        internal readonly MySoundPair FireSound = new MySoundPair();
        internal readonly MySoundPair TravelSound = new MySoundPair();
        internal readonly MySoundPair HitSound = new MySoundPair();
        internal readonly MyEntity3DSoundEmitter FireEmitter = new MyEntity3DSoundEmitter(null, true, 1f);
        internal readonly MyEntity3DSoundEmitter TravelEmitter = new MyEntity3DSoundEmitter(null, true, 1f);
        internal readonly MyEntity3DSoundEmitter HitEmitter = new MyEntity3DSoundEmitter(null, true, 1f);

        internal MyQueue<AfterGlow> GlowSteps = new MyQueue<AfterGlow>(64);
        internal Queue<Shrinks> TracerShrinks = new Queue<Shrinks>(64);
        internal List<Vector3D> Offsets = new List<Vector3D>(64);

        //internal Stack<Shrinking> ShrinkSteps = new Stack<Shrinking>();
        internal WeaponComponent FiringWeapon;
        internal WeaponSystem.FiringSoundState FiringSoundState;

        internal bool Offset;
        internal bool Accelerates;
        internal bool AmmoSound;
        internal bool HasTravelSound;
        internal bool HitSoundActive;
        internal bool HitSoundActived;
        internal bool StartSoundActived;
        internal bool FakeExplosion;
        internal bool Triggered;
        internal bool Cloaked;
        internal bool Active;
        internal bool ShrinkInited;
        internal bool Growing;
        internal bool Flip;
        internal bool TrailActivated;
        internal double MaxTracerLength;
        internal double MaxGlowLength;
        internal double FirstStepSize;
        internal double StepSize;
        internal double TotalLength;
        internal double Thickness;
        internal double ScaleFov;
        internal double VisualLength;
        internal double MaxSpeed;
        internal double MaxStepSize;
        internal double TracerLengthSqr;
        internal float LineScaler;
        internal float GlowShrinkSize;
        internal float DistanceToLine;
        internal int LifeTime;
        internal int MuzzleId;
        internal int WeaponId;
        internal int TracerStep;
        internal int TracerSteps;
        internal int TailSteps;
        internal uint LastTick;
        internal uint InitTick;
        internal TracerState Tracer;
        internal TrailState Trail;
        internal ModelState Model;
        internal Screen OnScreen;
        internal MatrixD OffsetMatrix;
        internal Vector3D Origin;
        internal Vector3D Position;
        internal Vector3D Direction;
        internal Vector3D PointDir;
        internal Vector3D HitVelocity;
        internal Vector3D HitPosition;
        internal Vector3D ShooterVelocity;
        internal Vector3D TracerStart;
        internal Vector3D ShooterVelStep;
        internal Vector3D BackOfTracer;
        internal Vector3D ClosestPointOnLine;
        internal Vector4 Color;

        internal Hit Hit;
        internal MatrixD PrimeMatrix = MatrixD.Identity;
        internal MatrixD TriggerMatrix = MatrixD.Identity;
        internal Shrinks EmptyShrink;

        internal enum TracerState
        {
            Full,
            Grow,
            Shrink,
            Off,
        }

        internal enum ModelState
        {
            None,
            Exists,
            Close,
        }

        internal enum TrailState
        {
            Front,
            Back,
            Off,
        }

        internal enum Screen // Tracer includes Tail;
        {
            Tracer,
            Trail,
            None,
        }

        internal void Init(ProInfo info, double firstStepSize, double maxSpeed)
        {
            System = info.System;
            Ai = info.Ai;
            Model = (info.System.PrimeModelId != -1 || info.System.TriggerModelId != -1) ? Model = ModelState.Exists : Model = ModelState.None;
            PrimeEntity = info.PrimeEntity;
            TriggerEntity = info.TriggerEntity;
            InitTick = Ai.Session.Tick;
            Origin = info.Origin;
            Offset = System.OffsetEffect;
            MaxTracerLength = System.TracerLength;
            FirstStepSize = firstStepSize;
            Accelerates = System.Values.Ammo.Trajectory.AccelPerSec > 0;
            MuzzleId = info.MuzzleId;
            WeaponId = info.WeaponId;
            MaxSpeed = maxSpeed;
            MaxStepSize = MaxSpeed * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
            ShooterVelocity = info.ShooterVel;
            ShooterVelStep = info.ShooterVel * MyEngineConstants.PHYSICS_STEP_SIZE_IN_SECONDS;
            info.Ai.WeaponBase.TryGetValue(info.Target.FiringCube, out FiringWeapon);
            ShrinkInited = false;

            if (System.DrawLine) Tracer = !System.IsBeamWeapon && firstStepSize < MaxTracerLength ? TracerState.Grow : TracerState.Full;
            else Tracer = TracerState.Off;

            if (System.Trail)
            {
                MaxGlowLength = MathHelperD.Clamp(System.Values.Graphics.Line.Trail.DecayTime * MaxStepSize, 0.1f, info.System.MaxTrajectory);
                Trail = System.Values.Graphics.Line.Trail.Back ? TrailState.Back : Trail = TrailState.Front;
                GlowShrinkSize = System.Values.Graphics.Line.Tracer.Width / System.Values.Graphics.Line.Trail.DecayTime;
            }
            else Trail = TrailState.Off;
            TotalLength = MathHelperD.Clamp(MaxTracerLength + MaxGlowLength, 0.1f, info.System.MaxTrajectory);

        }

        internal void Update(double stepSize, double visualLength, ref Vector3D position, ref Vector3D direction, ref Vector3D pointDir, bool growing = false)
        {
            LastTick = Ai.Session.Tick;
            Position = position;
            Direction = direction;
            StepSize = stepSize;
            VisualLength = visualLength;
            Flip = StepSize > VisualLength && !MyUtils.IsZero(StepSize - VisualLength);
            TracerStart = Flip ? Position : Position + (-Direction * VisualLength);
            if (Tracer == TracerState.Grow && MyUtils.IsZero(MaxTracerLength - VisualLength))
                Tracer = TracerState.Full;

            PointDir = pointDir;
            Growing = growing;
            ++LifeTime;

            if (OnScreen == Screen.None && (System.DrawLine || Model == ModelState.None && System.AmmoParticle))
            {
                var rayTracer = new RayD(TracerStart, Flip ? -PointDir : PointDir);
                var rayTrail = new RayD(Trail == TrailState.Back ? TracerStart : Position, -PointDir);

                //DsDebugDraw.DrawRay(rayTracer, VRageMath.Color.White, 0.5f, (float) VisualLength);
                //DsDebugDraw.DrawRay(rayTrail, VRageMath.Color.Black, 0.5f, (float) MathHelperD.Clamp(StepSize * (LifeTime + 1), 0, MaxGlowLength));

                double? dist;
                Ai.Session.CameraFrustrum.Intersects(ref rayTracer, out dist);
                if (dist != null && dist <= VisualLength)
                    OnScreen = Screen.Tracer;
                else if (OnScreen == Screen.None && System.Trail)
                {
                    var distBack = MathHelperD.Clamp(StepSize * (LifeTime + 1), 0, MaxGlowLength);
                    Ai.Session.CameraFrustrum.Intersects(ref rayTrail, out dist);
                    if (dist != null && dist <= distBack)
                    {
                        OnScreen = Screen.Trail;
                    }
                }
                if (OnScreen != Screen.None && System.Trail) TrailActivated = true;
            }
            else if (TrailActivated) OnScreen = Screen.Trail;
        }

        internal void Complete(ProInfo info, bool saveHit = false, bool closeModel = false)
        {
            if (!Active) {

                Active = true;
                Ai.Session.Av.AvShots.Add(this);
            }

            if (Hit.HitPos != Vector3D.Zero) {

                if (saveHit) {

                    if (Hit.Entity != null)
                        HitVelocity = Hit.Entity.GetTopMostParent()?.Physics?.LinearVelocity ?? Vector3D.Zero;
                    else if (Hit.Projectile != null) 
                        HitVelocity = Hit.Projectile.Velocity;
                    HitPosition = Hit.HitPos;
                }

                if (System.IsBeamWeapon) Tracer = TracerState.Full;
                else if (Tracer != TracerState.Off && VisualLength <= 0) {

                    if (OnScreen != Screen.None)
                        Tracer = TracerState.Off;
                    
                }
                else if (VisualLength / StepSize > 1)
                {
                    Tracer = TracerState.Shrink;
                    TotalLength = MathHelperD.Clamp(VisualLength + MaxGlowLength, 0.1f, Vector3D.Distance(Origin, Position));
                }
            }


            if (closeModel)
                Model = ModelState.Close;

            if (OnScreen != Screen.None && System.DrawLine) 
                LineVariableEffects();
            
            if (Tracer != TracerState.Off && Hit.HitPos != Vector3D.Zero) {
                
                if (System.IsBeamWeapon) RunBeam();
                else if (OnScreen != Screen.None && Tracer == TracerState.Shrink && false)  
                    Shrink();
            }
            else
            {
                if (OnScreen == Screen.Tracer && Tracer != TracerState.Off && System.OffsetEffect)
                    LineOffsetEffect(TracerStart, -PointDir, VisualLength);

                if (OnScreen != Screen.None && Trail != TrailState.Off)
                    RunGlow(ref EmptyShrink);
            }
            //Log.Line($"[Complete] {GlowSteps.Count} - OnScreen:{OnScreen} - Tracer:{Tracer} - Trail:{Trail} - Growing:{Growing} - TtoP:{Vector3D.Distance(TracerStart, Position)} - TvsV:{MaxTracerLength}({VisualLength}) - Hit:{Hit.HitPos != Vector3D.Zero}");
        }

        internal void LineVariableEffects()
        {
            var color = System.Values.Graphics.Line.Tracer.Color;
            if (System.LineColorVariance)
            {
                var cv = System.Values.Graphics.Line.ColorVariance;
                var randomValue = MyUtils.GetRandomFloat(cv.Start, cv.End);
                color.X *= randomValue;
                color.Y *= randomValue;
                color.Z *= randomValue;
            }
            Color = color;
            var width = System.Values.Graphics.Line.Tracer.Width;
            if (System.LineWidthVariance)
            {
                var wv = System.Values.Graphics.Line.WidthVariance;
                var randomValue = MyUtils.GetRandomFloat(wv.Start, wv.End);
                width += randomValue;
            }

            //var target = System.IsBeamWeapon ? Position + -Direction * VisualLength : Position + (-Direction * TotalLength);
            var target = Position + (-Direction * TotalLength);

            ClosestPointOnLine = MyUtils.GetClosestPointOnLine(ref Position, ref target, ref Ai.Session.CameraPos);
            DistanceToLine = (float)Vector3D.Distance(ClosestPointOnLine, MyAPIGateway.Session.Camera.WorldMatrix.Translation);
            //if (System.IsBeamWeapon && DistanceToLine < 1000) DistanceToLine = 1000;
            //else if (System.IsBeamWeapon && DistanceToLine < 350) DistanceToLine = 350;
            ScaleFov = Math.Tan(MyAPIGateway.Session.Camera.FovWithZoom * 0.5);
            Thickness = Math.Max(width, 0.10f * ScaleFov * (DistanceToLine / 100));
            LineScaler = ((float)Thickness / width);
        }

        internal void RunGlow(ref Shrinks shrinks)
        {
            var glowCount = GlowSteps.Count;
            if (glowCount <= System.Values.Graphics.Line.Trail.DecayTime)
            {
                var shrinking = shrinks.Thickness > 0;
                var glow = Ai.Session.Av.Glows.Count > 0 ? Ai.Session.Av.Glows.Pop() : new AfterGlow();
                glow.Step = 0;
                glow.VelStep = Direction * StepSize;
                var back = shrinking ? shrinks.Start + (-Direction * shrinks.Length) : TracerStart;
                //var front = shrinking ? shrinks.Start + TracerStart : Position;
                var startPos = Trail == TrailState.Back ? back : Position;
                //var startPos = Trail == TrailState.Back ? TracerStart : Position;
                glow.TailPos = startPos + -glow.VelStep;
                GlowSteps.Enqueue(glow);
                ++glowCount;
            }
            var endIdx = glowCount - 1;
            for (int i = endIdx; i >= 0; i--)
            {
                var glow = GlowSteps[i];

                if (i != 0) glow.Parent = GlowSteps[i - 1];
                if (i == endIdx)
                    glow.Line = i != 0 ? new LineD(glow.Parent.TailPos, glow.TailPos) : new LineD(glow.TailPos - glow.VelStep, glow.TailPos);
            }
        }

        internal void Shrink()
        {
            if (!ShrinkInited) ShrinkInit();
            for (int i = 0; i < TracerSteps; i++)
            {
                var shrunk = GetLine();
                if (shrunk.HasValue)
                {
                    var color = System.Values.Graphics.Line.Tracer.Color;
                    if (System.LineColorVariance)
                    {
                        var cv = System.Values.Graphics.Line.ColorVariance;
                        var randomValue = MyUtils.GetRandomFloat(cv.Start, cv.End);
                        color.X *= randomValue;
                        color.Y *= randomValue;
                        color.Z *= randomValue;
                    }

                    var width = System.Values.Graphics.Line.Tracer.Width;
                    if (System.LineWidthVariance)
                    {
                        var wv = System.Values.Graphics.Line.WidthVariance;
                        var randomValue = MyUtils.GetRandomFloat(wv.Start, wv.End);
                        width += randomValue;
                    }

                    width = (float)Math.Max(width, 0.10f * ScaleFov * (DistanceToLine / 100));

                    var length = (float)(shrunk.Value.Reduced + shrunk.Value.StepLength);
                    if (System.OffsetEffect)
                    {
                        var offsets = LineOffsetEffect(Hit.HitPos, -PointDir, length, true);
                        TracerShrinks.Enqueue(new Shrinks { Start = Hit.HitPos, Color = color, Length = length, Thickness = width, Offsets = offsets });
                    }
                    else
                        TracerShrinks.Enqueue(new Shrinks {Start = Hit.HitPos, Color = color, Length = length, Thickness = width});
                }
            }
            //Log.Line($"TracerSteps: {TracerSteps} - Shrinks:{TracerShrinks.Count}");
        }

        private void ShrinkInit()
        {
            var fractualSteps = VisualLength / StepSize;
            TracerSteps = (int)Math.Floor(fractualSteps);
            TracerStep = TracerSteps;
            var frontOfTracer = (TracerStart + (Direction * VisualLength));
            BackOfTracer = frontOfTracer + (-Direction * StepSize);
            if (fractualSteps < StepSize || TracerSteps <= 0)
                Tracer = TracerState.Off;
        }

        internal Shrunk? GetLine()
        {
            if (TracerStep-- > 0)
            {
                BackOfTracer += ShooterVelStep;
                Hit.HitPos += ShooterVelStep;
                var backOfTail = BackOfTracer + (Direction * (TailSteps++ * StepSize));
                var newTracerBack = Hit.HitPos + -(Direction * TracerStep * StepSize);
                var reduced = TracerStep * StepSize;
                return new Shrunk(ref newTracerBack, ref backOfTail, reduced, StepSize);
            }
            return null;
        }

        internal void RunBeam()
        {
            if (System.HitParticle && !(MuzzleId != 0 && (System.ConvergeBeams || System.OneHitParticle)))
            {

                if (FiringWeapon != null)
                {
                    var weapon = FiringWeapon.Platform.Weapons[WeaponId];
                    var effect = weapon.HitEffects[MuzzleId];
                    if (OnScreen != Screen.None)
                    {
                        if (effect != null)
                        {
                            var elapsedTime = effect.GetElapsedTime();
                            if (elapsedTime <= 0 || elapsedTime >= 1)
                            {
                                effect.Stop(true);
                                effect = null;
                            }
                        }
                        MatrixD matrix;
                        MatrixD.CreateTranslation(ref HitPosition, out matrix);
                        if (effect == null)
                        {
                            MyParticlesManager.TryCreateParticleEffect(System.Values.Graphics.Particles.Hit.Name, ref matrix, ref HitPosition, uint.MaxValue, out effect);
                            if (effect == null)
                            {
                                weapon.HitEffects[MuzzleId] = null;
                                return;
                            }

                            effect.DistanceMax = System.Values.Graphics.Particles.Hit.Extras.MaxDistance;
                            effect.DurationMax = System.Values.Graphics.Particles.Hit.Extras.MaxDuration;
                            effect.UserColorMultiplier = System.Values.Graphics.Particles.Hit.Color;
                            effect.Loop = System.Values.Graphics.Particles.Hit.Extras.Loop;
                            effect.UserRadiusMultiplier = System.Values.Graphics.Particles.Hit.Extras.Scale * 1;
                            var scale = MathHelper.Lerp(1, 0, (DistanceToLine * 2) / System.Values.Graphics.Particles.Hit.Extras.MaxDistance);
                            effect.UserEmitterScale = (float)scale;
                        }
                        else if (effect.IsEmittingStopped)
                            effect.Play();

                        effect.WorldMatrix = matrix;
                        effect.Velocity = HitVelocity;
                        weapon.HitEffects[MuzzleId] = effect;
                    }
                    else if (effect != null)
                    {
                        effect.Stop(false);
                        weapon.HitEffects[MuzzleId] = null;
                    }
                }
            }
        }

        internal List<Vector3D> LineOffsetEffect(Vector3D tracerStart, Vector3D direction, double tracerLength, bool addToShrinks = false)
        {
            var up = MatrixD.Identity.Up;
            var startPos = tracerStart + -(direction * tracerLength);
            MatrixD.CreateWorld(ref startPos, ref direction, ref up, out OffsetMatrix);
            TracerLengthSqr = tracerLength * tracerLength;
            var maxOffset = System.Values.Graphics.Line.OffsetEffect.MaxOffset;
            var minLength = System.Values.Graphics.Line.OffsetEffect.MinLength;
            var maxLength = MathHelperD.Clamp(System.Values.Graphics.Line.OffsetEffect.MaxLength, 0, tracerLength);

            double currentForwardDistance = 0;
            var first = true;
            List<Vector3D> shrinkList = null;
            while (currentForwardDistance <= tracerLength)
            {
                currentForwardDistance += MyUtils.GetRandomDouble(minLength, maxLength);
                var lateralXDistance = MyUtils.GetRandomDouble(maxOffset * -1, maxOffset);
                var lateralYDistance = MyUtils.GetRandomDouble(maxOffset * -1, maxOffset);
                if (!addToShrinks) Offsets.Add(new Vector3D(lateralXDistance, lateralYDistance, currentForwardDistance * -1));
                else
                {
                    if (first)
                    {
                        first = false;
                        shrinkList = Ai.Session.ListOfVectorsPool.Get();
                    }
                    shrinkList.Add(new Vector3D(lateralXDistance, lateralYDistance, currentForwardDistance * -1));
                }
            }

            if (addToShrinks && shrinkList != null)
                return shrinkList;

            return null;
        }


        internal void SetupSounds(double distanceFromCameraSqr)
        {
            FiringSoundState = System.FiringSound;

            if (!System.IsBeamWeapon && System.AmmoTravelSound)
            {
                HasTravelSound = true;
                TravelSound.Init(System.Values.Audio.Ammo.TravelSound, false);
            }
            else HasTravelSound = false;

            if (System.HitSound)
            {
                var hitSoundChance = System.Values.Audio.Ammo.HitPlayChance;
                HitSoundActive = (hitSoundChance >= 1 || hitSoundChance >= MyUtils.GetRandomDouble(0.0f, 1f));
                if (HitSoundActive)
                    HitSound.Init(System.Values.Audio.Ammo.HitSound, false);
            }

            if (FiringSoundState == WeaponSystem.FiringSoundState.PerShot && distanceFromCameraSqr < System.FiringSoundDistSqr)
            {
                StartSoundActived = true;
                FireSound.Init(System.Values.Audio.HardPoint.FiringSound, false);
                FireEmitter.SetPosition(Origin);
                FireEmitter.Entity = FiringWeapon.MyCube;
            }
        }

        internal void AmmoSoundStart()
        {
            TravelEmitter.SetPosition(Position);
            TravelEmitter.Entity = PrimeEntity;
            TravelEmitter.PlaySound(TravelSound, true);
            AmmoSound = true;
        }

        internal void Close()
        {
            // Reset only vars that are not always set
            Hit = new Hit();
            HitVelocity = Vector3D.Zero;
            HitPosition = Vector3D.Zero;
            TracerStart = Vector3D.Zero;
            BackOfTracer = Vector3D.Zero;
            OnScreen = Screen.None;
            Tracer = TracerState.Off;
            Trail = TrailState.Off;
            LifeTime = 0;
            TracerSteps = 0;
            TracerStep = 0;

            AmmoSound = false;
            HitSoundActive = false;
            HitSoundActived = false;
            StartSoundActived = false;
            HasTravelSound = false;
            FakeExplosion = false;
            Triggered = false;
            Cloaked = false;
            Active = false;
            TrailActivated = false;
            ShrinkInited = false;
            GlowSteps.Clear();
            Offsets.Clear();
            //
            FiringWeapon = null;
            PrimeEntity = null;
            TriggerEntity = null;
            Ai = null;
            System = null;
        }
    }

    internal class AfterGlow
    {
        internal AfterGlow Parent;
        internal Vector3D TailPos;
        internal Vector3D VelStep;
        internal LineD Line;
        internal int Step;
    }

    internal struct Shrinks
    {
        internal List<Vector3D> Offsets;
        internal Vector3D Start;
        internal Vector4 Color;
        internal float Length;
        internal float Thickness;
    }

    internal struct Shrunk
    {
        internal readonly Vector3D PrevPosition;
        internal readonly Vector3D BackOfTail;
        internal readonly double Reduced;
        internal readonly double StepLength;

        internal Shrunk(ref Vector3D prevPosition, ref Vector3D backOfTail, double reduced, double stepLength)
        {
            PrevPosition = prevPosition;
            BackOfTail = backOfTail;
            Reduced = reduced;
            StepLength = stepLength;
        }
    }
}
