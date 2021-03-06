﻿using System;
using System.Collections.Generic;
using VRage.Game.Entity;
using VRage.Game.ModAPI;
using VRageMath;
using WeaponCore.Platform;
using WeaponCore.Support;
using static WeaponCore.Support.PartAnimation;
using static WeaponCore.Support.WeaponDefinition;
using static WeaponCore.Support.WeaponDefinition.AnimationDef;
using static WeaponCore.Support.WeaponDefinition.AnimationDef.PartAnimationSetDef;

namespace WeaponCore
{
    public partial class Session
    {
        internal void CreateAnimationSets(AnimationDef animations, WeaponSystem system, out Dictionary<EventTriggers, PartAnimation[]> weaponAnimationSets, out Dictionary<string, EmissiveState> weaponEmissivesSet, out Dictionary<string, Matrix[]> weaponLinearMoveSet, out HashSet<string> animationIdLookup, out Dictionary<EventTriggers, uint> animationLengths, out string[] heatingSubpartNames)
        {

            var allAnimationSet = new Dictionary<EventTriggers, HashSet<PartAnimation>>();
            weaponAnimationSets = new Dictionary<EventTriggers, PartAnimation[]>();
            weaponEmissivesSet = new Dictionary<string, EmissiveState>();
            animationIdLookup = new HashSet<string>();
            animationLengths = new Dictionary<EventTriggers, uint>();

            var wepAnimationSets = animations.WeaponAnimationSets;
            var wepEmissivesSet = animations.Emissives;

            weaponLinearMoveSet = new Dictionary<string, Matrix[]>();

            var emissiveLookup = new Dictionary<string, WeaponEmissive>();

            if (animations.HeatingEmissiveParts != null && animations.HeatingEmissiveParts.Length > 0)
                heatingSubpartNames = animations.HeatingEmissiveParts;
            else
                heatingSubpartNames = new string[0];

            if (wepEmissivesSet != null)
            {
                foreach (var emissive in wepEmissivesSet)
                    emissiveLookup.Add(emissive.EmissiveName, emissive);
            }

            if (wepAnimationSets == null)
                return;
            
            foreach (var animationSet in wepAnimationSets)
            {                
                for (int t = 0; t < animationSet.SubpartId.Length; t++)
                {
                    
                    foreach (var moves in animationSet.EventMoveSets)
                    {
                        if (!allAnimationSet.ContainsKey(moves.Key))
                        {
                            allAnimationSet[moves.Key] = new HashSet<PartAnimation>();
                            animationLengths[moves.Key] = 0;
                        }

                        List<Matrix> moveSet = new List<Matrix>();
                        List<Matrix> rotationSet = new List<Matrix>();
                        List<Matrix> rotCenterSet = new List<Matrix>();
                        List<string> rotCenterNameSet = new List<string>();
                        List<string> emissiveIdSet = new List<string>();

                        Guid guid = Guid.NewGuid();
                        var id = Convert.ToBase64String(guid.ToByteArray());
                        animationIdLookup.Add(id);
                        AnimationType[] typeSet = new[]
                        {
                            AnimationType.Movement,
                            AnimationType.ShowInstant,
                            AnimationType.HideInstant,
                            AnimationType.ShowFade,
                            AnimationType.HideFade,
                            AnimationType.Delay,
                            AnimationType.EmissiveOnly
                        };

                        var moveIndexer = new List<int[]>();
                        var currentEmissivePart = new List<int>();
                        uint totalPlayLength = animationSet.AnimationDelays[moves.Key];
                        
                        for (int i = 0; i < moves.Value.Length; i++)
                        {
                            var move = moves.Value[i];

                            totalPlayLength += move.TicksToMove;

                            var hasEmissive = !string.IsNullOrEmpty(move.EmissiveName);

                            if (move.MovementType == RelMove.MoveType.Delay ||
                            move.MovementType == RelMove.MoveType.Show ||
                            move.MovementType == RelMove.MoveType.Hide)
                            {
                                moveSet.Add(Matrix.Zero);
                                rotationSet.Add(Matrix.Zero);
                                rotCenterSet.Add(Matrix.Zero);
                                for (var j = 0; j < move.TicksToMove; j++)
                                {
                                    var type = 5;

                                    switch (move.MovementType)
                                    {
                                        case RelMove.MoveType.Delay:
                                            break;

                                        case RelMove.MoveType.Show:
                                            type = move.Fade ? 3 : 1;
                                            break;

                                        case RelMove.MoveType.Hide:
                                            type = move.Fade ? 4 : 2;
                                            break;
                                    }

                                    WeaponEmissive emissive;
                                    if (hasEmissive && emissiveLookup.TryGetValue(move.EmissiveName, out emissive))
                                    {
                                        var progress = 0f;
                                        if (move.TicksToMove == 1)
                                            progress = 1;
                                        else
                                            progress = (float)j / (move.TicksToMove - 1);

                                        CreateEmissiveStep(emissive, id + moveIndexer.Count, progress, ref weaponEmissivesSet, ref currentEmissivePart);
                                    }
                                    else
                                    {
                                        weaponEmissivesSet[id + moveIndexer.Count] = new EmissiveState();
                                        currentEmissivePart.Add(-1);
                                    }

                                    emissiveIdSet.Add(id + moveIndexer.Count);

                                    moveIndexer.Add(new[]
                                        {moveSet.Count - 1, rotationSet.Count - 1, rotCenterSet.Count - 1, type, emissiveIdSet.Count - 1, currentEmissivePart.Count - 1});
                                }
                            }
                            else
                            {
                                var type = 6;
                                if (move.LinearPoints != null && move.LinearPoints.Length > 0)
                                {
                                    double distance = 0;
                                    var tmpDirVec = new double[move.LinearPoints.Length][];

                                    for (int j = 0; j < move.LinearPoints.Length; j++)
                                    {
                                        var point = move.LinearPoints[j];

                                        var d = Math.Sqrt((point.x * point.x) + (point.y * point.y) +
                                                          (point.z * point.z));

                                        distance += d;

                                        var dv = new[] { d, point.x / d, point.y / d, point.z / d };

                                        tmpDirVec[j] = dv;
                                    }

                                    if (move.MovementType == RelMove.MoveType.ExpoDecay)
                                    {
                                        var traveled = 0d;

                                        var check = 1d;
                                        var rate = 0d;
                                        while (check > 0)
                                        {
                                            rate += 0.001;
                                            check = distance * Math.Pow(1 - rate, move.TicksToMove);
                                            if (check < 0.001) check = 0;

                                        }

                                        var vectorCount = 0;
                                        var remaining = 0d;
                                        var vecTotalMoved = 0d;
                                        rate = 1 - rate;

                                        for (int j = 0; j < move.TicksToMove; j++)
                                        {
                                            var step = distance * Math.Pow(rate, j + 1);
                                            if (step < 0.001) step = 0;

                                            var lastTraveled = traveled;
                                            traveled = distance - step;
                                            var changed = traveled - lastTraveled;

                                            var progress = 0f;
                                            if (move.TicksToMove == 1 || j == move.TicksToMove - 1)
                                                progress = 1;
                                            else
                                                progress = (float)(traveled / distance);

                                            changed += remaining;
                                            if (changed > tmpDirVec[vectorCount][0] - vecTotalMoved)
                                            {
                                                var origMove = changed;
                                                changed = changed - (tmpDirVec[vectorCount][0] - vecTotalMoved);
                                                remaining = origMove - changed;
                                                vecTotalMoved = 0;
                                            }
                                            else
                                            {
                                                vecTotalMoved += changed;
                                                remaining = 0;
                                            }


                                            var vector = new Vector3(tmpDirVec[vectorCount][1] * changed,
                                                tmpDirVec[vectorCount][2] * changed,
                                                tmpDirVec[vectorCount][3] * changed);

                                            var matrix = Matrix.CreateTranslation(vector);

                                            moveSet.Add(matrix);

                                            WeaponEmissive emissive;
                                            if (hasEmissive && emissiveLookup.TryGetValue(move.EmissiveName, out emissive))
                                            {
                                                CreateEmissiveStep(emissive, id + moveIndexer.Count, progress, ref weaponEmissivesSet, ref currentEmissivePart);
                                            }
                                            else
                                            {
                                                weaponEmissivesSet[id + moveIndexer.Count] = new EmissiveState();
                                                currentEmissivePart.Add(-1);
                                            }

                                            emissiveIdSet.Add(id + moveIndexer.Count);

                                            CreateRotationSets(move, progress, ref type, ref rotCenterNameSet, ref rotCenterSet, ref rotationSet);

                                            moveIndexer.Add(new[]
                                                {moveSet.Count - 1, rotationSet.Count - 1, rotCenterSet.Count - 1, 0, emissiveIdSet.Count - 1, currentEmissivePart.Count - 1});

                                            if (remaining > 0)
                                                vectorCount++;

                                        }
                                    }
                                    else if (move.MovementType == RelMove.MoveType.ExpoGrowth)
                                    {
                                        var traveled = 0d;

                                        var rate = 0d;
                                        var check = 0d;
                                        while (check < distance)
                                        {
                                            rate += 0.001;
                                            check = 0.001 * Math.Pow(1 + rate, move.TicksToMove);
                                        }

                                        var vectorCount = 0;
                                        var remaining = 0d;
                                        var vecTotalMoved = 0d;
                                        rate += 1;

                                        for (int j = 0; j < move.TicksToMove; j++)
                                        {
                                            var step = 0.001 * Math.Pow(rate, j + 1);
                                            if (step > distance) step = distance;

                                            var lastTraveled = traveled;
                                            traveled = step;
                                            var changed = traveled - lastTraveled;

                                            var progress = 0f;
                                            if (move.TicksToMove == 1 || j == move.TicksToMove - 1)
                                                progress = 1;
                                            else
                                                progress = (float)(traveled / distance);

                                            changed += remaining;
                                            if (changed > tmpDirVec[vectorCount][0] - vecTotalMoved)
                                            {
                                                var origMove = changed;
                                                changed = changed - (tmpDirVec[vectorCount][0] - vecTotalMoved);
                                                remaining = origMove - changed;
                                                vecTotalMoved = 0;
                                            }
                                            else
                                            {
                                                vecTotalMoved += changed;
                                                remaining = 0;
                                            }


                                            var vector = new Vector3(tmpDirVec[vectorCount][1] * changed,
                                                tmpDirVec[vectorCount][2] * changed,
                                                tmpDirVec[vectorCount][3] * changed);

                                            var matrix = Matrix.CreateTranslation(vector);

                                            moveSet.Add(matrix);

                                            WeaponEmissive emissive;
                                            if (hasEmissive && emissiveLookup.TryGetValue(move.EmissiveName, out emissive))
                                            {
                                                CreateEmissiveStep(emissive, id + moveIndexer.Count, progress, ref weaponEmissivesSet, ref currentEmissivePart);
                                            }
                                            else
                                            {
                                                weaponEmissivesSet[id + moveIndexer.Count] = new EmissiveState();
                                                currentEmissivePart.Add(-1);
                                            }

                                            emissiveIdSet.Add(id + moveIndexer.Count);

                                            CreateRotationSets(move, progress, ref type, ref rotCenterNameSet, ref rotCenterSet, ref rotationSet);

                                            moveIndexer.Add(new[]
                                                {moveSet.Count - 1, rotationSet.Count - 1, rotCenterSet.Count - 1, 0, emissiveIdSet.Count - 1, currentEmissivePart.Count - 1});

                                            if (remaining > 0)
                                                vectorCount++;
                                        }
                                    }
                                    else if (move.MovementType == RelMove.MoveType.Linear)
                                    {
                                        var distancePerTick = distance / move.TicksToMove;
                                        var vectorCount = 0;
                                        var remaining = 0d;
                                        var vecTotalMoved = 0d;

                                        CreateRotationSets(move, 1, ref type, ref rotCenterNameSet, ref rotCenterSet, ref rotationSet);

                                        for (int j = 0; j < move.TicksToMove; j++)
                                        {
                                            var changed = distancePerTick + remaining;
                                            if (changed > tmpDirVec[vectorCount][0] - vecTotalMoved)
                                            {
                                                var origMove = changed;
                                                changed = changed - (tmpDirVec[vectorCount][0] - vecTotalMoved);
                                                remaining = origMove - changed;
                                                vecTotalMoved = 0;
                                            }
                                            else
                                            {
                                                vecTotalMoved += changed;
                                                remaining = 0;
                                            }

                                            var vector = new Vector3(tmpDirVec[vectorCount][1] * changed,
                                                tmpDirVec[vectorCount][2] * changed,
                                                tmpDirVec[vectorCount][3] * changed);

                                            var matrix = Matrix.CreateTranslation(vector);

                                            moveSet.Add(matrix);

                                            WeaponEmissive emissive;
                                            if (hasEmissive && emissiveLookup.TryGetValue(move.EmissiveName, out emissive))
                                            {
                                                var progress = 0f;
                                                if (move.TicksToMove == 1)
                                                    progress = 1;
                                                else
                                                    progress = (float)j / (move.TicksToMove - 1);

                                                CreateEmissiveStep(emissive, id + moveIndexer.Count, progress, ref weaponEmissivesSet, ref currentEmissivePart);
                                            }
                                            else
                                            {
                                                weaponEmissivesSet[id + moveIndexer.Count] = new EmissiveState();
                                                currentEmissivePart.Add(-1);
                                            }

                                            emissiveIdSet.Add(id + moveIndexer.Count);

                                            moveIndexer.Add(new[]
                                                {moveSet.Count - 1, rotationSet.Count - 1, rotCenterSet.Count - 1, 0, emissiveIdSet.Count - 1, currentEmissivePart.Count - 1});

                                            if (remaining > 0)
                                                vectorCount++;
                                        }
                                    }
                                }
                                else
                                {
                                    moveSet.Add(Matrix.Zero);

                                    var rate = GetRate(move.MovementType, move.TicksToMove);

                                    if (move.MovementType == RelMove.MoveType.Linear)
                                        CreateRotationSets(move, 1, ref type, ref rotCenterNameSet, ref rotCenterSet, ref rotationSet);

                                    for (int j = 0; j < move.TicksToMove; j++)
                                    {
                                        var progress = 0d;
                                        if (move.MovementType == RelMove.MoveType.ExpoGrowth)
                                            progress = (0.001 * Math.Pow(rate, j)) / (double)move.TicksToMove;
                                        if (move.MovementType == RelMove.MoveType.ExpoDecay)
                                        {
                                            var perc = ((double)move.TicksToMove * Math.Pow(rate, j)) / (double)move.TicksToMove;
                                            progress = MathHelper.Lerp(1d, 0d, perc);
                                        }
                                        else
                                            progress = (double)j / (double)(move.TicksToMove - 1);


                                        if (move.TicksToMove == 1 || j == move.TicksToMove - 1)
                                            progress = 1;

                                        WeaponEmissive emissive;
                                        if (hasEmissive && emissiveLookup.TryGetValue(move.EmissiveName, out emissive))
                                        {
                                            CreateEmissiveStep(emissive, id + moveIndexer.Count, (float)progress, ref weaponEmissivesSet, ref currentEmissivePart);
                                        }
                                        else
                                        {
                                            weaponEmissivesSet[id + moveIndexer.Count] = new EmissiveState();
                                            currentEmissivePart.Add(-1);
                                        }

                                        emissiveIdSet.Add(id + moveIndexer.Count);

                                        if (move.MovementType != RelMove.MoveType.Linear)
                                            CreateRotationSets(move, progress, ref type, ref rotCenterNameSet, ref rotCenterSet, ref rotationSet);

                                        //Log.Line($"type: {type}");

                                        moveIndexer.Add(new[]
                                            {moveSet.Count - 1, rotationSet.Count - 1, rotCenterSet.Count - 1, type, emissiveIdSet.Count - 1, currentEmissivePart.Count - 1});
                                    }
                                }
                            }

                        }

                        if (animationLengths[moves.Key] < totalPlayLength)
                            animationLengths[moves.Key] = totalPlayLength;

                        var loop = false;
                        var reverse = false;
                        var triggerOnce = false;
                        var resetEmissives = false;

                        if (animationSet.Loop != null && animationSet.Loop.Contains(moves.Key))
                            loop = true;

                        if (animationSet.Reverse != null && animationSet.Reverse.Contains(moves.Key))
                            reverse = true;

                        if (animationSet.TriggerOnce != null && animationSet.TriggerOnce.Contains(moves.Key))
                            triggerOnce = true;

                        if (animationSet.ResetEmissives != null && animationSet.ResetEmissives.Contains(moves.Key))
                            resetEmissives = true;

                        var partAnim = new PartAnimation(moves.Key, id, rotationSet.ToArray(),
                            rotCenterSet.ToArray(), typeSet, emissiveIdSet.ToArray(), currentEmissivePart.ToArray(), moveIndexer.ToArray(), animationSet.SubpartId[t], null, null,
                            animationSet.BarrelId, animationSet.AnimationDelays[moves.Key], system, loop, reverse, triggerOnce, resetEmissives);

                        weaponLinearMoveSet.Add(id, moveSet.ToArray());

                        partAnim.RotCenterNameSet = rotCenterNameSet.ToArray();
                        allAnimationSet[moves.Key].Add(partAnim);
                    }
                }
            }

            foreach (var animationsKv in allAnimationSet)
            {
                weaponAnimationSets[animationsKv.Key] = new PartAnimation[animationsKv.Value.Count];
                animationsKv.Value.CopyTo(weaponAnimationSets[animationsKv.Key], 0);
            }

        }

        internal double GetRate(RelMove.MoveType move, uint ticksToMove)
        {
            var rate = 1d;
            if (move == RelMove.MoveType.ExpoGrowth)
            {
                var check = 0d;
                while (check < ticksToMove)
                {
                    rate += 0.001;
                    check = 0.001 * Math.Pow(1 + rate, ticksToMove);
                }
                rate += 1;
            }
            else if (move == RelMove.MoveType.ExpoDecay)
            {
                var check = 1d;
                while (check > 0)
                {
                    rate += 0.001;
                    check = ticksToMove * Math.Pow(1 - rate, ticksToMove);
                    if (check < 0.001) check = 0;

                }
                rate = 1 - rate;
            }

            return rate;
        }

        internal Dictionary<EventTriggers, PartAnimation[]> CreateWeaponAnimationSet(WeaponSystem system, RecursiveSubparts parts)
        {
            if (!system.AnimationsInited)
            {
                var allAnimationSet = new Dictionary<EventTriggers, PartAnimation[]>();
                foreach (var animationSet in system.WeaponAnimationSet)
                {
                    allAnimationSet[animationSet.Key] =  new PartAnimation[animationSet.Value.Length];

                    for (int i = 0; i < animationSet.Value.Length; i++)
                    {
                        var animation = animationSet.Value[i];
                        MyEntity part;
                        parts.NameToEntity.TryGetValue(animation.SubpartId, out part);
                        var subpart = part as MyEntitySubpart;
                        if (subpart == null) continue;

                        var rotations = new Matrix[animation.RotationSet.Length];
                        var rotCenters = new Matrix[animation.RotCenterSet.Length];
                        animation.RotationSet.CopyTo(rotations, 0);
                        animation.RotCenterSet.CopyTo(rotCenters, 0);

                        var rotCenterNames = animation.RotCenterNameSet;

                        var partCenter = GetPartLocation("subpart_" + animation.SubpartId, subpart.Parent.Model);


                        for (int j = 0; j < rotations.Length; j++)
                        {
                            if (rotations[j] != Matrix.Zero)
                                rotations[j] = Matrix.CreateTranslation(-partCenter) * rotations[j] * Matrix.CreateTranslation(partCenter);
                        }

                        for (int j = 0; j < rotCenters.Length; j++)
                        {
                            if (rotCenters[j] != Matrix.Zero && rotCenterNames != null)
                            {
                                var dummyCenter = GetPartLocation(rotCenterNames[j], subpart.Model);
                                rotCenters[j] = Matrix.CreateTranslation(-(partCenter + dummyCenter)) * rotCenters[j] * Matrix.CreateTranslation((partCenter + dummyCenter));
                            }
                        }

                        allAnimationSet[animationSet.Key][i] = new PartAnimation(animation.EventTrigger, animation.AnimationId, rotations, rotCenters,
                            animation.TypeSet, animation.EmissiveIds, animation.CurrentEmissivePart, animation.MoveToSetIndexer, animation.SubpartId, subpart, parts.Entity,
                            animation.Muzzle, animation.MotionDelay, system, animation.DoesLoop,
                            animation.DoesReverse, animation.TriggerOnce, animation.ResetEmissives);
                    }
                }

                system.WeaponAnimationSet.Clear();

                foreach (var animationKv in allAnimationSet)
                {
                    system.WeaponAnimationSet[animationKv.Key] = new PartAnimation[animationKv.Value.Length];
                    animationKv.Value.CopyTo(system.WeaponAnimationSet[animationKv.Key], 0);
                }

                system.AnimationsInited = true;
                return allAnimationSet;
            }

            var returnAnimations = new Dictionary<EventTriggers, PartAnimation[]>();
            foreach (var animationKv in system.WeaponAnimationSet)
            {
                returnAnimations[animationKv.Key] = new PartAnimation[animationKv.Value.Length];
                for (int i = 0; i < animationKv.Value.Length; i++)
                {
                    var animation = animationKv.Value[i];
                    MyEntity part;
                    parts.NameToEntity.TryGetValue(animation.SubpartId, out part);
                    var subpart = part as MyEntitySubpart;
                    if (subpart == null) continue;
                    returnAnimations[animationKv.Key][i] = new PartAnimation(animation)
                    {
                        Part = subpart,
                        MainEnt = parts.Entity,
                    };
                }
            }
            //Log.Line("Copying Animations");
            return returnAnimations;
        }

        internal Matrix CreateRotation(double x, double y, double z)
        {

            var rotation = MatrixD.Zero;

            if (x > 0 || x < 0)
                rotation = MatrixD.CreateRotationX(MathHelperD.ToRadians(x));

            if (y > 0 || y < 0)
                if (x > 0 || x < 0)
                    rotation *= MatrixD.CreateRotationY(MathHelperD.ToRadians(y));
                else
                    rotation = MatrixD.CreateRotationY(MathHelperD.ToRadians(y));

            if (z > 0 || z < 0)
                if (x > 0 || x < 0 || y > 0 || y < 0)
                    rotation *= MatrixD.CreateRotationZ(MathHelperD.ToRadians(z));
                else
                    rotation = MatrixD.CreateRotationZ(MathHelperD.ToRadians(z));

            return rotation;
        }

        internal void CreateRotationSets(RelMove move, double progress, ref int type, ref List<string> rotCenterNameSet, ref List<Matrix> rotCenterSet, ref List<Matrix> rotationSet)
        {
            type = 6;
            var fullProgress = 1 - progress < 0.001;

            if (!String.IsNullOrEmpty(move.CenterEmpty) &&
                                    (move.RotAroundCenter.x > 0 || move.RotAroundCenter.y > 0 ||
                                     move.RotAroundCenter.z > 0 || move.RotAroundCenter.x < 0 ||
                                     move.RotAroundCenter.y < 0 || move.RotAroundCenter.z < 0))
            {
                rotCenterNameSet.Add(move.CenterEmpty);                

                var xPerTick = move.RotAroundCenter.x / move.TicksToMove;
                var newX = fullProgress ? MathHelper.Lerp(0, move.RotAroundCenter.x / move.TicksToMove, progress) : xPerTick;

                var yPerTick = move.RotAroundCenter.y / move.TicksToMove;
                var newY = fullProgress ? MathHelper.Lerp(0, move.RotAroundCenter.y / move.TicksToMove, progress) : yPerTick;

                var zPerTick = move.RotAroundCenter.z / move.TicksToMove;
                var newZ = fullProgress ? MathHelper.Lerp(0, move.RotAroundCenter.z / move.TicksToMove, progress) : zPerTick;

                rotCenterSet.Add(CreateRotation(newX, newY, newZ));

                type = 0;
            }
            else
            {
                rotCenterNameSet.Add(null);
                rotCenterSet.Add(Matrix.Zero);
            }

            if (move.Rotation.x > 0 || move.Rotation.y > 0 || move.Rotation.z > 0 ||
                move.Rotation.x < 0 || move.Rotation.y < 0 || move.Rotation.z < 0)
            {
                var xPerTick = move.Rotation.x / move.TicksToMove;
                var newX = fullProgress ? MathHelper.Lerp(0, move.Rotation.x / move.TicksToMove, progress) : xPerTick;

                var yPerTick = move.Rotation.y / move.TicksToMove;
                var newY = fullProgress ? MathHelper.Lerp(0, move.Rotation.y / move.TicksToMove, progress) : yPerTick;

                var zPerTick = move.Rotation.z / move.TicksToMove;
                var newZ = fullProgress ? MathHelper.Lerp(0, move.Rotation.z / move.TicksToMove, progress) : zPerTick;

                rotationSet.Add(CreateRotation(newX, newY, newZ));

                type = 0;
            }
            else
                rotationSet.Add(Matrix.Zero);
        }

        internal void CreateEmissiveStep(WeaponEmissive emissive, string id, float progress, ref Dictionary<string, EmissiveState> allEmissivesSet, ref List<int> currentEmissivePart)
        {
            var setColor = (Color)emissive.Colors[0];
            if (emissive.Colors.Length > 1)
            {
                if (progress < 1)
                {
                    float scaledTime = progress * (float)(emissive.Colors.Length - 1);
                    Color lastColor = emissive.Colors[(int)scaledTime];
                    Color nextColor = emissive.Colors[(int)(scaledTime + 1f)];
                    float scaledProgress = (float)(scaledTime * progress);
                    setColor = Color.Lerp(lastColor, nextColor, scaledProgress);
                }
                else
                    setColor = emissive.Colors[emissive.Colors.Length - 1];
            }

            var intensity = MathHelper.Lerp(emissive.IntensityRange[0],
                emissive.IntensityRange[1], progress);

            var currPart = (int)Math.Round(MathHelper.Lerp(0, emissive.EmissivePartNames.Length - 1, progress));

            allEmissivesSet.Add(id, new EmissiveState { CurrentColor = setColor, CurrentIntensity = intensity, EmissiveParts = emissive.EmissivePartNames, CycleParts = emissive.CycleEmissivesParts, LeavePreviousOn = emissive.LeavePreviousOn });
            currentEmissivePart.Add(currPart);
        }

        internal static Color[] CreateHeatEmissive()
        {
            var colors = new[]
            {
                new Color(10, 0, 0, 150),
                new Color(30, 0, 0, 150),
                new Color(250, .01f, 0, 180),
                new Color(240, .02f, 0, 200f),
                new Color(240, .03f, 0, 210f),
                new Color(220, .04f, 0, 230f),
                new Color(210, .05f, .01f, 240f),
                new Color(210, .05f, .02f, 255f),
                new Color(210, .05f, .03f, 255f),
                new Color(210, .04f, .04f, 255f),
                new Color(210, .03f, .05f, 255f)
            };

            var setColors = new Color[68];

            for (int i = 0; i <= 67; i++)
            {
                var progress = (float)i / 67;

                if (progress < 1)
                {
                    float scaledTime = progress * (colors.Length - 1);
                    Color lastColor = colors[(int)scaledTime];
                    Color nextColor = colors[(int)(scaledTime + 1f)];
                    float scaledProgress = scaledTime * progress;
                    setColors[i] = Color.Lerp(lastColor, nextColor, scaledProgress);
                }
                else
                    setColors[i] = colors[colors.Length - 1];
            }

            return setColors;
        }

        internal Vector3 GetPartLocation(string partName, IMyModel model)
        {
            Dictionary<string, IMyModelDummy> dummyList = new Dictionary<string, IMyModelDummy>();
            model.GetDummies(dummyList);

            IMyModelDummy dummy;
            if (dummyList.TryGetValue(partName, out dummy))
                return dummy.Matrix.Translation;

            return Vector3.Zero;
        }

        internal IMyModelDummy GetPartDummy(string partName, IMyModel model)
        {
            Dictionary<string, IMyModelDummy> dummyList = new Dictionary<string, IMyModelDummy>();
            model.GetDummies(dummyList);

            IMyModelDummy dummy;
            if (dummyList.TryGetValue(partName, out dummy))
                return dummy;

            return null;
        }

        internal void ProcessAnimations()
        {
            PartAnimation anim;
            while (ThreadedAnimations.TryDequeue(out anim))
                AnimationsToProcess.Add(anim);

            for (int i = AnimationsToProcess.Count - 1; i >= 0; i--)
            {
                var animation = AnimationsToProcess[i];

                if (animation?.MainEnt != null && !animation.MainEnt.MarkedForClose && animation.Part != null)
                {
                    if (animation.StartTick > Tick) continue;

                    if (animation.MovesPivotPos || animation.CanPlay)
                    {
                        var localMatrix = animation.Part.PositionComp.LocalMatrix;
                        Matrix rotation;
                        Matrix rotAroundCenter;
                        Vector3D translation;
                        AnimationType animationType;
                        EmissiveState currentEmissive;

                        animation.GetCurrentMove(out translation, out rotation, out rotAroundCenter, out animationType, out currentEmissive);
                        
                        if (animation.Reverse)
                        {
                            if (animationType == AnimationType.Movement) localMatrix.Translation = localMatrix.Translation - translation;

                            animation.Previous();
                            if (animation.Previous(false) == animation.NumberOfMoves - 1)
                                animation.Reverse = false;
                        }
                        else
                        {
                            if (animationType == AnimationType.Movement) localMatrix.Translation = localMatrix.Translation + translation;

                            animation.Next();
                            if (animation.DoesReverse && animation.Next(false) == 0)
                                animation.Reverse = true;
                        }


                        if (rotation != Matrix.Zero)
                            localMatrix *= animation.Reverse ? Matrix.Invert(rotation) : rotation;

                        if (rotAroundCenter != Matrix.Zero)
                            localMatrix *= animation.Reverse ? Matrix.Invert(rotAroundCenter) : rotAroundCenter;

                        if (animationType == AnimationType.Movement)
                        {
                            animation.Part.PositionComp.SetLocalMatrix(ref localMatrix,
                                null, true);
                        }
                        else if (!DedicatedServer && (animationType == AnimationType.ShowInstant || animationType == AnimationType.ShowFade))
                        {
                            animation.Part.Render.FadeIn = animationType == AnimationType.ShowFade;
                            var matrix = animation.Part.PositionComp.LocalMatrix;

                            //animation.Part.OnClose += testing;
                            animation.Part.Render.AddRenderObjects();

                            animation.Part.PositionComp.SetLocalMatrix(ref matrix, null, true);
                        }
                        else if (!DedicatedServer && (animationType == AnimationType.HideInstant || animationType == AnimationType.HideFade))
                        {
                            animation.Part.Render.FadeOut = animationType == AnimationType.HideFade;
                            var matrix = animation.Part.PositionComp.LocalMatrix;
                            animation.Part.Render.RemoveRenderObjects();
                            animation.Part.PositionComp.SetLocalMatrix(ref matrix, null, true);
                        }
                        

                        if (!DedicatedServer && currentEmissive.EmissiveParts != null && currentEmissive.EmissiveParts.Length > 0)
                        {
                            if (currentEmissive.CycleParts)
                            {
                                animation.Part.SetEmissiveParts(currentEmissive.EmissiveParts[currentEmissive.CurrentPart], currentEmissive.CurrentColor,
                                    currentEmissive.CurrentIntensity);
                                if (!currentEmissive.LeavePreviousOn)
                                {
                                    var prev = currentEmissive.CurrentPart - 1 >= 0 ? currentEmissive.CurrentPart - 1 : currentEmissive.EmissiveParts
                                        .Length - 1;
                                    animation.Part.SetEmissiveParts(currentEmissive.EmissiveParts[prev],
                                        Color.Transparent,
                                        currentEmissive.CurrentIntensity);
                                }
                            }
                            else
                            {

                                for (int j = 0; j < currentEmissive.EmissiveParts.Length; j++)
                                    animation.Part.SetEmissiveParts(currentEmissive.EmissiveParts[j], currentEmissive.CurrentColor, currentEmissive.CurrentIntensity);
                            }
                        }
                    }
                    else
                    {
                        if (animation.Reverse)
                        {                            
                            animation.Previous();
                            if (animation.Previous(false) == animation.NumberOfMoves - 1)
                                animation.Reverse = false;
                        }
                        else
                        {
                            animation.Next();
                            if (animation.DoesReverse && animation.Next(false) == 0)
                                animation.Reverse = true;
                        }
                    }

                    //Log.Line(animation.Looping)
                    

                    if (!animation.Reverse && !animation.Looping && animation.CurrentMove == 0)
                    {
                        AnimationsToProcess.RemoveAtFast(i);
                        animation.Running = false;

                        if (!DedicatedServer && animation.ResetEmissives && animation.EmissiveParts != null)
                        {
                            for (int j = 0; j < animation.EmissiveParts.Length; j++)
                            {
                                var emissivePart = animation.EmissiveParts[j];
                                animation.Part.SetEmissiveParts(emissivePart, Color.Transparent, 0);
                            }
                        }
                    }
                }
                else
                    AnimationsToProcess.RemoveAtFast(i);

            }
        }

        private void testing(MyEntity obj)
        {
            Log.Line("Closed");
        }
    }
}
