using System;
using System.Runtime.InteropServices;
using Ihg.Netroids.Runtime.GamePlay.GameNetworking.Simulation;
using Ihg.Netroids.Runtime.GamePlay.GameNetworking.States;
using Ihg.Utils;
using UnityEngine;
using UnityEngine.Assertions;

namespace Ihg.Netroids.Runtime.NetGame.Runtime
{
    /// <summary>
    /// Manage a fixed set of game state frames. 
    /// </summary>
    public class GameStateBuffer
    {
        private int m_BufferSize;
        
        private IGameStateRecord[] m_GameStates;

        /// <summary>
        /// Next frame to simulate from.
        /// </summary>
        public int currentFrame;
        
        /// <summary>
        /// Last final frame received for given state.
        /// </summary>
        public int[] finalFrames;
        
        /// <summary>
        /// Latest frame in buffer.
        /// </summary>
        public int latestFrame;

        /// <summary>
        /// Total number of GameState records  stored in buffer.
        /// </summary>
        public int BufferSize => m_BufferSize;

        /// <summary>
        /// Oldest frame in buffer.
        /// </summary>
        public int MinFrame => currentFrame + m_MinDelta;

        public int MaxFrame => currentFrame + m_MaxDelta;

        /// <summary>
        /// Minimum allowable current frame value.
        /// </summary>
        public int HardFloor => m_HardFloor;

        /// <summary>
        /// Delta from current of oldest valid frame in buffer. 
        /// </summary>
        private int m_MinDelta;
        
        /// <summary>
        /// Delta from current of latest valid frame in buffer.
        /// </summary>
        private int m_MaxDelta;

        /// <summary>
        /// Minimum allowable current frame value.
        /// </summary>
        private int m_HardFloor;

        public readonly GameSimulation gameSimulation;
        
        public GameStateBuffer(GameSimulation gameSimulation)
        {
            this.gameSimulation = gameSimulation;
        }

        public void Initialize(int size)
        {
            Assert.IsTrue(size > 0);
            m_BufferSize = size;
            int halfBufferSize = m_BufferSize / 2;
            m_MinDelta = -(m_BufferSize % 2 == 0 ? halfBufferSize - 1 : halfBufferSize);
            m_MaxDelta = halfBufferSize;
            currentFrame = m_HardFloor = halfBufferSize - 1;
            InitializeGameStates();
        }
        
        public Vector2 GetMinMaxDelta()
        {
            return new Vector2(m_MinDelta, m_MaxDelta);
        }

        public Vector2 GetMinMax()
        {
            Assert.IsTrue(currentFrame >= m_HardFloor);
            return new Vector2(currentFrame + m_MinDelta, currentFrame + m_MaxDelta);
        }

        public bool InRange(int frame)
        {
            Vector2 minMax = GetMinMax();
            return frame >= minMax.x && frame <= minMax.y;
        }

        private void InitializeGameStates()
        {
            m_GameStates = new IGameStateRecord[BufferSize];
            finalFrames = new int[gameSimulation.GetRecordSize()];
            finalFrames.Fill(-1);

            for (int index = 0; index < m_GameStates.Length; index++)
            {
                m_GameStates[index] = gameSimulation.NewRecord();
                m_GameStates[index].frame = index;
            }
        }

        public IGameStateRecord GetGameState(int stateFrame)
        {
            Assert.IsTrue(InRange(stateFrame));
            return m_GameStates[stateFrame % BufferSize];
        }

        public GameState GetCurrentGameState()
        {
            return (GameState) GetGameState(currentFrame);
        }

        public IGameStateRecord GetCurrentGameState(out int frame)
        {
            frame = currentFrame;
            return GetGameState(currentFrame);
        }
        
        public void GetFromToStates(int fromFrame, out IGameStateRecord from, out IGameStateRecord to)
        {
            Assert.IsTrue(InRange(fromFrame));
            Assert.IsTrue(InRange(fromFrame + 1));
            from = m_GameStates[fromFrame % BufferSize];
            to = m_GameStates[(fromFrame + 1) % BufferSize];
        }

        public GameState GetFrameStateUnchecked(int frame)
        {
            return (GameState)m_GameStates[frame % BufferSize];
        }
        
        public void ClearOldestState()
        {
            int elementIndex = MinFrame % BufferSize;
            gameSimulation.SetDefaultState(m_GameStates[elementIndex]);
            m_GameStates[elementIndex].frame = MinFrame;
        }

        public void ClearNewestState()
        {
            int elementIndex = MaxFrame % BufferSize;
            gameSimulation.SetDefaultState(m_GameStates[elementIndex]);
            m_GameStates[elementIndex].frame = MaxFrame;
        }

        /// <summary>
        /// Set the current frame, clearing old state data as required.
        /// </summary>
        /// <param name="newCurrent"></param>
        public void MoveToFrame(int newCurrent)
        {
            Assert.IsTrue(newCurrent >= m_HardFloor);
            if (newCurrent < MinFrame || newCurrent > MaxFrame)
            {
                currentFrame = newCurrent;
                ClearAll();
            }
            else
            {
                int offset = newCurrent - currentFrame;
                MoveCurrentFrame(offset);
            }
        }
        
        public void MoveCurrentFrame(int offset)
        {
            if (offset < 0)
            {
                // Rewind.
                for (int i = 0; i > offset; i--)
                {
                    ClearOldestState();
                    currentFrame--;
                }
                Assert.IsFalse(currentFrame < m_HardFloor);
            }
            else
            {
                // Fast forward.
                for (int i = 0; i < offset; i++)
                {
                    ClearNewestState();
                    currentFrame++;
                }
            }
        }
        
        public bool TryGetGameState(int stateFrame, out IGameStateRecord gameState)
        {
            gameState = m_GameStates[stateFrame % BufferSize];
            return InRange(stateFrame);
        }

        public void ClearAll()
        {
            for (int index = 0; index < m_GameStates.Length; index++)
            {
                IGameStateRecord gameState = m_GameStates[index];
                gameSimulation.SetDefaultState(gameState);
                gameState.frame = MinFrame + index;
            }
        }

        public Texture2D CreateStateTexture()
        {
            Texture2D stateTexture = new Texture2D(gameSimulation.GetRecordSize(), BufferSize, TextureFormat.ARGB32, false)
            {
                filterMode = FilterMode.Point
            };

            return stateTexture;
        }

        public static void Copy<T>(T[] src, T[] dest) where T : unmanaged, IGameStateData<T>
        {
            Span<byte> readOnlySpan = MemoryMarshal.Cast<T, byte>(src);
            Span<byte> targetSpan = MemoryMarshal.Cast<T, byte>(dest);
            readOnlySpan.CopyTo(targetSpan);
        }

        public static bool AreEqual<T>(T[] src, T[] dest) where T : unmanaged
        {
            Span<byte> readOnlySpan = MemoryMarshal.Cast<T, byte>(src);
            Span<byte> targetSpan = MemoryMarshal.Cast<T, byte>(dest);
            return readOnlySpan.SequenceCompareTo(targetSpan) == 0;
        }
        
        public static bool AreEqual(RigidbodyState[] rigidbodyStates, RigidbodyState[] otherStates)
        {
            for (int index = 0; index < rigidbodyStates.Length; index++)
            {
                if (rigidbodyStates[index].Diff(otherStates[index]) > 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}