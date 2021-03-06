using System;
using Sandbox.Game;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Collections;
using VRage.Game;
using VRage.Game.Components;
using VRage.Game.Entity;
using VRageMath;
using WeaponCore.Support;
using static Sandbox.Definitions.MyDefinitionManager;

namespace WeaponCore
{
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation | MyUpdateOrder.AfterSimulation | MyUpdateOrder.Simulation, int.MaxValue - 1)]
    public partial class Session : MySessionComponentBase
    {
        public override void BeforeStart()
        {
            try
            {
                BeforeStartInit();
            }
            catch (Exception ex) { Log.Line($"Exception in BeforeStart: {ex}"); }
        }

        public override void UpdatingStopped()
        {
            try
            {
                Log.Line($"Paused:{Tick}");
                Paused();
                _paused = true;
            }
            catch (Exception ex) { Log.Line($"Exception in UpdatingStopped: {ex}"); }
        }


        public override void UpdateBeforeSimulation()
        {
            try
            {
                DsUtil.Start("av");
                if (!DedicatedServer) Av.End();
                DsUtil.Complete("av", true);

                Timings();

                if (!WeaponAmmoRemoveQueue.IsEmpty && CTask.IsComplete)
                {
                    if (CTask.valid && CTask.Exceptions != null)
                        TaskHasErrors(ref CTask, "CTask");

                    CTask = MyAPIGateway.Parallel.StartBackground(AmmoToRemove, RemoveAmmo);
                }

                if (!WeaponAmmoPullQueue.IsEmpty && ITask.IsComplete)
                {
                    if (ITask.valid && ITask.Exceptions != null)
                        TaskHasErrors(ref ITask, "ITask");

                    ITask = MyAPIGateway.Parallel.StartBackground(AmmoPull, MoveAmmo);
                }

                if (!CompsToStart.IsEmpty)
                    StartComps();

                if (Tick120 && CompsDelayed.Count > 0)
                    DelayedComps();

                if (CompReAdds.Count > 0)
                    ChangeReAdds();

                if (Tick3600 && MpActive) 
                    NetReport();

                if (Tick180) 
                    ProfilePerformance();

                FutureEvents.Tick(Tick);

                if (!DedicatedServer && UiInput.PlayerCamera && !InMenu) WheelUi.UpdatePosition();
            }
            catch (Exception ex) { Log.Line($"Exception in SessionBeforeSim: {ex}"); }
        }

        public override void Simulate()
        {
            try
            {
                if (!DedicatedServer)
                {
                    EntityControlUpdate();
                    CameraMatrix = Session.Camera.WorldMatrix;
                    CameraPos = CameraMatrix.Translation;
                    PlayerPos = Session.Player?.Character?.WorldAABB.Center ?? Vector3D.Zero;
                }

                if (GameLoaded)
                {
                    DsUtil.Start("ai");
                    AiLoop();
                    DsUtil.Complete("ai", true);

                    DsUtil.Start("charge");
                    if (ChargingWeapons.Count > 0) UpdateChargeWeapons();
                    DsUtil.Complete("charge", true);

                    DsUtil.Start("acquire");
                    if (AcquireTargets.Count > 0) CheckAcquire();
                    DsUtil.Complete("acquire", true);

                    DsUtil.Start("shoot");
                    if (ShootingWeapons.Count > 0) ShootWeapons();
                    DsUtil.Complete("shoot", true);

                }

                if (!DedicatedServer && !WheelUi.WheelActive && !InMenu)
                {
                    UpdateLocalAiAndCockpit();
                    if (UiInput.PlayerCamera && ActiveCockPit != null) 
                        TargetSelection();
                }

                if (FragmentsNeedingEntities.Count > 0)
                    Projectiles.PrepFragmentEntities();

                DsUtil.Start("projectiles");
                Projectiles.Update();
                DsUtil.Complete("projectiles", true);

                DsUtil.Start("network1");
                if (WeaponsToSync.Count > 0) Proccessor.Proccess();
                DsUtil.Complete("network1", true);

                /*
                if (!DedicatedServer)
                {
                    //PTask = MyAPIGateway.Parallel.StartBackground(Projectiles.Update);
                    if (WeaponsToSync.Count > 0) NTask = MyAPIGateway.Parallel.StartBackground(Proccessor.Proccess);
                }
                */

            }
            catch (Exception ex) { Log.Line($"Exception in SessionSim: {ex}"); }
        }

        public override void UpdateAfterSimulation()
        {
            try
            {
                if (Placer != null) UpdatePlacer();
                if (!DedicatedServer) ProcessAnimations();

                /*
                DsUtil.Start("projectiles");
                if (!DedicatedServer && false)
                {
                    if (!PTask.IsComplete)
                        PTask.Wait();

                    if (PTask.IsComplete && PTask.valid && PTask.Exceptions != null)
                        TaskHasErrors(ref PTask, "PTask");
                }
                else Projectiles.Update();

                DsUtil.Complete("projectiles", true);
                */
                if (_effectedCubes.Count > 0) 
                    ApplyGridEffect();

                if (Tick60) 
                    GridEffects();

                if (GridTask.IsComplete)
                    CheckDirtyGrids();

                DsUtil.Start("damage");
                if (Hits.Count > 0) ProcessHits();
                DsUtil.Complete("damage", true);

                if (IsClient)
                {
                    if (!MTask.IsComplete)
                        MTask.Wait();

                    if (MTask.IsComplete && MTask.valid && MTask.Exceptions != null)
                        TaskHasErrors(ref MTask, "MTask");
                }
                /*
                DsUtil.Start("network");
                if (!DedicatedServer)
                {
                    if (!NTask.IsComplete)
                        NTask.Wait();

                    if (NTask.IsComplete && NTask.valid && NTask.Exceptions != null)
                        TaskHasErrors(ref NTask, "NTask");
                }
                else if (WeaponsToSync.Count > 0) Proccessor.Proccess();
                */

                DsUtil.Start("network2");
                Proccessor.AddPackets();

                if (MpActive && !HandlesInput)
                {
                    if (PacketsToClient.Count > 0) ProccessServerPacketsForClients();
                    if (PacketsToServer.Count > 0) ProccessClientPacketsForServer();
                }
                DsUtil.Complete("network2", true);
            }
            catch (Exception ex) { Log.Line($"Exception in SessionAfterSim: {ex}"); }
        }

        public override void Draw()
        {
            try
            {
                if (DedicatedServer || _lastDrawTick == Tick || _paused) return;
                _lastDrawTick = Tick;
                DsUtil.Start("draw");
                CameraMatrix = Session.Camera.WorldMatrix;
                CameraPos = CameraMatrix.Translation;
                CameraFrustrum.Matrix = (Camera.ViewMatrix * Camera.ProjectionMatrix);
                if ((UiInput.PlayerCamera || UiInput.FirstPersonView || UiInput.InSpyCam) && !InMenu && !Session.Config.MinimalHud && !MyAPIGateway.Gui.IsCursorVisible)
                {
                    if (WheelUi.WheelActive) WheelUi.DrawWheel();
                    TargetUi.DrawTargetUi();
                }

                Av.Run();
                DsUtil.Complete("draw", true);
            }
            catch (Exception ex) { Log.Line($"Exception in SessionDraw: {ex}"); }
        }


        public override void HandleInput()
        {
            if (HandlesInput)
            {
                UiInput.UpdateInputState();
                if (MpActive)
                {
                    if (UiInput.MouseButtonPressed != UiInput.MouseButtonWasPressed && ActiveControlBlock != null) SendMouseUpdate(ActiveControlBlock);
                    if (ClientGridResyncRequests.Count > 0) ProccessGridResyncRequests();
                    if (PacketsToClient.Count > 0) ProccessServerPacketsForClients();
                    if (PacketsToServer.Count > 0) ProccessClientPacketsForServer();
                    if (ClientSideErrorPktList.Count > 0) ReproccessClientErrorPackets();
                }
            }
        }

        public override void LoadData()
        {
            try
            {
                AllDefinitions = Static.GetAllDefinitions();
                SoundDefinitions = Static.GetSoundDefinitions();
                MyEntities.OnEntityCreate += OnEntityCreate;
                MyAPIGateway.Gui.GuiControlCreated += MenuOpened;
                MyAPIGateway.Gui.GuiControlRemoved += MenuClosed;

                MyAPIGateway.Utilities.RegisterMessageHandler(7771, Handler);
                MyAPIGateway.Utilities.SendModMessage(7772, null);

                foreach (var mod in Session.Mods)
                {
                    if (mod.PublishedFileId == 1365616918) ShieldMod = true;
                    else if (mod.PublishedFileId == 1931509062 || mod.PublishedFileId == 1995197719 || mod.PublishedFileId == 2006751214 || mod.PublishedFileId == 2015560129) ReplaceVanilla = true;
                    else if (mod.GetPath().Contains("AppData\\Roaming\\SpaceEngineers\\Mods\\VanillaReplacement"))
                        ReplaceVanilla = true;
                }

                TriggerEntityModel = ModContext.ModPath + "\\Models\\Environment\\JumpNullField.mwm";
                TriggerEntityPool = new MyConcurrentPool<MyEntity>(0, TriggerEntityClear, 10000, TriggerEntityActivator);

                ReallyStupidKeenShit();
            }
            catch (Exception ex) { Log.Line($"Exception in LoadData: {ex}"); }
        }

        protected override void UnloadData()
        {
            try
            {
                if (IsServer || DedicatedServer)
                    MyAPIGateway.Multiplayer.UnregisterMessageHandler(ServerPacketId, ServerReceivedPacket);
                else
                    MyAPIGateway.Multiplayer.UnregisterMessageHandler(ClientPacketId, ClientReceivedPacket);

                MyAPIGateway.Utilities.UnregisterMessageHandler(7771, Handler);

                MyAPIGateway.TerminalControls.CustomControlGetter -= CustomControlHandler;
                MyEntities.OnEntityCreate -= OnEntityCreate;
                MyAPIGateway.Gui.GuiControlCreated -= MenuOpened;
                MyAPIGateway.Gui.GuiControlRemoved -= MenuClosed;

                MyVisualScriptLogicProvider.PlayerDisconnected -= PlayerDisconnected;
                MyVisualScriptLogicProvider.PlayerRespawnRequest -= PlayerConnected;
                ApiServer.Unload();

                PurgeAll();

                Log.Line("Logging stopped.");
                Log.Close();
            }
            catch (Exception ex) { Log.Line($"Exception in UnloadData: {ex}"); }
        }
    }
}

