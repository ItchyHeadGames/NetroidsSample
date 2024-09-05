//#define DEBUG_DETERMINISM
//#define DEBUG_MUTATION
//#define DEBUG_VERIFY_NO_PHYS_CHANGE

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Ihg.Netroids.Runtime.GameBase;
using Ihg.Netroids.Runtime.GamePlay.GameNetworking.States;
using Ihg.Netroids.Runtime.NetGame.Runtime;
using Ihg.Utils;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.SceneManagement;
using Debug = UnityEngine.Debug;
using Random = UnityEngine.Random;
using Vector3 = UnityEngine.Vector3;

namespace Ihg.Netroids.Runtime.GamePlay.GameNetworking.Simulation
{
    public class GameSimulation
    {
        private float m_DeltaTime = Time.fixedDeltaTime;
        
        // Any access to Rigidbodies must be done through the physics system. No changing of RigidbodyState while this is
        // true is not allowed.

        public RigidbodySystem rigidbodySystem;
        public EffectSystem effectSystem;
        public NetroidsPhysics netroidsPhysics;
        public AsteroidSystem asteroidSystem;
        public BuffSystem buffSystem;
        public TimeSystem timeSystem;
        public EndlessDmSystem endlessDmSystem;
        public DMSystem dmSystem;
        public TeamDMSystem teamDmSystem;
        public ClassicGameSystem classicSystem;
        public ShipSystem shipSystem;
        public PlayerSystem playerSystem;
        public DamageSystem damageSystem;
        public MissileSystem missileSystem;
        public ExplosionSystem explosionSystem;
        public WeaponLockSystem weaponLockSystem;
        public BodyWrapSystem bodyWrapSystem;
        public WrapProjectileSystem wrapProjectileSystem;
        public PortalSystem portalSystem;
        public RaygunSystem raygunSystem;
        public BulletSystem bulletSystem;
        public FlamingMeteorSystem flamingMeteorSystem;
        public MeteorStormSystem meteorStormSystem;
        public ShipTurretSystem shipTurretSystem;
        public WeaponsSystem weaponsSystem;
        public GameModeSystem gameModeSystem;
        public HasLosSystem hasLosSystem;
        public ProjectileSystem projectileSystem;
        public CollisionSystem collisionSystem;
        public FixedTurretSystem fixedTurretSystem;
        
        public Settings settings;
        public Settings.GamePlaySettings gamePlaySettings;

        public Scene sourceScene;

        public float DeltaTime
        {
            set { m_DeltaTime = value; }
            get { return m_DeltaTime; }
        }

        public float GetDeltaTime()
        {
            return m_DeltaTime;
        }
       
        public GameSimulation(Scene sourceScene)
        {
            this.sourceScene = sourceScene;
        }

        public void Initialize(float stepTime, string name, IGameSettings gameSettings, Settings.SharedSettings sharedSettings)
        {
            LoggedStopWatch initTimer = LoggedStopWatch.StartNewStopWatch(System.Reflection.MethodBase.GetCurrentMethod().Name); 

            settings = (Settings) gameSettings;
            
            m_InvalidMutationCheckState = new GameState();
            m_DeterminismCheckState = new GameState();
            m_RigidbodyStatesCheck = new GameState();
            gamePlaySettings = sharedSettings.gamePlaySettings;

            ShipSystem.Configure(gamePlaySettings);
            FlamingMeteorSystem.Configure(gamePlaySettings);
            SceneTurretsData sceneTurretsData = new SceneTurretsData(sourceScene, gamePlaySettings);
            FixedTurretSystem.Configure(gamePlaySettings, sceneTurretsData);

            GameState.Init(gamePlaySettings.gameStateConfig);
            GameState.Initialize(m_InvalidMutationCheckState);
            GameState.Initialize(m_DeterminismCheckState);
            GameState.Initialize(m_RigidbodyStatesCheck);
            
            m_DeltaTime = stepTime;
            rigidbodySystem = new RigidbodySystem();
            timeSystem = new TimeSystem(m_DeltaTime);
            buffSystem = new BuffSystem(gamePlaySettings, timeSystem);
            effectSystem = new EffectSystem(timeSystem, settings.GamePlay.effectSettings);
            netroidsPhysics = new NetroidsPhysics(settings.configuration, sourceScene, name);
            asteroidSystem = new AsteroidSystem(netroidsPhysics, gamePlaySettings, buffSystem, effectSystem);
            hasLosSystem = new HasLosSystem(netroidsPhysics, rigidbodySystem);
            shipSystem = new ShipSystem(gamePlaySettings, netroidsPhysics, effectSystem, timeSystem, buffSystem, hasLosSystem);
            portalSystem = new PortalSystem(netroidsPhysics, gamePlaySettings.portalSettings, effectSystem, timeSystem);
            playerSystem = new PlayerSystem(shipSystem, gamePlaySettings, buffSystem, timeSystem, portalSystem);
            explosionSystem = new ExplosionSystem(effectSystem, gamePlaySettings, netroidsPhysics, buffSystem, asteroidSystem);
            missileSystem = new MissileSystem(gamePlaySettings, timeSystem, netroidsPhysics, explosionSystem);
            dmSystem = new DMSystem(timeSystem, gamePlaySettings, asteroidSystem);
            endlessDmSystem = new EndlessDmSystem(asteroidSystem);
            classicSystem = new ClassicGameSystem(timeSystem, gamePlaySettings, asteroidSystem);
            teamDmSystem = new TeamDMSystem(timeSystem, gamePlaySettings, asteroidSystem);
            weaponLockSystem = new WeaponLockSystem(gamePlaySettings, netroidsPhysics, timeSystem);
            damageSystem = new DamageSystem(gamePlaySettings, buffSystem);
            bodyWrapSystem = new BodyWrapSystem();
            wrapProjectileSystem = new WrapProjectileSystem();
            raygunSystem = new RaygunSystem(gamePlaySettings.rayGunSettings, buffSystem, gamePlaySettings.juiceSettings, 
                                            netroidsPhysics, weaponLockSystem, asteroidSystem, gamePlaySettings.scoreSettings, 
                                            effectSystem);

            projectileSystem = new ProjectileSystem(timeSystem, netroidsPhysics);
            
            bulletSystem = new BulletSystem(netroidsPhysics, weaponLockSystem, gamePlaySettings, 
                                            effectSystem, asteroidSystem, damageSystem, projectileSystem, timeSystem);

            flamingMeteorSystem = new FlamingMeteorSystem(gamePlaySettings,
                                                          asteroidSystem, effectSystem, damageSystem, projectileSystem);

            meteorStormSystem = new MeteorStormSystem(netroidsPhysics, settings.GamePlay.meteorStormSettings, 
                                                  flamingMeteorSystem, effectSystem, buffSystem, timeSystem);

            shipTurretSystem = new ShipTurretSystem(timeSystem, settings.GamePlay.shipTurretSettings, netroidsPhysics, damageSystem, 
                                                    effectSystem, asteroidSystem);

            weaponsSystem = new WeaponsSystem(gamePlaySettings, raygunSystem, bulletSystem, missileSystem, effectSystem);
            gameModeSystem = new GameModeSystem(dmSystem, endlessDmSystem, teamDmSystem, classicSystem);

            collisionSystem = new CollisionSystem(netroidsPhysics, missileSystem, asteroidSystem, buffSystem,
                                                  gamePlaySettings, effectSystem);

            fixedTurretSystem = new FixedTurretSystem(sceneTurretsData, hasLosSystem, shipSystem, bulletSystem, 
                                                      settings.GamePlay.fixedTurretSettings, timeSystem);
            
            //NetroidsPhysics.Initialize(name, m_SimulationSettings);
            ShipState.maxLockFrames = SecondsToFrames(gamePlaySettings.playerShipSettings.missileLockTime).CheckedAsShort();
            ShipState.framesPerPlasmaShot = SecondsToFrames(gamePlaySettings.playerShipSettings.plasmaShootInterval).CheckedAsShort();
            ShipState.framesPerMissileShot = SecondsToFrames(gamePlaySettings.missileSettings.missileInterval).CheckedAsShort();
            ShipState.framesPerRaygunShot = SecondsToFrames(gamePlaySettings.rayGunSettings.shotInterval).CheckedAsShort();

            MissileState.maxFramesAlive = SecondsToFrames(gamePlaySettings.missileSettings.missileLife).CheckedAsShort();
            FlamingMeteorState.maxFramesAlive = SecondsToFrames(gamePlaySettings.meteorStormSettings.duration).CheckedAsShort();
            initTimer.StopAndLog();
        }

        public void Shutdown()
        {
            netroidsPhysics.Shutdown();
        }

        [Conditional("DEBUG")]
        public void UpdateSettings()
        {
            ShipState.maxLockFrames = SecondsToFrames(gamePlaySettings.playerShipSettings.missileLockTime).CheckedAsShort();
            ShipState.framesPerPlasmaShot = SecondsToFrames(gamePlaySettings.playerShipSettings.plasmaShootInterval).CheckedAsShort();
            ShipState.framesPerMissileShot = SecondsToFrames(gamePlaySettings.missileSettings.missileInterval).CheckedAsShort();
        }

        private GameState m_InvalidMutationCheckState;
        private GameState m_DeterminismCheckState;
        private GameState m_RigidbodyStatesCheck;
        private PhysicsSystemState m_PhysicsSystemState;

        [Conditional("DEBUG_MUTATION")]
        private void MutationCheckStart(ref GameState from)
        {
            GameState.Copy(from, m_InvalidMutationCheckState);
        }

        [Conditional("DEBUG_VERIFY_NO_PHYS_CHANGE")]
        private void PhysicsCheckStart()
        {
            m_PhysicsSystemState = netroidsPhysics.GetSystemState();
        }
        
        [Conditional("DEBUG_VERIFY_NO_PHYS_CHANGE")]
        private void PhysicsCheckEnd()
        {
            if (netroidsPhysics.CheckHasChanged(m_PhysicsSystemState))
            {
                Debug.LogError("Detected change in physics system.");
            }
        }

        /// <summary>
        /// RigidbodyStatesCheckStart & RigidbodyStatesCheckEnd checks no changes to GameState Rigidbody states take place
        /// while the game physics simulation is happening. 
        /// </summary>
        /// <param name="gameState"></param>
        [Conditional("DEBUG_VERIFY_NO_PHYS_CHANGE")]
        private void RigidbodyStatesCheckStart(GameState gameState)
        {
            GameState.Copy(gameState, m_RigidbodyStatesCheck);
        }

        [Conditional("DEBUG_VERIFY_NO_PHYS_CHANGE")]
        private void RigidbodyStatesCheckEnd(GameState gameState)
        {
            if (!GameStateBuffer.AreEqual(m_RigidbodyStatesCheck.rigidBodyStates, gameState.rigidBodyStates))
            {
                Debug.LogError("Rigidbody states have changed");
            }
        }

        public void Simulate(IGameStateRecord iFrom, IGameStateRecord iTo)
        {
            GameState from = (GameState) iFrom;
            GameState to = (GameState) iTo;
            //Collidable.frameCount = iFrom.frame;
            
            MutationCheckStart(ref from);
            PhysicsCheckStart();
            
            UpdateSettings();
            
            GameState.Copy(from, to);
            to.frame = from.frame + 1;
            netroidsPhysics.ClearTeleportedFlag(to.rigidBodyStates);
            
            rigidbodySystem.Update(to);

            fixedTurretSystem.Update(to);
            
            gameModeSystem.Update(to);
           
            playerSystem.Update(to);

            shipSystem.Update(to);

            BuffSystem.UpdateBuffState(to);
        
            PhysicsCheckEnd();

            NetroidsPhysicsSystem netroidsPhysicsSystem = netroidsPhysics.NetroidsPhysicsSystem;
            
            asteroidSystem.UpdateVariations(to);

            netroidsPhysicsSystem.PrepareForPhysicsStep(to);
            
            RigidbodyStatesCheckStart(to);

            playerSystem.ProcessPlayerMoveInput(from, to, this);

            asteroidSystem.UpdateAsteroidBodies(to, this);

            weaponLockSystem.Update(to);
            
            missileSystem.UpdateMissiles(to);

            projectileSystem.Update(to);
            
            bulletSystem.Update(to);
            
            flamingMeteorSystem.Update(to);

            netroidsPhysicsSystem.physicsSystem.Simulate(m_DeltaTime);

            collisionSystem.ProcessCollisions(to);
            
            //RaygunSystem.Update(physicsSystem, to, SettingsManager.RayGunSettings);

            RigidbodyStatesCheckEnd(to);
            
            netroidsPhysicsSystem.physicsSystem.UpdateStatesFromRigidbodies(to);
            
            Profiler.BeginSample("HasLosSystem.Update");
            hasLosSystem.Update(to);
            Profiler.EndSample();

            portalSystem.ProcessTriggers(to);
            
            PhysicsCheckStart();

            portalSystem.Update(to);
            
            meteorStormSystem.Update(to);
            
            shipTurretSystem.Update(to);

            weaponsSystem.Update(to);
            
            asteroidSystem.UpdateAsteroidStates(to);

            playerSystem.LateUpdate(to);
            shipSystem.LateUpdate(to);

            bodyWrapSystem.WrapBodies(from, to, this);
            
            wrapProjectileSystem.Update(to, this);

            gameModeSystem.LateUpdate(from, to);
            
            effectSystem.Update(to);
            
            //DisableCollidedBodies();

            PhysicsCheckEnd();

            netroidsPhysics.Update();
            
            MutationCheckEnd(from);
            DeterminismCheck(from, to);
        }

        private bool m_PerformingCheck;

        [Conditional("DEBUG_MUTATION")]
        private void MutationCheckEnd(GameState from)
        {
            bool condition = GameState.Equals(from, m_InvalidMutationCheckState, out float _);
            if (!condition)
            {
                Debug.LogError($"Mutation of from state not allowed");
            }
        }
        
        [Conditional("DEBUG_DETERMINISM")]
        private void DeterminismCheck(GameState from, GameState to)
        {
            if (m_PerformingCheck)
            {
                return;
            }

            m_PerformingCheck = true;
            Simulate(from, m_DeterminismCheckState);
            if (!GameState.Equals(m_DeterminismCheckState, to, out float diff, 0.0001f))
            {
                Debug.LogError($"Determinism failure from {from.frame} to {to.frame} diff = {diff}.");
            }
            m_PerformingCheck = false;
        }
        
        

        public int SecondsToFrames(float seconds)
        {
            return SecondsToFrames(seconds, m_DeltaTime);
        }
        
        public static int SecondsToFrames(float seconds, float deltaTime)
        {
            return (int)Math.Round(seconds / deltaTime);
        }

        public bool EnoughJuiceForThrust(ShipState shipState, InputState inputState, out float thrust, out int juiceCost, bool hasBoostBuff)
        {
            float thrustNormalized = InputState.ByteToNormalized(inputState.thrust);
            var thrustCost = gamePlaySettings.juiceSettings.thrustCost;
            var thrustSetting = gamePlaySettings.playerShipSettings.thrust;
            thrustSetting = hasBoostBuff ? thrustSetting * gamePlaySettings.buffSettings.boostMultiplier : thrustSetting;
            thrust = thrustSetting * thrustNormalized;
            juiceCost = (int)(thrustCost * m_DeltaTime * thrustNormalized);
            return juiceCost < shipState.juice;
        }

        public bool EnoughJuiceForSecondaryThrust(ShipState shipState, out float thrust,
                                                  out int juiceCost, byte inputThrustState, float thrustSetting)
        {
            float thrustNormalized = InputState.ByteToNormalized(inputThrustState);
            var thrustCost = gamePlaySettings.juiceSettings.thrustCost;
            thrust = thrustSetting * thrustNormalized;
            var thrustCostScale = thrustSetting / gamePlaySettings.playerShipSettings.thrust;
            juiceCost = (int)(thrustCost * m_DeltaTime * thrustNormalized * thrustCostScale);
            return juiceCost < shipState.juice;
        }

        public bool EnoughJuiceForReverseThrust(ShipState shipState, InputState inputState, out float reverseThrust,
                                                out int juiceCost)
        {
            return EnoughJuiceForSecondaryThrust(shipState, out reverseThrust, out juiceCost, inputState.reverseThrust,
                                                 gamePlaySettings.playerShipSettings.thrust);
        }

        public GameState NewRecord()
        {
            GameState gameState = new GameState();
            GameState.Initialize(gameState);
            return gameState;
        }

        public void SetDefaultState(IGameStateRecord gameState)
        {
            GameState.SetDefaultState((GameState)gameState);
        }

        public void CopyNonFinal(IGameStateRecord from, IGameStateRecord to)
        {
            GameState.CopyNonFinal((GameState)from, (GameState)to);
        }

        public void ActivatePlayer(IGameStateRecord iGameState, int playerIndex, string playerName, byte playerFlags, byte skillLevel = 0)
        {
            GameState gameState = (GameState) iGameState;
            Debug.Log($"Activating player {playerIndex}");
            playerSystem.ActivatePlayer(gameState, playerIndex, playerName, playerFlags, skillLevel);
            gameState.FinalizeElementIndex<GamePlayerState>(playerIndex);
        }
        
        public void DeactivatePlayer(IGameStateRecord iGameState, int playerIndex)
        {
            GameState gameState = (GameState)iGameState;
            playerSystem.DeactivatePlayer(gameState, playerIndex);
            gameState.FinalizeElementIndex<GamePlayerState>(playerIndex);

            // Do we need this? PlayerSystem should despawn ships when a player is no longer active.
            gameState.shipStates[playerIndex] = ShipState.s_Default;
            gameState.shipStates[playerIndex].seed = playerIndex;
            gameState.FinalizeElementIndex<ShipState>(playerIndex);
        }

        /// <summary>
        /// Dev func.
        /// </summary>
        /// <returns></returns>
        public IEnumerable<Rigidbody> GetAllSimulatedBodies()
        {
            List<Rigidbody> allBodies = new List<Rigidbody>();
            foreach (NetroidsPhysicsSystem system in netroidsPhysics.netroidsPhysicsSystems)
            {
                foreach (Rigidbody systemBody in system.physicsSystem.allBodies)
                {
                    allBodies.Add(systemBody);
                }
            }
            return allBodies;
        }

        public int GetMaxPlayers()
        {
            return GameState.maxPlayers;
        }

        public IGameStateElement GetPlayerInputElement(IGameStateRecord gameState, short playerIndex, out short playerInputStateIndex)
        {
            playerInputStateIndex = GameState.GetElementIndexForType<InputState>(playerIndex);
            return gameState.GetGameStateElement(playerInputStateIndex);
        }

        public Type GetElementType(IGameStateRecord gameStateRecord, int elementIndex)
        {
            return ((GameState)gameStateRecord).GetElementType(elementIndex);
        }

        public int GetRecordSize()
        {
            return GameState.stateRecordSize;
        }

        public InputState AccumulateState(ref InputState inputState, IInputStateSource inputStateSource)
        {
            return InputStateManager.AccumulateState(inputState, inputStateSource);
        }

        public bool IsPlayerInputState(int gameStateElementIndex)
        {
            if (gameStateElementIndex >= GameState.inputStateBase && gameStateElementIndex < GameState.inputStateTop)
            {
                return true;
            }
            return false;
        }

        /// <summary>
        /// Launch missile at player 0 from edge of arena.
        /// </summary>
        /// <param name="gameState"></param>
        /// <param name="playerIndex"></param>
        public void FireTestMissile(GameState gameState, sbyte playerIndex)
        {
            sbyte ownerPlayerIndex = 0;
            short targetBodyIndex = (GameState.shipBodyBase + playerIndex).CheckedAsShort();
            Vector3 targetPosition = gameState.rigidBodyStates[targetBodyIndex].position;
            Vector3 missileStart = targetPosition + Random.onUnitSphere * 200;
            int missileIndex = missileSystem.TrySpawnMissile(ownerPlayerIndex, targetBodyIndex, missileStart, gameState);
            if (missileIndex != -1)
            {
                gameState.FinalizeElementIndex<MissileState>(missileIndex);
                gameState.FinalizeElementIndex<RigidbodyState>(GameState.missileBodyBase + missileIndex);
            }
        }

        public void FireTestRockStorm(GameState gameState, sbyte playerIndex = 0)
        {
            meteorStormSystem.LaunchAtBody(gameState, (short)(GameState.shipBodyBase + playerIndex), playerIndex);
            gameState.FinalizeAll();
        }

        public bool CanShootBody(int bodyIndex)
        {
            return netroidsPhysics.allBodyLayers[bodyIndex].TestLayerMask(Layers.s_RayHitMask);
        }
    }
}