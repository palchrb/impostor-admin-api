using System;
using Impostor.Api.Events;
using Impostor.Api.Events.Client;
using Impostor.Api.Events.Meeting;
using Impostor.Api.Events.Player;
using Impostor.Plugins.AdminApi.Services;

namespace Impostor.Plugins.AdminApi.EventListeners;

/// <summary>
/// Captures game/player/client events and pushes them to the EventBus (for SSE)
/// and ChatLog (for chat buffer).
/// </summary>
public class AdminEventBroadcaster : IEventListener
{
    private readonly EventBusService _bus;
    private readonly ChatLogService _chatLog;

    public AdminEventBroadcaster(EventBusService bus, ChatLogService chatLog)
    {
        _bus = bus;
        _chatLog = chatLog;
    }

    [EventListener]
    public void OnClientConnected(IClientConnectedEvent e)
    {
        var endpoint = e.Client.Connection?.EndPoint;
        _bus.Publish(new AdminEvent("client.connected", DateTime.UtcNow, new
        {
            clientId = e.Client.Id,
            name = e.Client.Name,
            ip = endpoint?.Address.ToString(),
            platform = e.Client.PlatformSpecificData.Platform.ToString(),
            version = e.Client.GameVersion.Value,
        }));
    }

    [EventListener]
    public void OnGameCreated(IGameCreatedEvent e)
    {
        _bus.Publish(new AdminEvent("game.created", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            hostName = e.Host?.Name,
            hostIp = e.Host?.Connection?.EndPoint?.Address.ToString(),
        }));
    }

    [EventListener]
    public void OnGameDestroyed(IGameDestroyedEvent e)
    {
        _bus.Publish(new AdminEvent("game.destroyed", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
        }));
    }

    [EventListener]
    public void OnGameStarting(IGameStartingEvent e)
    {
        _bus.Publish(new AdminEvent("game.starting", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            playerCount = e.Game.PlayerCount,
        }));
    }

    [EventListener]
    public void OnGameStarted(IGameStartedEvent e)
    {
        _bus.Publish(new AdminEvent("game.started", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            playerCount = e.Game.PlayerCount,
        }));
    }

    [EventListener]
    public void OnGameEnded(IGameEndedEvent e)
    {
        _bus.Publish(new AdminEvent("game.ended", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            reason = e.GameOverReason.ToString(),
        }));
    }

    [EventListener]
    public void OnGameAlter(IGameAlterEvent e)
    {
        _bus.Publish(new AdminEvent("game.privacyChanged", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            isPublic = e.IsPublic,
        }));
    }

    [EventListener]
    public void OnGameHostChanged(IGameHostChangedEvent e)
    {
        _bus.Publish(new AdminEvent("game.hostChanged", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            previousHostClientId = e.PreviousHost.Client.Id,
            previousHostName = e.PreviousHost.Client.Name,
            newHostClientId = e.NewHost?.Client.Id,
            newHostName = e.NewHost?.Client.Name,
        }));
    }

    [EventListener]
    public void OnGameOptionsChanged(IGameOptionsChangedEvent e)
    {
        var options = e.Game.Options;
        _bus.Publish(new AdminEvent("game.optionsChanged", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            changedBy = e.ChangedBy.ToString(),
            maxPlayers = options.MaxPlayers,
            numImpostors = options.NumImpostors,
            mapId = (int)options.Map,
            gameMode = options.GameMode.ToString(),
            languageKeywords = (long)options.Keywords,
        }));
    }

    [EventListener]
    public void OnPlayerJoined(IGamePlayerJoinedEvent e)
    {
        var endpoint = e.Player.Client.Connection?.EndPoint;
        _bus.Publish(new AdminEvent("player.joined", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            clientId = e.Player.Client.Id,
            name = e.Player.Client.Name,
            ip = endpoint?.Address.ToString(),
            isHost = e.Player.IsHost,
        }));
    }

    [EventListener]
    public void OnPlayerLeft(IGamePlayerLeftEvent e)
    {
        _bus.Publish(new AdminEvent("player.left", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            clientId = e.Player.Client.Id,
            name = e.Player.Client.Name,
        }));
    }

    [EventListener]
    public void OnPlayerChat(IPlayerChatEvent e)
    {
        var client = e.ClientPlayer.Client;
        var endpoint = client.Connection?.EndPoint;
        var code = e.Game.Code.Code;
        var playerName = e.PlayerControl.PlayerInfo?.PlayerName ?? client.Name;

        var chatMessage = new ChatMessage(
            Timestamp: DateTime.UtcNow,
            GameCode: code,
            ClientId: client.Id,
            PlayerName: playerName,
            Ip: endpoint?.Address.ToString(),
            Message: e.Message);

        _chatLog.Add(chatMessage);

        _bus.Publish(new AdminEvent("chat", DateTime.UtcNow, chatMessage));
    }

    [EventListener]
    public void OnPlayerMurder(IPlayerMurderEvent e)
    {
        _bus.Publish(new AdminEvent("player.murder", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            killerName = e.PlayerControl.PlayerInfo?.PlayerName,
            victimName = e.Victim.PlayerInfo?.PlayerName,
            result = e.Result.ToString(),
        }));
    }

    [EventListener]
    public void OnPlayerStartMeeting(IPlayerStartMeetingEvent e)
    {
        var callerName = e.PlayerControl.PlayerInfo?.PlayerName ?? e.ClientPlayer.Client.Name;
        _bus.Publish(new AdminEvent("meeting.called", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            callerClientId = e.ClientPlayer.Client.Id,
            callerName,
            // e.Body is null for an emergency button; non-null means a body was reported.
            isEmergency = e.Body == null,
            reportedBodyName = e.Body?.PlayerInfo?.PlayerName,
        }));
    }

    [EventListener]
    public void OnPlayerExile(IPlayerExileEvent e)
    {
        var name = e.PlayerControl.PlayerInfo?.PlayerName ?? e.ClientPlayer.Client.Name;
        _bus.Publish(new AdminEvent("player.exiled", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            clientId = e.ClientPlayer.Client.Id,
            name,
        }));
    }

    [EventListener]
    public void OnPlayerVoted(IPlayerVotedEvent e)
    {
        var voterName = e.PlayerControl.PlayerInfo?.PlayerName ?? e.ClientPlayer.Client.Name;
        _bus.Publish(new AdminEvent("player.voted", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            voterClientId = e.ClientPlayer.Client.Id,
            voterName,
            voteType = e.VoteType.ToString(),
            votedForName = e.VotedFor?.PlayerInfo?.PlayerName,
        }));
    }

    [EventListener]
    public void OnMeetingStarted(IMeetingStartedEvent e)
    {
        _bus.Publish(new AdminEvent("meeting.started", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
        }));
    }

    [EventListener]
    public void OnMeetingEnded(IMeetingEndedEvent e)
    {
        _bus.Publish(new AdminEvent("meeting.ended", DateTime.UtcNow, new
        {
            code = e.Game.Code.Code,
            exiledName = e.Exiled?.PlayerInfo?.PlayerName,
            isTie = e.IsTie,
        }));
    }
}
