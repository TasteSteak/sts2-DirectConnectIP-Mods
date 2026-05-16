using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DirectConnectIP.Helpers;
using DirectConnectIP.Patches.Network;
using Godot;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.CharacterSelect;
using MegaCrit.Sts2.Core.Nodes.Screens.CustomRun;
using MegaCrit.Sts2.Core.Nodes.Screens.DailyRun;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Runs;

namespace DirectConnectIP.Network;

public static class ConnectionService
{
    public static async Task ConnectAsync(IClientConnectionInitializer initializer)
    {
        var joinFlow = new JoinFlow();
            
        try
        {
            var tree = (SceneTree)Engine.GetMainLoop();
            var token = MenuStateManager.ConnectionCts?.Token ?? CancellationToken.None;
                
            JoinResult joinResult;
            await using (token.Register(() => joinFlow.CancelToken.Cancel()))
            {
                joinResult = await joinFlow.Begin(initializer, tree);
            }
            var mainMenu = NGame.Instance!.MainMenu;
                
            mainMenu!.OpenMultiplayerSubmenu();
            MenuStateManager.CloseAllPanels();

            NSubmenuStack uiStack = null;
            var startMsec = Time.GetTicksMsec();
            const ulong maxWaitMs = 1500; 

            while (Time.GetTicksMsec() - startMsec < maxWaitMs)
            {
                uiStack = mainMenu.SubmenuStack;
                    
                if (uiStack.IsInsideTree()) break; 

                await tree.ToSignal(tree, SceneTree.SignalName.ProcessFrame);
            }

            if (uiStack == null)
            {
                HandleError(NetError.InternalError, joinFlow.NetService, "等待超时：无法获取官方的 MainMenu.SubmenuStack。");
                return;
            }

            if (!joinResult.sessionState.HasValue)
            {
                HandleError(NetError.InternalError, joinFlow.NetService, "JoinFlow 完成，但未返回有效的 SessionState。");
                return;
            }

            await RouteSessionState(joinResult, joinFlow.NetService);
        }
        catch (OperationCanceledException)
        {
            GD.Print("[DirectConnectIP] 连接已被玩家主动取消。");
        }
        catch (ClientConnectionFailedException ex)
        {
            if (!MenuStateManager.IsConnectionCancelled)
            {
                MenuStateManager.CloseAllPanels();
                GD.PrintErr($"[DirectConnectIP] 客户端连接被拒/失败。错误码: {ex.info.GetErrorString()}");
                PopupHelper.ShowNetError(ex.info); 
            }
        }
        catch (Exception ex)
        {
            if (!MenuStateManager.IsConnectionCancelled)
            {
                MenuStateManager.CloseAllPanels();
                GD.PrintErr($"[DirectConnectIP] 发生未捕获的连接异常:\n{ex.Message}\n{ex.StackTrace}");
                PopupHelper.ShowNetError(new NetErrorInfo(NetError.InternalError, selfInitiated: true));
            }
        }
    }

    private static async Task RouteSessionState(JoinResult result, INetGameService netService)
    {
        switch (result.sessionState.Value)
        {
            case RunSessionState.InLobby:
                if (!result.joinResponse.HasValue)
                {
                    HandleError(NetError.InternalError, netService, "状态为 InLobby，但缺失 joinResponse 数据包！");
                    return;
                }
                PushNewRunLobbyScreen(result.gameMode, result.joinResponse.Value, netService);
                break;

            case RunSessionState.InLoadedLobby:
                if (!result.loadJoinResponse.HasValue)
                {
                    HandleError(NetError.InternalError, netService, "状态为 InLoadedLobby，但缺失 loadJoinResponse 数据包！");
                    return;
                }
                PushLoadedLobbyScreen(result.gameMode, result.loadJoinResponse.Value, netService);
                break;

            case RunSessionState.Running:
                if (!result.rejoinResponse.HasValue)
                {
                    HandleError(NetError.InternalError, netService, "状态为 Running，但缺失 rejoinResponse 数据包！");
                    return;
                }
                await RejoinRunningRunAsync(result.rejoinResponse.Value, netService);
                break;

            case RunSessionState.None:
            default:
                HandleError(NetError.InternalError, netService, $"收到未知或无效的游戏状态: {result.sessionState.Value}");
                break;
        }
    }

    private static void PushNewRunLobbyScreen(GameMode mode, ClientLobbyJoinResponseMessage response, INetGameService netService)
    {
        switch (mode)
        {
            case GameMode.Standard:
                UiStackHelper.PushScreen<NCharacterSelectScreen>(s => s.InitializeMultiplayerAsClient(netService, response));
                break;
            case GameMode.Daily:
                UiStackHelper.PushScreen<NDailyRunScreen>(s => s.InitializeMultiplayerAsClient(netService, response));
                break;
            case GameMode.Custom:
                UiStackHelper.PushScreen<NCustomRunScreen>(s => s.InitializeMultiplayerAsClient(netService, response));
                break;
            case GameMode.None:
                HandleError(NetError.InternalError, netService, "GameMode 为 None，无法初始化大厅界面。");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的 GameMode。");
        }
    }

    private static void PushLoadedLobbyScreen(GameMode mode, ClientLoadJoinResponseMessage response, INetGameService netService)
    {
        switch (mode)
        {
            case GameMode.Standard:
                UiStackHelper.PushScreen<NMultiplayerLoadGameScreen>(s => s.InitializeAsClient(netService, response));
                break;
            case GameMode.Daily:
                UiStackHelper.PushScreen<NDailyRunLoadScreen>(s => s.InitializeAsClient(netService, response));
                break;
            case GameMode.Custom:
                UiStackHelper.PushScreen<NCustomRunLoadScreen>(s => s.InitializeAsClient(netService, response));
                break;
            case GameMode.None:
                HandleError(NetError.InternalError, netService, "GameMode 为 None，无法初始化读档大厅界面。");
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(mode), mode, "未知的 GameMode。");
        }
    }

    private static async Task RejoinRunningRunAsync(ClientRejoinResponseMessage response, INetGameService netService)
    {
        try
        {
            GD.Print("[DirectConnectIP] 收到运行中重连快照，正在恢复到房主权威状态。");

            MenuStateManager.CloseAllPanels();
            var game = NGame.Instance;
            if (game == null)
            {
                HandleError(NetError.InternalError, netService, "无法获取 NGame 实例，运行中重连失败。");
                return;
            }

            await game.Transition.FadeOut();
            PrepareForRunningSnapshotRejoin(netService);

            var run = response.serializableRun;
            var loadMessage = new ClientLoadJoinResponseMessage
            {
                serializableRun = run,
                playersAlreadyConnected = run.Players.Select(p => p.NetId).ToList()
            };
            var lobby = new LoadRunLobby(netService, new RejoinLoadRunLobbyListener(), loadMessage);
            var runState = RunState.FromSerializable(run);
            await RunManager.Instance.SetUpSavedMultiPlayer(runState, lobby);
            await game.LoadRun(runState, run.PreFinishedRoom);
            lobby.CleanUp(disconnectSession: false);
            await game.Transition.FadeIn();
        }
        catch (Exception ex)
        {
            GD.PrintErr($"[DirectConnectIP] 运行中重连恢复失败:\n{ex.Message}\n{ex.StackTrace}");
            HandleError(NetError.InternalError, netService, "运行中重连恢复失败。");
        }
    }

    private static void PrepareForRunningSnapshotRejoin(INetGameService incomingNetService)
    {
        var runManager = RunManager.Instance;
        if (runManager.DebugOnlyGetState() == null) return;

        if (ReferenceEquals(runManager.NetService, incomingNetService))
        {
            throw new InvalidOperationException("运行中重连前发现新连接已绑定到旧 RunManager，无法安全清理旧状态。");
        }

        GD.Print("[DirectConnectIP] 清理本地旧运行状态，准备载入房主权威快照。");
        runManager.CleanUp(false);
        OfflineTakeoverCore.IsDirectConnectActive = incomingNetService.IsConnected;
    }

    private sealed class RejoinLoadRunLobbyListener : ILoadRunLobbyListener
    {
        public void PlayerConnected(ulong playerId) { }
        public void RemotePlayerDisconnected(ulong playerId) { }
        public Task<bool> ShouldAllowRunToBegin() => Task.FromResult(true);
        public void BeginRun() { }
        public void PlayerReadyChanged(ulong playerId) { }

        public void LocalPlayerDisconnected(NetErrorInfo info)
        {
            PopupHelper.ShowNetError(info);
        }
    }

    private static void HandleError(NetError reason, INetGameService netService, string debugLogMessage)
    {
        GD.PrintErr($"[DirectConnectIP - 错误拦截] {debugLogMessage} (触发 UI 报错: {reason})");
        PopupHelper.ShowNetError(new NetErrorInfo(reason, selfInitiated: true));
        netService?.Disconnect(reason, now: true);
    }
}
