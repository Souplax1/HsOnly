using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace Hsonly;

public class HeadshotOnlyConfig : BasePluginConfig
{
    [JsonPropertyName("PluginEnabled")] public bool PluginEnabled { get; set; } = true;
    [JsonPropertyName("AlwaysEnableHsOnly")] public bool AlwaysEnableHsOnly { get; set; } = false;
    [JsonPropertyName("scaleEnabled")] public bool ScaleEnabled { get; set; } = true;
    [JsonPropertyName("AdminFlagtoForceHsOnly")] public string AdminFlagtoForceHsOnly { get; set; } = "@css/root";
    [JsonPropertyName("EnableReward")] public bool EnableReward { get; set; } = false;
    [JsonPropertyName("RequiredKills")] public int RequiredKills { get; set; } = 35;
    [JsonPropertyName("executeCommand")] public string ExecuteCommand { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "You won.";
    [JsonPropertyName("MaxScale")] public float MaxScale { get; set; } = 1.5f;
    [JsonPropertyName("MinScale")] public float MinScale { get; set; } = 0.5f;
}

public class HeadshotOnly : BasePlugin, IPluginConfig<HeadshotOnlyConfig>
{
    public override string ModuleName => "HS only";
    public override string ModuleVersion => "1.1.0";
    public override string ModuleAuthor => "Yeezy";
    public override string ModuleDescription => "Enable/Disable Headshot Only with size scaling based on kills/deaths";

    public required HeadshotOnlyConfig Config { get; set; }

    private readonly Dictionary<int, bool> _playerHsEnabled = new();
    private readonly Dictionary<int, float> _playerScales = new();
    private bool _adminHeadshotOnly;

    public void OnConfigParsed(HeadshotOnlyConfig config) => Config = config;

    public override void Load(bool hotReload)
    {
        AddCommand("css_hs", "Toggle Headshot only", OnAdminHsCommand);

        RegisterEventHandler<EventPlayerHurt>(OnPlayerHurt);
        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerDisconnect>(OnPlayerDisconnect);
        RegisterEventHandler<EventRoundStart>(OnRoundStart);
    }

    private HookResult OnPlayerHurt(EventPlayerHurt @event, GameEventInfo info)
    {
        if (!Config.PluginEnabled || Config.AlwaysEnableHsOnly) return HookResult.Continue;

        var victim = @event.Userid;
        var attacker = @event.Attacker;

        if (!ValidatePlayers(victim, attacker)) return HookResult.Continue;
        if (victim.Team == attacker.Team) return HookResult.Continue;

        var hsRequired = _adminHeadshotOnly || _playerHsEnabled.GetValueOrDefault(attacker.Slot, false);
        if (!hsRequired) return HookResult.Continue;

        RestoreDamage(victim,@event.Hitgroup, @event.DmgHealth, @event.DmgArmor);
        return HookResult.Continue;
    }

    private void RestoreDamage(CCSPlayerController player,int hitgroup, int healthDamage, int armorDamage)
    {
        if (!player.PawnIsAlive || player.PlayerPawn.Value == null) return;

        if (hitgroup != 1)
        {
            player.PlayerPawn.Value.Health += healthDamage;
            player.PlayerPawn.Value.ArmorValue += armorDamage;
        }
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        var attacker = @event.Attacker;
        var victim = @event.Userid;

        if (Config.ScaleEnabled && attacker != null && attacker.IsValid && attacker != victim)
        {
            UpdatePlayerScale(attacker, Config.MaxScale, 0.03f);
        }

        if (Config.ScaleEnabled && victim != null && victim.IsValid)
        {
            UpdatePlayerScale(victim, Config.MinScale, -0.03f);
        }

        if (Config.EnableReward && attacker != null && attacker.IsValid)
        {
            CheckForReward(attacker);
        }

        return HookResult.Continue;
    }

    private void UpdatePlayerScale(CCSPlayerController player, float limit, float delta)
    {
        var currentScale = _playerScales.GetValueOrDefault(player.Slot, 1.0f);
        Server.PrintToChatAll($"[{ChatColors.Gold}HS Only{ChatColors.Default}] {player.PlayerName} {ChatColors.Green}Scale: {currentScale}");
        var newScale = System.Math.Clamp(currentScale + delta, Config.MinScale, Config.MaxScale);

        _playerScales[player.Slot] = newScale;
        SetPlayerScale(player, newScale);
    }

    private void CheckForReward(CCSPlayerController player)
    {
        var kills = player.ActionTrackingServices?.MatchStats?.Kills ?? 0;
        if (kills >= Config.RequiredKills)
        {
            Server.PrintToChatAll($"[{ChatColors.Gold}HS Only{ChatColors.Default}] {player.PlayerName} {ChatColors.Green}{Config.Message}");
            if (!string.IsNullOrEmpty(Config.ExecuteCommand))
            {
                Server.ExecuteCommand(Config.ExecuteCommand.Replace("{STEAMID}", player.SteamID.ToString()));
            }
        }
    }

    private void OnAdminHsCommand(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (!Config.PluginEnabled || Config.AlwaysEnableHsOnly || player == null || !player.IsValid) return;

        if (!AdminManager.PlayerHasPermissions(player, Config.AdminFlagtoForceHsOnly))
        {
            player.PrintToChat($" {ChatColors.DarkRed}Missing permissions!");
            return;
        }

        _adminHeadshotOnly = !_adminHeadshotOnly;
        var status = _adminHeadshotOnly ? $"{ChatColors.Green}Enabled" : $"{ChatColors.Red}Disabled";
        Server.PrintToChatAll($"[{ChatColors.Gold}HS Only{ChatColors.Default}] {status}");
    }

    private void SetPlayerScale(CCSPlayerController player, float scale)
    {
        if (!player.PawnIsAlive || player.PlayerPawn.Value == null) return;

        var pawn = player.PlayerPawn.Value;
        var skeleton = pawn.CBodyComponent?.SceneNode?.GetSkeletonInstance();
        if (skeleton != null) skeleton.Scale = scale;

        pawn.AcceptInput("SetScale", null, null, scale.ToString());

        Server.NextFrame(() =>
        {
            if (pawn.IsValid)
            {
                Utilities.SetStateChanged(pawn, "CBaseEntity", "m_CBodyComponent");
            }
        });
    }

    private HookResult OnRoundStart(EventRoundStart @event, GameEventInfo info)
    {
        foreach (var player in Utilities.GetPlayers())
        {
            if (player?.IsValid == true && _playerScales.TryGetValue(player.Slot, out var scale))
            {
                SetPlayerScale(player, scale); // Reapply the stored scale
            }
        }
        return HookResult.Continue;
    }


    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player?.IsValid == true)
        {
            _playerScales[player.Slot] = 1.0f;
            _playerHsEnabled[player.Slot] = false;
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerDisconnect(EventPlayerDisconnect @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player?.IsValid == true)
        {
            _playerScales.Remove(player.Slot);
            _playerHsEnabled.Remove(player.Slot);
        }
        return HookResult.Continue;
    }


    private static bool ValidatePlayers(params CCSPlayerController[] players)
    {
        foreach (var player in players)
        {
            if (player == null || !player.IsValid || !player.PawnIsAlive || player.PlayerPawn.Value == null)
                return false;
        }
        return true;
    }
}