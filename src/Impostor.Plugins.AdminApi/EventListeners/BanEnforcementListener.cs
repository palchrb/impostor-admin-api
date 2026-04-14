using Impostor.Api.Events;
using Impostor.Api.Games;
using Impostor.Api.Innersloth;
using Impostor.Plugins.AdminApi.Services;
using Microsoft.Extensions.Logging;
using System;

namespace Impostor.Plugins.AdminApi.EventListeners;

public class BanEnforcementListener : IEventListener
{
    private readonly ILogger<BanEnforcementListener> _logger;
    private readonly BanListService _banList;
    private readonly EventBusService _bus;

    public BanEnforcementListener(
        ILogger<BanEnforcementListener> logger,
        BanListService banList,
        EventBusService bus)
    {
        _logger = logger;
        _banList = banList;
        _bus = bus;
    }

    [EventListener]
    public void OnPlayerJoining(IGamePlayerJoiningEvent e)
    {
        var client = e.Player.Client;
        var endpoint = client.Connection?.EndPoint;
        if (endpoint == null)
        {
            return;
        }

        if (_banList.IsBanned(endpoint.Address))
        {
            _logger.LogInformation(
                "Rejecting join from banned IP {Ip} (client {Name})",
                endpoint.Address,
                client.Name);

            e.JoinResult = GameJoinResult.FromError(GameJoinError.Banned);

            // Publish this ourselves instead of adding another listener to the
            // same event - that keeps us independent of listener ordering.
            _bus.Publish(new AdminEvent("player.joining.rejected", DateTime.UtcNow, new
            {
                code = e.Game.Code.Code,
                clientId = client.Id,
                name = client.Name,
                ip = endpoint.Address.ToString(),
                reason = "Banned",
            }));
        }
    }
}
