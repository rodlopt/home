using System;
using System.Collections.Generic;
using Sandbox;

namespace Home;

public partial class HomePlayer
{
    [Net] public bool IsAdmin {get; set;} = false;
    [Net] public bool IsModerator {get; set;} = false;

    public IDictionary<long, float> PlayerVolumes {get; set;} = new Dictionary<long, float>();

    private void LoadAdmin()
    {
        Game.AssertClient();

        // Load Player Volumes
        foreach(var player in Game.Clients)
        {
            if(player.SteamId == Client.SteamId) continue;

            var voice = Cookie.Get<float>("home.voice." + player.SteamId.ToString(), 1);
            if(voice != 1.0f)
            {
                PlayerVolumes[player.SteamId] = voice;
            }
        }
    }

    private void InitRole(IClient client)
    {
        Log.Info("🏠: Initializing player role...");
        switch(client.SteamId)
        {
            // Owner
            case 76561198031113835: // Carson
                IsAdmin = true;
                IsModerator = true;
                break;
            
            // Moderators
            case 76561198038564197: // Yukon
            case 76561198043458654: // Del Pickle
            case 76561198037527559: // Stella Wisps
            case 76561198160401653: // Milk Man
                IsModerator = true;
                break;
        }

        if(IsAdmin) Log.Info("🏠: Player is an admin.");
        if(IsModerator) Log.Info("🏠: Player is a moderator.");
    }

    private async void LoadBadges()
    {
        if(HasOwnerPermissions()) GiveBadge("owner");

        switch(Client.SteamId)
        {
            // Developers/Contributors
            case 76561198031113835: // Carson
            case 76561197990720321: // ShadowBrain
            case 76561198155010327: // Luke
            case 76561197972285500: // ItsRifter
            case 76561198048910256: // Sugma Gaming
                GiveBadge("developer");
                break;

            // Playtesters
            case 76561198330783877: // Ian
            case 76561197969358147: // Jammie
            case 76561197960279240: // Hearth
            case 76561198045068860: // Trundler
                GiveBadge("playtester");
                break;
        }

        if(IsAdmin) GiveBadge("admin");
        if(IsModerator) GiveBadge("moderator");

        Log.Info("🏠: Awaiting game package...");
        Log.Info($"Getting game package carsonk.home");
        var package = await Package.FetchAsync("carsonk.home", false);
        if(package != null)
        {
            // More than 30 days of playtime
            if(package.Interaction.Seconds > 2592000)
            {
                GiveBadge("30d");
            }
            // More than 7 days of playtime
            else if(package.Interaction.Seconds > 604800)
            {
                GiveBadge("7d");
            }
            // More than 24 hours of playtime
            else if(package.Interaction.Seconds > 86400)
            {
                GiveBadge("24h");
            }
        }
    }

    public void TogglePlayerMute(long steamId)
    {
        Game.AssertClient();
        if(PlayerVolumes.ContainsKey(steamId) && PlayerVolumes[steamId] == 0)
        {
            PlayerVolumes[steamId] = 1;
        }
        else
        {
            PlayerVolumes[steamId] = 0;
        }
        Cookie.Set("home.voice." + steamId.ToString(), PlayerVolumes[steamId]);
    }

    public bool HasOwnerPermissions()
    {
        return (Client.SteamId == 76561198031113835);
    }

    public bool HasAdminPermissions()
    {
        return IsAdmin;
    }

    public bool HasModeratorPermissions()
    {
        return IsAdmin || IsModerator || (ConsoleSystem.GetValue("sv_cheats") == "1");
    }

    public string GetDisplayStyle()
    {
        if(HasOwnerPermissions()) return "rainbow";
        if(Client.SteamId == Game.LocalClient.SteamId) return "me";
        if(HasAdminPermissions()) return "admin";
        if(HasModeratorPermissions()) return "moderator";
        return "";
    }

}