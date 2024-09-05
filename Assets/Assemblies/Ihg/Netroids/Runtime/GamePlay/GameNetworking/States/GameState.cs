#define SAFE_COPY

using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Ihg.Netroids.Runtime.NetGame.Runtime;
using Ihg.Utils;
using JetBrains.Annotations;
using Unity.Netcode;
using UnityEngine;


namespace Ihg.Netroids.Runtime.GamePlay.GameNetworking.States
{
    public class NonSynced : Attribute { }

    public class GameState : IGameStateRecord, IDisposable
    {
        public struct BasesAndCounts
        {
            public int[] bases;
            public int[] counts;
        }

        [Flags]
        public enum ComponentMask : byte
        {
            HasVelocity = 1 << 0,
            HasRotation = 1 << 1
        }
        
        public static ComponentMask[] bodyComponentMasks;
        
        // Configuration
        public static short maxAsteroids = 50;
        
        // TODO: Players are indexed as sbyte ie 127
        public static short maxPlayers = 1;
        public static short maxTeams = 2;
        public static short maxShips = maxPlayers;
        public static short maxBullets = 10;
        public static short maxEffects = 5;
        public static short maxNetLineEffects = 10;
        public static short maxRigidbodies;
        public static short maxMissiles = (short) (ShipState.ProjectileWeapons * maxPlayers);
        public static short maxRayGuns = (short)(ShipState.RaygunWeapons * maxPlayers);
        public static short maxBuffs = (short)(BuffState.s_NumTypes * maxPlayers);
        public static short maxPortals = maxPlayers;

        public int frame
        {
            get => frameStates[0].frameCount;
            set => frameStates[0].frameCount = value;
        }

        public ref GameModeState GameModeState => ref gameModeStates[0];
        public ref DmState DMState => ref dmStates[0];
        public ref TeamDmState TeamDmState => ref teamTDMStates[0];
        public ref ClassicState ClassicState => ref classicStates[0];
        public ref EndlessDmState EndlessDmState => ref endlessDmStates[0];

        public static readonly Type[] s_TypeOrder = {
            typeof(FrameStateInfo), 
            typeof(GameModeState), 
            typeof(DmState),
            typeof(TeamDmState),
            typeof(ClassicState), 
            typeof(EndlessDmState), 
            typeof(InputState), 
            typeof(GamePlayerState),
            typeof(ShipState), 
            typeof(RigidbodyState), 
            typeof(AsteroidState), 
            typeof(BulletState),
            typeof(ProjectileState),
            typeof(FlamingMeteorState),
            typeof(EffectState), 
            typeof(NetLineEffectState), 
            typeof(MissileState), 
            typeof(RayGunState),
            typeof(BuffState),
            typeof(TeamState),
            typeof(PortalState),
            typeof(OwnerState),
            typeof(DodgeState),
            typeof(BodyRotationState),
            typeof(BodyVelocityState),
            typeof(ShipTurretState),
            typeof(MeteorStormState),
            typeof(HasLosToPlayerState),
            typeof(TrackedMeteorState), 
            typeof(FixedTurretState),
            typeof(AimStateXY)
        };
        
        [SuppressMessage("ReSharper", "UnusedTypeParameter")]
        public struct Base<T, TU>
        {
            // ReSharper disable once StaticMemberInGenericType
            [UsedImplicitly]
            public static int index;

            // ReSharper disable once StaticMemberInGenericType
            [UsedImplicitly]
            public static int top;
        }

        [SuppressMessage("ReSharper", "StaticMemberInGenericType")]
        [SuppressMessage("ReSharper", "UnusedTypeParameter")]
        public struct StateInfo<T>
        {
            public static int baseIndex;
            public static int count;
            public static int top;
        }

        public static readonly Dictionary<Type, short> s_TypeCounts = new Dictionary<Type, short>();
        
        public static readonly Dictionary<Type, Type[]> s_StateOrdersMap = new Dictionary<Type, Type[]>
        {
            { typeof(ProjectileState), new[] { typeof(BulletState), typeof(FlamingMeteorState) } },
            { typeof(OwnerState), new [] { typeof(ProjectileState), typeof(MissileState), typeof(FixedTurretState) } },
            { typeof(RigidbodyState), new []{ typeof(ShipState), typeof(MissileState), typeof(PortalState), typeof(AsteroidState)} },
            { typeof(BodyVelocityState), new[] { typeof(ShipState), typeof(AsteroidState), typeof(MissileState) } },
            { typeof(BodyRotationState), new[] { typeof(ShipState),  typeof(PortalState)  } },
            { typeof(AimStateXY), new[] { typeof(FixedTurretState) } },
            { typeof(HasLosToPlayerState), new []{typeof(FixedTurretState), typeof(ShipState)  } }
        };

        public static readonly Type[] s_OptionalBodyComponents = { typeof(BodyVelocityState), typeof(BodyRotationState) }; 

        public static readonly Dictionary<Type, BasesAndCounts> s_BodyBasesAndCounts =
            new Dictionary<Type, BasesAndCounts>();

        /// <summary>
        /// Number of elements in the state record.
        /// </summary>
        public static int stateRecordSize;

        /// <summary>
        /// Length of each serialized record when using SerializeTo/From
        /// </summary>
        public static int serializedRecordByteLen;

        public static readonly Dictionary<Type, short> s_RigidbodyBases = new Dictionary<Type, short>();
        public static readonly Dictionary<Type, short> s_StateBases = new Dictionary<Type, short>();
        public static readonly Dictionary<Type, short> s_OwnerBases = new Dictionary<Type, short>();
        public static bool[] synced;
        public static Type[] elementTypes;
        
        public static int rigidbodyBase;
        public static int missileOwnerBase;
        public static int asteroidBodyBase;
        public static int shipBodyBase;
        public static int portalBodyBase;
        public static int missileBodyBase;
        public static int inputStateBase;
        public static int inputStateTop;

        public FrameStateInfo[] frameStates;
        public GameModeState[] gameModeStates;
        public DmState[] dmStates;
        public TeamDmState[] teamTDMStates;
        public ClassicState[] classicStates;
        public EndlessDmState[] endlessDmStates;
        public InputState[] inputStates;
        public RigidbodyState[] rigidBodyStates;
        public BodyRotationState[] rotationStates;
        public BodyVelocityState[] velocityStates;
        public AsteroidState[] asteroidStates;
        public BulletState[] bulletStates;
        public FlamingMeteorState[] flamingMeteorStates;
        public GamePlayerState[] playerStates;
        public TeamState[] teamStates;
        public ShipState[] shipStates;
        public PortalState[] portalStates;
        public EffectState[] effectStates;
        public NetLineEffectState[] lineEffectStates;
        public MissileState[] missileStates;
        public RayGunState[] rayGunStates;
        public BuffState[] buffStates;
        public OwnerState[] ownerStates;
        public DodgeState[] dodgeStates;
        public ShipTurretState[] shipTurretStates;
        public MeteorStormState[] meteorStormStates;
        public TrackedMeteorState[] trackedMeteorStates;
        public HasLosToPlayerState[] hasLosToPlayerStates;
        public ProjectileState[] projectileStates;
        public FixedTurretState[] fixedTurretStates;
        public AimStateXY[] aimStates;
        
        /// <summary>
        /// Refers to underling structure arrays.
        /// </summary>
        public IGameStateElement[] gameStateElements;

        public struct StateArrayBuffer
        {
            public unsafe byte* ptr;
            public int size;
            public GCHandle handle;
        }

        public StateArrayBuffer[] stateBuffers;

        private static GameState s_DefaultState;

        /// <summary>
        /// TODO: This should be part of some "GameBufferState", to distinguish from actual games state 
        /// </summary>
        public bool[] final;

        private static Type FindElementType(int elementIndex)
        {
            foreach (KeyValuePair<Type,short> typeBase in s_StateBases)
            {
                if (elementIndex >= typeBase.Value && elementIndex < typeBase.Value + s_TypeCounts[typeBase.Key])
                {
                    return typeBase.Key;
                }
            }
            return default;
        }

        private static int GetRbBase<T>() => s_RigidbodyBases[typeof(T)];
        public static int GetOwnerBase<T>() => s_OwnerBases[typeof(T)];
        public static int GetStateBase<T>() => s_StateBases[typeof(T)];

        public static int[] bodyToVelocityLookup;
        public static int[] bodyToRotationLookup;
        public static int[] velocityToBodyLookup;
        public static int[] rotationToBodyLookup;

        public static short GetStateBase(Type type) => s_StateBases[type];

        public static void Init(GameStateConfig gameStateConfig)
        {
            s_TypeCounts.Clear();
            
            maxPortals = maxPlayers = maxShips = gameStateConfig.maxPlayers;  
            maxAsteroids = gameStateConfig.maxAsteroids;
            maxBullets = gameStateConfig.maxBullets;
            maxEffects = gameStateConfig.maxEffects;
            maxNetLineEffects = gameStateConfig.maxLineEffects;
            maxMissiles = gameStateConfig.maxMissiles;
            maxRayGuns = (short)(maxPlayers * ShipState.RaygunWeapons);
            maxBuffs = (short)(maxPlayers * BuffState.s_NumTypes);

            s_TypeCounts[typeof(FrameStateInfo)] = 1;
            s_TypeCounts[typeof(GameModeState)] = 1;
            s_TypeCounts[typeof(DmState)] = 1;
            s_TypeCounts[typeof(TeamDmState)] = 1;
            s_TypeCounts[typeof(ClassicState)] = 1;
            s_TypeCounts[typeof(EndlessDmState)] = 1;
            s_TypeCounts[typeof(InputState)] = maxPlayers;
            s_TypeCounts[typeof(GamePlayerState)] = maxPlayers;
            s_TypeCounts[typeof(TeamState)] = maxTeams;
            s_TypeCounts[typeof(ShipState)] = maxShips;
            s_TypeCounts[typeof(PortalState)] = maxPortals;
            s_TypeCounts[typeof(AsteroidState)] = maxAsteroids;
            s_TypeCounts[typeof(BulletState)] = maxBullets;
            s_TypeCounts[typeof(EffectState)] = maxEffects;
            s_TypeCounts[typeof(NetLineEffectState)] = maxNetLineEffects;
            s_TypeCounts[typeof(MissileState)] = maxMissiles;
            s_TypeCounts[typeof(RayGunState)] = maxRayGuns;
            s_TypeCounts[typeof(BuffState)] = maxBuffs;
            s_TypeCounts[typeof(DodgeState)] = maxPlayers;
            s_TypeCounts[typeof(FlamingMeteorState)] = gameStateConfig.maxFlamingAsteroids;
            s_TypeCounts[typeof(ShipTurretState)] = maxPlayers;
            s_TypeCounts[typeof(MeteorStormState)] = maxPlayers;
            s_TypeCounts[typeof(FixedTurretState)] = gameStateConfig.maxFixedTurrets;
            s_TypeCounts[typeof(TrackedMeteorState)] = gameStateConfig.maxFlamingAsteroids;
            
            PopulateTypeCounts();

            maxRigidbodies = s_TypeCounts[typeof(RigidbodyState)];

            stateRecordSize = s_TypeCounts.Values.Sum(value => value);

            InitBasesFromOrder(s_StateBases, s_TypeOrder);
            InitBasesFromOrder(s_RigidbodyBases, s_StateOrdersMap[typeof(RigidbodyState)]);
            InitBasesFromOrder(s_OwnerBases, s_StateOrdersMap[typeof(OwnerState)]);
            
            ConstructBasesAndCounts();
            ConstructBodyComponentMasks();
            ConstructBodyToComponentLookups();

            rigidbodyBase = GetStateBase<RigidbodyState>();
            asteroidBodyBase = GetRbBase<AsteroidState>();
            shipBodyBase = GetRbBase<ShipState>();
            portalBodyBase = GetRbBase<PortalState>();
            missileOwnerBase = GetOwnerBase<MissileState>();
            missileBodyBase = GetRbBase<MissileState>();
            inputStateBase = GetStateBase<InputState>();
            inputStateTop = inputStateBase + s_TypeCounts[typeof(InputState)];
            ConfigureBasesAndCounts();
            ConfigureElementTypes();
            ConfigureSynced();
            InitDefaultState();
            InitializeBaseIndices();
        }

        public static void PopulateTypeCounts()
        {
            VerifyTypeCounts();
            
            // Reverse the map to make it easier to propagate counts upwards
            Dictionary<Type, List<Type>> reverseMap = new Dictionary<Type, List<Type>>();
            foreach (KeyValuePair<Type, Type[]> pair in s_StateOrdersMap)
            {
                foreach (Type derivedType in pair.Value)
                {
                    if (!reverseMap.ContainsKey(derivedType))
                    {
                        reverseMap[derivedType] = new List<Type>();
                    }
                    reverseMap[derivedType].Add(pair.Key);
                }
            }

            // Propagate counts up the hierarchy
            Queue<Type> toProcess = new Queue<Type>(s_TypeCounts.Keys);
            while (toProcess.Count > 0)
            {
                Type currentType = toProcess.Dequeue();

                if (reverseMap.TryGetValue(currentType, out List<Type> baseTypes))
                {
                    foreach (Type baseType in baseTypes)
                    {
                        s_TypeCounts.TryAdd(baseType, 0);
                        s_TypeCounts[baseType] += s_TypeCounts[currentType];
                        // Ensure the base type is processed only after all its children are processed
                        if (s_StateOrdersMap[baseType].All(dt => s_TypeCounts.ContainsKey(dt)))
                        {
                            toProcess.Enqueue(baseType);
                        }
                    }
                }
            }
        }
        
        private static void VerifyTypeCounts()
        {
            List<Type> invalidTypes = s_TypeCounts.Keys.Where(k => s_StateOrdersMap.ContainsKey(k)).ToList();
            StringBuilder errorString = new StringBuilder();
            // Log errors for invalid types and initialize their counts to zero
            foreach (var type in invalidTypes)
            {
                errorString.Append($"\ns_TypeCounts contains type {type.Name} with value {s_TypeCounts[type]}. Initializing count to 0.");
                s_TypeCounts[type] = 0;
            }

            if (errorString.Length > 0)
            {
                Debug.LogError(errorString.ToString());
            }
        }

        public static void InitializeBaseIndices()
        {
            // Process each entry in the state orders map
            foreach (KeyValuePair<Type, Type[]> baseEntry in s_StateOrdersMap)
            {
                RecursivelySetIndices(baseEntry.Key, 0, new List<Type>());
            }
        }

        private static void RecursivelySetIndices(Type baseType, int baseIndex, List<Type> visitedTypes)
        {
            if (visitedTypes.Contains(baseType))
            {
                return; // Avoid cycles in the type relationships
            }

            visitedTypes.Add(baseType);

            if (s_StateOrdersMap.TryGetValue(baseType, out Type[] derivedTypes))
            {
                foreach (Type derivedType in derivedTypes)
                {
                    // Create and set the index for Base<baseType, derivedType>
                    Type genericBaseType = typeof(Base<,>).MakeGenericType(baseType, derivedType);
                    genericBaseType.GetField(nameof(Base<int,int>.index)).SetValue(null, baseIndex);
                    genericBaseType.GetField(nameof(Base<int,int>.top)).SetValue(null, baseIndex + s_TypeCounts[derivedType]);

                    // Update the base index for the next derived type
                    int count = s_TypeCounts.TryGetValue(derivedType, out short typeCount) ? typeCount : 0;
                    RecursivelySetIndices(derivedType, baseIndex, new List<Type>(visitedTypes));
                    baseIndex += count;
                }
            }

            visitedTypes.Remove(baseType);
        }
        
        private static void ConfigureElementTypes()
        {
            elementTypes = new Type[stateRecordSize];
            for (int index = 0; index < stateRecordSize; index++)
            {
                elementTypes[index] = FindElementType(index);
            }
        }

        private static void ConfigureSynced()
        {
            synced = new bool[stateRecordSize];
            for (int index = 0; index < stateRecordSize; index++)
            {
                synced[index] = elementTypes[index].GetCustomAttribute<NonSynced>() == null; 
            }
        }

        private static void ConfigureBasesAndCounts()
        {
            foreach (Type type in s_TypeOrder)
            {
                MethodInfo method = typeof(GameState).GetMethod(nameof(ConfigureBaseAndCount), BindingFlags.Static | BindingFlags.NonPublic);
                MethodInfo genericMethod = method.MakeGenericMethod(type);
                genericMethod.Invoke(null, null);
            }
        }

        public static int GetSizeOfType(Type type)
        {
            MethodInfo method = typeof(GameState).GetMethod(nameof(GetSizeOfType), BindingFlags.Static | BindingFlags.NonPublic);
            MethodInfo genericMethod = method.MakeGenericMethod(type);
            return (int) genericMethod.Invoke(null, null);
        }

        private  static unsafe int GetSizeOfType<T>() where T : unmanaged
        {
            return sizeof(T);
        }

        private static void ConfigureBaseAndCount<T>()
        {
            StateInfo<T>.baseIndex = GetStateBase<T>();
            StateInfo<T>.count = s_TypeCounts[typeof(T)];
            StateInfo<T>.top = StateInfo<T>.baseIndex + StateInfo<T>.count;
        }

        /// <summary>
        /// Construct a lookup from rigidbody index back their respective VelocityState and or RotationState.
        /// The lookup will continue -1 for those bodies that don't maintain a velocity state for example. 
        /// </summary>
        private static void ConstructBodyToComponentLookups()
        {
            Dictionary<Type, int[]> bodyComponentToBodyLookups = new Dictionary<Type, int[]>();
            Dictionary<Type, int[]> bodyToBodyComponentLookups = new Dictionary<Type, int[]>();

            foreach (Type bodyComponentType in s_OptionalBodyComponents)
            {
                // Eg Velocity, Rotation.
                int bodyTypeCount = s_TypeCounts[bodyComponentType];
                int [] bodyToComponentLookup = bodyToBodyComponentLookups[bodyComponentType] = new int[maxRigidbodies];
                bodyToComponentLookup.Fill(-1);
                int[] toBodyLookup = bodyComponentToBodyLookups[bodyComponentType] = new int [s_TypeCounts[bodyComponentType]];
                for (int indexIntoBodyComponents = 0, basesIndex = 0, segmentIndex = 0; indexIntoBodyComponents < bodyTypeCount; )
                {
                    BasesAndCounts basesAndCounts = s_BodyBasesAndCounts[bodyComponentType];
                    if (segmentIndex >= basesAndCounts.counts[basesIndex])
                    {
                        basesIndex++;
                        segmentIndex = 0;
                        continue;
                    }
                    int indexIntoRigidbodies = basesAndCounts.bases[basesIndex] + segmentIndex++;
                    bodyToComponentLookup[indexIntoRigidbodies] = indexIntoBodyComponents;
                    toBodyLookup[indexIntoBodyComponents] = indexIntoRigidbodies;
                    indexIntoBodyComponents++;
                }
            }
            bodyToVelocityLookup = bodyToBodyComponentLookups[typeof(BodyVelocityState)];
            bodyToRotationLookup = bodyToBodyComponentLookups[typeof(BodyRotationState)];
            velocityToBodyLookup = bodyComponentToBodyLookups[typeof(BodyVelocityState)];
            rotationToBodyLookup = bodyComponentToBodyLookups[typeof(BodyRotationState)];
        }

        private static void ConstructBodyComponentMasks()
        {
            bodyComponentMasks = new ComponentMask[maxRigidbodies];
            for (int bodyIndex = 0; bodyIndex < bodyComponentMasks.Length; bodyIndex++)
            {
                if (BodyHasComponent<BodyRotationState>(bodyIndex))
                {
                    bodyComponentMasks[bodyIndex] |= ComponentMask.HasRotation;
                } 
                if (BodyHasComponent<BodyVelocityState>(bodyIndex))
                {
                    bodyComponentMasks[bodyIndex] |= ComponentMask.HasVelocity;
                } 
            }
        }

        public static bool BodyHasComponent<T>(int bodyIndex)
        {
            foreach (KeyValuePair<Type, short> keyValuePair in s_RigidbodyBases)
            {
                Type type = keyValuePair.Key;
                if (bodyIndex >= keyValuePair.Value && bodyIndex < s_TypeCounts[type])
                {
                    // The body with specified index is used by the first order type keyValuePair.Key eg Ship,Bullet, or Asteroid etc.
                    // Now see if the body component type eg Velocity or Rotation have that type.
                    return s_StateOrdersMap[typeof(T)].Contains(type);
                }
            }
            return false;
        }

        private static void ConstructBasesAndCounts()
        {
            foreach (Type bodyComponentType in s_OptionalBodyComponents)
            {
                // eg Ship, Asteroid for VelocityState.
                Type[] primaryTypesForBodyComponentTypes = s_StateOrdersMap[bodyComponentType];

                s_BodyBasesAndCounts[bodyComponentType] = new BasesAndCounts
                {
                    bases = new int[primaryTypesForBodyComponentTypes.Length],
                    counts = new int[primaryTypesForBodyComponentTypes.Length]
                };

                // PrimaryType eg Ship
                for (int index = 0; index < primaryTypesForBodyComponentTypes.Length; index++)
                {
                    Type primaryType = primaryTypesForBodyComponentTypes[index];
                    s_BodyBasesAndCounts[bodyComponentType].counts[index] = s_TypeCounts[primaryType];
                    s_BodyBasesAndCounts[bodyComponentType].bases[index] = s_RigidbodyBases[primaryType];
                }
            }
        }

        private static void InitDefaultState()
        {
            s_DefaultState = new GameState();
            InitializeGameState(s_DefaultState);
            
            s_DefaultState.shipStates.Fill(ShipState.s_Default);
            s_DefaultState.asteroidStates.Fill(AsteroidState.s_Default);
            s_DefaultState.effectStates.Fill(EffectState.s_DefaultState);
            s_DefaultState.lineEffectStates.Fill(NetLineEffectState.s_DefaultState);
            s_DefaultState.rotationStates.Fill(BodyRotationState.s_DefaultState);
            s_DefaultState.dodgeStates.Fill(DodgeState.s_DefaultState);
            s_DefaultState.playerStates.Fill(GamePlayerState.s_DefaultState);
            s_DefaultState.trackedMeteorStates.Fill(TrackedMeteorState.s_None);
            InitAsteroidStates(s_DefaultState.asteroidStates);
            InitShipStates(s_DefaultState.shipStates);
            
            serializedRecordByteLen = s_DefaultState.CalculateSerializedRecordLength();
        }
        
        public static void InitBasesFromOrder(Dictionary<Type, short> bases, Type[] order)
        {
            bases[order[0]] = 0; 
            for (int i = 1; i < order.Length; i++)
            {
                Type previousType = order[i - 1];
                Type type = order[i];
                bases[type] = (short)(bases[previousType] + s_TypeCounts[previousType]);
            }            
        }   

        /// <summary>
        /// Initializes the GameState and fill with the default state data.
        /// </summary>
        /// <param name="gameState"></param>
        public static void Initialize(GameState gameState)
        {
            InitializeGameState(gameState);
            SetDefaultState(gameState);
        }

        private static void InitTypeStates(GameState gameState)
        {
            foreach (Type type in s_TypeOrder)
            {
                MethodInfo method = typeof(GameState).GetMethod(nameof(InitTypeStates), BindingFlags.Static | BindingFlags.Public);
                MethodInfo genericMethod = method.MakeGenericMethod(type);
                genericMethod.Invoke(null, new object[]{gameState});
            }
        }

        public static void InitTypeStates<T>(GameState gameState) where T : unmanaged, IGameStateData<T>
        {
            FieldInfo fieldInfo = typeof(GameState).GetFields().FirstOrDefault(fieldInfo => fieldInfo.FieldType.IsArray &&
                                                                                   fieldInfo.FieldType.GetElementType() == typeof(T));
            if (s_TypeCounts[typeof(T)] != StateInfo<T>.count)
            {
                string error = $"TypeCount mismatch s_TypeCounts[{typeof(T).Name}] = {s_TypeCounts[typeof(T)]} StateInfo<{typeof(T).Name}>.count = {StateInfo<T>.count}";
                throw new ArgumentOutOfRangeException(error);
            }
            T[] stateArray = new T[StateInfo<T>.count];
            for (int index = 0; index < stateArray.Length; index++)
            {
                stateArray[index] = default;
            }
            fieldInfo.SetValue(gameState, stateArray);
        }

        public static void FillGameStateElements(GameState gameState)
        {
            foreach (Type type in s_TypeOrder)
            {
                typeof(GameState).GetMethod(nameof(FillGameStateElements), BindingFlags.Public | BindingFlags.Instance).
                                  MakeGenericMethod(type).Invoke(gameState, null);
            }
        }

        public T[] GetStateArray<T>() where T : unmanaged
        {
            return (T[]) GetStateArray(typeof(T));
        }

        public object GetStateArray(Type type)
        {
            FieldInfo fieldInfo = typeof(GameState).GetFields().FirstOrDefault(fieldInfo => fieldInfo.FieldType.IsArray &&
                                                                                   fieldInfo.FieldType.GetElementType() == type);
            return fieldInfo.GetValue(this);
        }

        public static StateArrayBuffer GetStateArrayAsHandle(GameState gameState, Type type)
        {
            FieldInfo fieldInfo = typeof(GameState).GetFields().FirstOrDefault(fi => fi.FieldType.IsArray && fi.FieldType.GetElementType() == type);
            if (fieldInfo == null)
            {
                throw new InvalidOperationException("No matching field found.");
            }
            Array array = (Array)fieldInfo.GetValue(gameState);
            return GetArrayAsStateBuffer(array);
        }

        public static StateArrayBuffer GetArrayAsStateBuffer(Array array)
        {
            unsafe
            {
                int elementSize = GetSizeOfType(array.GetType().GetElementType());
                int bufferSize = array.Length * elementSize;
                GCHandle gch = GCHandle.Alloc(array, GCHandleType.Pinned);
                IntPtr address = gch.AddrOfPinnedObject();

                return new StateArrayBuffer
                {
                    handle = gch,
                    ptr = (byte *) address,
                    size = bufferSize
                };
            }
        }

        public StateArrayBuffer GetStateArrayAsBuffer(Type type)
        {
            return GetStateArrayAsHandle(this, type);
        }
       
        public void FillGameStateElements<T>() where T : unmanaged, IGameStateData<T>
        {
            short baseForType = GetStateBase(typeof(T));
            short countForType = s_TypeCounts[typeof(T)];
            T[] gameStateDatas = GetStateArray<T>();
            for (int stateIndex = 0; stateIndex < countForType; stateIndex++)
            {
                gameStateElements[baseForType + stateIndex] = new GameBufferState<T>(gameStateDatas, stateIndex);
            }
        }
        
        /// <summary>
        /// Initialize the GameState record.
        /// </summary>
        /// <param name="gameState"></param>
        private static void InitializeGameState(GameState gameState)
        {
            unsafe
            {
                InitTypeStates(gameState);
                gameState.final = new bool[stateRecordSize];
                gameState.gameStateElements = new IGameStateElement[stateRecordSize];
                FillGameStateElements(gameState);
                gameState.stateBuffers = new StateArrayBuffer[s_TypeOrder.Length];
                // gameState.stateBytePtrArrays = new byte*[s_TypeOrder.Length];
                // gameState.stateBufferSizes = new int[s_TypeOrder.Length];
                
                for (int index = 0; index < s_TypeOrder.Length; index++)
                {
                    Type type = s_TypeOrder[index];
                    gameState.stateBuffers[index] = gameState.GetStateArrayAsBuffer(type);
                }
                

                List<(IntPtr start, IntPtr end, Type arrayType)> regions = new List<(IntPtr start, IntPtr end, Type arrayType)>();

                // Build the list of regions from pointers and buffer sizes
                for (int i = 0; i < gameState.stateBuffers.Length; i++)
                {
                    IntPtr start = (IntPtr)gameState.stateBuffers[i].ptr;
                    IntPtr end = (IntPtr)(gameState.stateBuffers[i].ptr + gameState.stateBuffers[i].size);
                    regions.Add((start, end, s_TypeOrder[i]));
                }

                // Sort regions by the start pointer
                regions.Sort((a, b) => a.start.ToInt64().CompareTo(b.start.ToInt64()));

                // Check for overlaps
                for (int i = 0; i < regions.Count - 1; i++)
                {
                    if (regions[i].end.ToInt64() > regions[i + 1].start.ToInt64())
                    {
                        Debug.LogError($"{regions[i].arrayType.Name} buffer overlaps {regions[i + 1].arrayType.Name} buffer.");
                    }
                }
            }
        }
        

        private int CalculateSerializedRecordLength()
        {
            int len = stateBuffers.Select(b => b.size).Sum(); 
            len += MemoryMarshal.Cast<bool, byte>(final).Length;
            return len;
        }
        
        public void SerializeStatesTo<T>(Stream stream, T[] array) where T : unmanaged
        {
            stream.Write(MemoryMarshal.Cast<T, byte>(array));
        }
        
        public void SerializeStatesFrom<T>(Stream stream, T[] array) where T : unmanaged
        {
            Span<byte> bytes = MemoryMarshal.Cast<T, byte>(array);
            int read;
            while (!bytes.IsEmpty && (read = stream.Read(bytes)) > 0)
            {
                bytes = bytes.Slice(read);
            }
        }

        public void SerializeTo(Stream stream)
        {
            unsafe
            {
                for (int index = 0; index < s_TypeCounts.Count; index++)
                {
                    Span<byte> buffer = new Span<byte>(stateBuffers[index].ptr, stateBuffers[index].size);
                    stream.Write(buffer);
                }
            }
            //TOOD: Move final
            SerializeStatesTo(stream, final);
        }

        public void SerializeFrom(Stream stream, bool noFinal = false)
        {
            for (int i = 0; i < s_TypeCounts.Count; i++)
            {
                unsafe
                {
                    byte* currentPointer = stateBuffers[i].ptr;
                    int currentSize = stateBuffers[i].size;
                    Span<byte> buffer = new Span<byte>(currentPointer, currentSize);
                    if (stream.Read(buffer) != currentSize)
                    {
                        throw new InvalidOperationException("Stream did not contain enough data for the buffer at index " + i);
                    }
                }
            }
            if (!noFinal)
            {
                //TOOD: Move final
                SerializeStatesFrom(stream, final);
            }
        }

        /// <summary>
        /// Copy default game state data to game state.
        /// </summary>
        /// <param name="gameState"></param>
        public static void SetDefaultState(GameState gameState)
        {
            Copy(s_DefaultState, gameState);
        }
        
        private static void InitShipStates(ShipState[] shipStates)
        {
            for (int index = 0; index < shipStates.Length; index++)
            {
                shipStates[index].seed = index;
            }
        }

        private static void InitAsteroidStates(AsteroidState[] gameStateAsteroidStates)
        {
            for (int index = 0; index < gameStateAsteroidStates.Length; index++)
            {
                gameStateAsteroidStates[index].seed = index;
            }
        }

        public void FinalizeElementIndex(Type type, int elementIndex)
        {
            int elementIndexForType = GetElementIndexForType(type, elementIndex);
            final[elementIndexForType] = true;
        }

        public void FinalizeElementIndex<T>(int stateElementIndex)
        {
            FinalizeElementIndex(typeof(T), stateElementIndex);
        }

        public static void Copy(GameState from, GameState to)
        {
            unsafe
            {
                for (int index = 0; index < s_TypeCounts.Count; index++)
                {
                    // if (from.stateBuffers[index].size != to.stateBuffers[index].size)
                    // {
                    //     Debug.LogError("Buffer size mismatch");
                    // }
                    Buffer.MemoryCopy(from.stateBuffers[index].ptr, to.stateBuffers[index].ptr, to.stateBuffers[index].size, from.stateBuffers[index].size);
                }
            }
            // for (int gameStateIndex = 0; gameStateIndex < stateRecordSize; gameStateIndex++)
            // {
            //     to.gameStateElements[gameStateIndex].Copy(from.gameStateElements[gameStateIndex]);                    
            // }
            from.final.AsSpan().CopyTo(to.final.AsSpan());
        }
        
        public static bool Equals(GameState first, GameState second, out float diff, float tolerance = 0)
        {
            diff = 0;
            for (int gameStateIndex = 0; gameStateIndex < stateRecordSize; gameStateIndex++)
            {
                diff = first.gameStateElements[gameStateIndex].Diff(second.gameStateElements[gameStateIndex]); 
                if (diff > tolerance)
                {
                    return false;
                }                    
            }
            return true;
        }

        public static void CopyNonFinal(GameState from, GameState to)
        { 
            for (int gameStateIndex = 0; gameStateIndex < stateRecordSize; gameStateIndex++)
            {
                bool final = to.final[gameStateIndex];

                if (!final)
                {
                    to.gameStateElements[gameStateIndex].Copy(from.gameStateElements[gameStateIndex]);                    
                }
            }
        }

        /// <summary>
        /// Get the global index of the element state for the given type with the given index within it's own typed state
        /// array. 
        /// </summary>
        /// <param name="index"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static short GetElementIndexForType<T>(short index)
        {
            return (short)(GetStateBase(typeof(T)) + index);
        }

        /// <summary>
        /// Get the state index for type.
        /// </summary>
        /// <param name="stateIndex">Global element index</param>
        /// <typeparam name="T"></typeparam>
        /// <returns>State index</returns>
        public static int GetStateIndexForElement<T>(int stateIndex)
        {
            return stateIndex - GetStateBase<T>();
        }

        public void UpdateState(int index, IGameStateElement gameStateElement)
        {
            try
            {
                gameStateElements[index].Copy(gameStateElement);
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
        }

        /// <summary>
        /// Get the global state index of the ship body state.
        /// </summary>
        /// <param name="playerIndex"></param>
        /// <returns></returns>
        public static short GetPlayerShipBodyStateIndex(short playerIndex)
        {
            short shipBodyIndex = GetShipBodyIndex(playerIndex);
            return GetElementIndexForType<RigidbodyState>(shipBodyIndex);
        }

        public static bool IsShipBodyIndex(int bodyIndex)
        {
            return bodyIndex >= shipBodyBase && bodyIndex < shipBodyBase + maxShips;
        }
        
        public static bool IsShipRigidBodyState(short playerIndex, int stateElementIndex)
        {
            return GetPlayerShipBodyStateIndex(playerIndex) == stateElementIndex;
        }

        /// <summary>
        /// If this bodyIndex represents a body of type T eg Ship/Asteroid return true and set index.
        /// </summary>
        /// <param name="bodyIndex"></param>
        /// <param name="tIndex"></param>
        /// <typeparam name="T"></typeparam>
        /// <returns></returns>
        public static bool TryGetIndexForTypeOfBody<T>(int bodyIndex, out int tIndex) where T : unmanaged
        {
            int bodyBase = GetRbBase<T>();
            int bodyCount = s_TypeCounts[typeof(T)];
            if (bodyIndex >= bodyBase && bodyIndex < bodyBase + bodyCount)
            {
                tIndex = bodyIndex - bodyBase;
                return true;
            }
            tIndex = -1;
            return false;
        }

        public static bool IsAsteroidBody(int rigidbodyIndex)
        {
            return rigidbodyIndex >= asteroidBodyBase && rigidbodyIndex < asteroidBodyBase + maxAsteroids;
        }
        
        public static bool IsPlayerShipState(short playerIndex, int stateElementIndex)
        {
            return GetElementIndexForType<ShipState>(playerIndex) == stateElementIndex;
        }

        public static bool IsPlayerInputState(short playerIndex, int stateElementIndex)
        {
            return GetElementIndexForType<InputState>(playerIndex) == stateElementIndex;
        }
        
        public bool IsPlayerInputState(int gameStateElementIndex)
        {
            if (gameStateElementIndex >= inputStateBase && gameStateElementIndex < inputStateTop)
            {
                return true;
            }
            return false;
        }

        public bool TryGetStateFromElementIndex<T>(int stateElementIndex, out T state) where T : unmanaged, IGameStateData<T> 
        {
            if (IsStateType<T>(stateElementIndex))
            {
                state = (gameStateElements[stateElementIndex] as IGameStateElementBase<T>).StateData;
                return true;
            }
            state = default;
            return false;
        }
        
        public static bool IsStateType<T>(int stateElementIndex) where T : unmanaged, IGameStateData<T>
        {
            int stateBase = GetStateBase<T>();
            return stateElementIndex >= stateBase && stateElementIndex < stateBase + s_TypeCounts[typeof(T)];
        }
        
        public static short GetRigidbodyIndex<T>(int index)
        {
            return (short)(s_RigidbodyBases[typeof(T)] + index);
        }
        
        public static int GetAsteroidBodyIndex(int asteroidIndex)
        {
            return GetRigidbodyIndex<AsteroidState>(asteroidIndex);
        }

        /// <summary>
        /// Get the index for the ships rigidbody in the rigidBodyStates array.
        /// </summary>
        /// <param name="shipIndex"></param>
        /// <returns></returns>
        public static short GetShipBodyIndex(int shipIndex)
        {
            return GetRigidbodyIndex<ShipState>(shipIndex);
        }
        
        public static int GetElementIndexForType(Type type, int index)
        {
            return GetStateBase(type) + index;
        }

        public ref RayGunState GetRayGunState(int shipIndex, int raygunIndex)
        {
            return ref rayGunStates[shipIndex * ShipState.RaygunWeapons + raygunIndex];
        }

        public Type GetElementType(int elementIndex)
        {
            return gameStateElements[elementIndex].GetStateType();
        }

        public void Read(int elementIndex, FastBufferReader reader)
        {
            gameStateElements[elementIndex].Read(reader);
        }

        public void Finalize(int elementIndex)
        {
            final[elementIndex] = true;
        }

        public void FinalizeAll()
        {
            for (int index = 0; index < final.Length; index++)
            {
                final[index] = true;
            }
        }
        
        public bool IsFinal(int elementIndex)
        {
            return final[elementIndex];
        }

        public IGameStateElement GetGameStateElement(short elementIndex)
        {
            return gameStateElements[elementIndex];
        }

        public void SetPlayerInputState(InputState playerInputState, int playerIndex)
        {
            inputStates[playerIndex] = playerInputState;
            FinalizeElementIndex<InputState>(playerIndex);
        }

        public int GetElementCount()
        {
            return gameStateElements.Length;
        }

        public int GetMaxPlayers()
        {
            return maxPlayers;
        }

        public sbyte GetFirstInactivePlayerIndex(sbyte baseSearch = 0)
        {
            ShipState[] states = shipStates;
            for (sbyte playerIndex = baseSearch; playerIndex < states.Length; playerIndex++)
            {
                GamePlayerState playerState = playerStates[playerIndex];
                if (!playerState.Active)
                {
                    return playerIndex;
                }
            }
            return -1;
        }

        public bool TryGetShipIndex(int rigidBodyIndex, out int shipIndex)
        {
            if (rigidBodyIndex >= shipBodyBase && rigidBodyIndex < shipBodyBase + maxShips)
            {
                shipIndex = rigidBodyIndex - shipBodyBase;
                return true;
            }
            shipIndex = -1;
            return false;
        }

        public ref BodyVelocityState GetMissileVelocityStateRW(int missileIndex)
        {
            return ref velocityStates[Base<BodyVelocityState, MissileState>.index + missileIndex];
        }
        
        public BodyVelocityState GetMissileVelocityState(int missileIndex)
        {
            return velocityStates[Base<BodyVelocityState, MissileState>.index + missileIndex];
        }

        public ref BodyVelocityState GetShipVelocityStateRW(int shipIndex)
        {
            return ref velocityStates[Base<BodyVelocityState, ShipState>.index + shipIndex];
        }

        public Vector3 GetBodyVelocity(int bodyIndex)
        {
            int velocityIndex = bodyToVelocityLookup[bodyIndex];
            return velocityIndex == -1 ? Vector3.zero : velocityStates[velocityIndex].velocity; 
        }

        public ref BodyVelocityState GetBodyVelocityStateRW(int bodyIndex)
        {
            return ref velocityStates[bodyToVelocityLookup[bodyIndex]]; 
        }

        public Quaternion GetBodyRotation(int bodyIndex)
        {
            int rotationIndex = bodyToRotationLookup[bodyIndex];
            return rotationIndex == -1 ? Quaternion.identity : rotationStates[rotationIndex].rotation; 
        }

        public void Dispose()
        {
            foreach (StateArrayBuffer buffer in stateBuffers)
            {
                buffer.handle.Free();
            }
        }
    }
}