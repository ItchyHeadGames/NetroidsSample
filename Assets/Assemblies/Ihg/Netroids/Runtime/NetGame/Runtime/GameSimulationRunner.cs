using System;
using Ihg;
using Ihg.Netroids.Runtime.GameBase;
using Ihg.Netroids.Runtime.GamePlay.GameNetworking.Simulation;
using Ihg.Netroids.Runtime.GamePlay.GameNetworking.States;
using Ihg.Utils;
using Unity.Netcode;
using UnityEngine;
using Physics = UnityEngine.Physics;

namespace Ihg.Netroids.Runtime.NetGame.Runtime
{
    public class GameSimulationRunner
    {
        private GameStateBuffer m_StateBuffer;
        private GameSimulation m_Simulation;
        private IGameStateRecord m_TempState;

        public GameStateBuffer StateBuffer => m_StateBuffer;
        public GameSimulation Simulation => m_Simulation;

        public int replayFrames;
        
        public void Init(string name, Settings gameSettings, SimulationRunnerSettings simulationRunnerSettings, 
                         GameSimulation gameSimulation, Settings.SharedSettings sharedSettings)
        {
            LoggedStopWatch initTimer = LoggedStopWatch.StartNewStopWatch(System.Reflection.MethodBase.GetCurrentMethod().Name); 
            m_Simulation = gameSimulation;
            m_StateBuffer = new GameStateBuffer(m_Simulation);
            m_Simulation.Initialize(Time.fixedDeltaTime, name, gameSettings, sharedSettings);
            m_StateBuffer.Initialize(simulationRunnerSettings.gameStateBufferSize);

            m_TempState = m_Simulation.NewRecord();
            replayFrames = simulationRunnerSettings.replayFrames;
            initTimer.StopAndLog();
        }

        public void Shutdown()
        {
            m_Simulation.Shutdown();
        }

        public readonly SlidingAverageFloat incomingDataMonitor = new SlidingAverageFloat(200);

        public void ProcessIncomingStates(FastBufferReader payLoad)
        {
            payLoad.ReadValueSafe(out int frame);
            payLoad.ReadValueSafe(out byte count);
            payLoad.ReadValueSafe(out short stateBase);
            short nextState = stateBase;
            ProcessIncomingState(frame, stateBase, payLoad);
            for (int i = 1; i < count; i++)
            {
                payLoad.ReadValueSafe(out byte stateIndexOffset);
                nextState += stateIndexOffset;
                nextState = (short)(nextState % GameState.stateRecordSize); 
                ProcessIncomingState(frame, nextState, payLoad);
            }
        }

        public void ProcessIncomingState(FastBufferReader payLoad)
        {
            payLoad.ReadValueSafe(out int frame);
            payLoad.ReadValueSafe(out short stateIndex);
            ProcessIncomingState(frame, stateIndex, payLoad);
        }
        
        public void ProcessIncomingState(int frame, int stateElementIndex, FastBufferReader reader)
        {
            if (!m_StateBuffer.InRange(frame))
            {
                Debug.LogWarning(
                    $"Frame data {frame} is out of range for simulation buffer {m_StateBuffer.GetMinMax().x} -> {m_StateBuffer.GetMinMax().y}");
                incomingDataMonitor.AddValue(0);
                return;
            }

            int rewindFrame = GetRewindFrame();

            if (frame < rewindFrame)
            {
                Debug.LogWarning(
                    $"Frame data {frame} is outside of the replay buffer range {rewindFrame} -> {m_StateBuffer.GetMinMax().y} currentFrame = {m_StateBuffer.currentFrame}");
                frame = rewindFrame;
                incomingDataMonitor.AddValue(0);
            }

            IGameStateRecord bufferState = m_StateBuffer.GetGameState(frame);

            // Debug.Log(
            //     $"Received state {stateElementIndex} frame {frame} type {bufferState.gameStateElements[stateElementIndex].GetStateType().Name}");

            bufferState.Read(stateElementIndex, reader);

            // if (frame > m_StateBuffer.latestFrame)
            // {
            //     m_StateBuffer.ClearFinalStates(m_StateBuffer.latestFrame, frame);
            //     m_StateBuffer.latestFrame = frame;
            // }

            //Debug.Log($"ReceiveState {bufferState.gameStateElements[stateElementIndex].StateData.GetType().Name}[{stateElementIndex}] frame={frame}");

            if (frame != bufferState.frame)
            {
                //Debug.LogError($"Frame count mismatch incoming = {frame} buffer = {bufferState.frame}");
                bufferState.frame = frame;
            }
            bufferState.Finalize(stateElementIndex);

            m_StateBuffer.finalFrames[stateElementIndex] =
                Math.Max(m_StateBuffer.finalFrames[stateElementIndex], frame);
            incomingDataMonitor.AddValue(1);
        }

        public void Step()
        {
            Physics.simulationMode = SimulationMode.Script;
            Replay();
            Simulate();
            Physics.simulationMode = SimulationMode.FixedUpdate;
        }

        public int CalculateRewindFrame() => m_StateBuffer.currentFrame - replayFrames;
        
        public int GetRewindFrame() => Mathf.Max(m_StateBuffer.MinFrame, CalculateRewindFrame());

        private void Replay()
        {
            int currentFrame = m_StateBuffer.currentFrame;
            //Assert.IsTrue(currentFrame <= m_StateBuffer.latestFrame);
            int rewindFrame = GetRewindFrame();

            for (int frame = rewindFrame; frame < currentFrame; frame++)
            {
                m_StateBuffer.GetFromToStates(frame, out IGameStateRecord fromState, out IGameStateRecord toState);
                m_Simulation.Simulate(fromState, m_TempState);
                m_Simulation.CopyNonFinal(m_TempState, toState);
            }
        }

        public void Simulate()
        {
            int currentFrame = m_StateBuffer.currentFrame;
            m_StateBuffer.GetFromToStates(currentFrame, out IGameStateRecord fromState, out IGameStateRecord toState);

            m_Simulation.Simulate(fromState, m_TempState);
            m_Simulation.CopyNonFinal(m_TempState, toState);
            m_StateBuffer.MoveCurrentFrame(1);

            // if (m_StateBuffer.currentFrame % 10 == 0)
            // {
            //     Debug.Log($"m_StateBuffer.currentFrame = {m_StateBuffer.currentFrame} @ {NetworkSystemState.RealTimeMs}");
            // }
            //
            // if (m_StateBuffer.latestFrame < m_StateBuffer.currentFrame)
            // {
            //     m_StateBuffer.latestFrame = m_StateBuffer.currentFrame;
            //     toState.final.Fill(false);
            // }
        }

        public void ActivatePlayer(int playerIndex, string playerName, byte playerFlags, byte skillLevel = 0)
        {
            m_Simulation.ActivatePlayer(StateBuffer.GetCurrentGameState(), playerIndex, playerName, playerFlags, skillLevel);
        }

        public void DeactivatePlayer(int playerIndex)
        {
            m_Simulation.DeactivatePlayer(StateBuffer.GetCurrentGameState(), playerIndex);
        }

        public void DeactivateAllPlayers()
        {
            // Remove human player.
            // Fill with 6 bots of each type.
            GameState currentGameState = StateBuffer.GetCurrentGameState();

            // Deactivate all current players
            for (int playerIndex = 0; playerIndex < currentGameState.playerStates.Length; playerIndex++)
            {
                GamePlayerState gamePlayerState = currentGameState.playerStates[playerIndex];

                if (gamePlayerState.Active)
                {
                    DeactivatePlayer(playerIndex);
                }
            }
        }

        public sbyte GetFirstInactivePlayerIndex()
        {
            return StateBuffer.GetCurrentGameState().GetFirstInactivePlayerIndex();
        }
        
        public void SetGameMode(GameModeState.GameMode gameMode)
        {
            GameState gameState = StateBuffer.GetCurrentGameState();
            gameState.GameModeState.gameMode = gameMode;
            gameState.Finalize(GameState.GetStateBase<GameModeState>());
        }
    }
}