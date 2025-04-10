using CounterStrikeSharp.API;
using CounterStrikeSharp.API.Core;
using CounterStrikeSharp.API.Modules.Utils;
using CounterStrikeSharp.API.Modules.Admin;
using CounterStrikeSharp.API.Modules.Commands;
using System.Text.Json.Serialization;
namespace Hsonly;

public class HeadshotOnlyConfig : BasePluginConfig
{
    [JsonPropertyName("PluginEnabled")] public bool PluginEnabled { get; set; } = true;
    [JsonPropertyName("AlwaysEnableHsOnly")] public bool AlwaysEnableHsOnly { get; set; } = false;
    [JsonPropertyName("scaleEnabled")] public bool ScaleEnabled { get; set; } = true;
    [JsonPropertyName("AdminFlagtoForceHsOnly")] public string AdminFlagtoForceHsOnly { get; set; } = "@css/root";
    [JsonPropertyName("RequiredKills")] public int RequiredKills { get; set; } = 35;
    [JsonPropertyName("executeCommand")] public string ExecuteCommand { get; set; } = "";
    [JsonPropertyName("message")] public string Message { get; set; } = "You have won VIP!";
}

public class HeadshotOnly : BasePlugin, IPluginConfig<HeadshotOnlyConfig>
{
    public override string ModuleName => "HS only";
    public override string ModuleVersion => "1.0.0";
    public override string ModuleAuthor => "Yeezy";
    public override string ModuleDescription => "Enable/Disable Headshot Only with size scaling based on kills/deaths";

    public required HeadshotOnlyConfig Config { get; set; }

    public bool[] g_Headshot = new bool[64];
    public float[] g_PlayerScale = new float[64];
    public bool adminHeadshotOnly = false;

    public void OnConfigParsed(HeadshotOnlyConfig config)
    {
        Config = config;
    }

    public override void Load(bool hotReload)
    {
        // Initialize player scales
        for (int i = 0; i < g_PlayerScale.Length; i++)
        {
            g_PlayerScale[i] = 1.0f;
        }

        AddCommand("css_hs", "Toggle Headshot only", cmd_AdminHsOnly);

        RegisterEventHandler<EventPlayerHurt>((@event, info) =>
        {
            var player = @event.Userid;
            var attacker = @event.Attacker;

            if (!Config.PluginEnabled || player == null || attacker == null || !player.IsValid || !attacker.IsValid)
                return HookResult.Continue;

            if (player.TeamNum == attacker.TeamNum && !(@event.DmgHealth > 0 || @event.DmgArmor > 0))
                return HookResult.Continue;

            if (g_Headshot[attacker.Slot] || adminHeadshotOnly || Config.AlwaysEnableHsOnly)
            {
                if (@event.Hitgroup != 1)
                {
                    player.PlayerPawn.Value.Health += @event.DmgHealth;
                    player.PlayerPawn.Value.ArmorValue += @event.DmgArmor;
                }
            }
            return HookResult.Continue;
        });

        RegisterEventHandler<EventPlayerDeath>(OnPlayerDeath);
        RegisterEventHandler<EventPlayerConnectFull>(OnPlayerConnectFull);
        RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);
    }

    private HookResult OnPlayerConnectFull(EventPlayerConnectFull @event, GameEventInfo info)
    {
        if(Config.ScaleEnabled == false) return HookResult.Continue;
        var player = @event.Userid;
        if (player != null && player.IsValid)
        {
            int slot = player.Slot;
            if (slot >= 0 && slot < g_PlayerScale.Length)
            {
                g_PlayerScale[slot] = 1.0f;
                SetPlayerScale(player, 1.0f);
            }
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
    {
        var player = @event.Userid;
        if (player != null && player.IsValid && player.PlayerPawn.Value != null)
        {
            int slot = player.Slot;
            if (slot >= 0 && slot < g_PlayerScale.Length)
            {
                SetPlayerScale(player, g_PlayerScale[slot]);
             
            }
        }
        return HookResult.Continue;
    }

    private HookResult OnPlayerDeath(EventPlayerDeath @event, GameEventInfo info)
    {
        if(!adminHeadshotOnly || Config.ScaleEnabled == false) return HookResult.Continue;
        var attacker = @event.Attacker;
        var victim = @event.Userid;

        // Handle size scaling
        if (attacker != null && attacker.IsValid && attacker != victim)
        {
            g_PlayerScale[attacker.Slot] = Math.Min(g_PlayerScale[attacker.Slot] + 0.03f, 1.5f);
            SetPlayerScale(attacker, g_PlayerScale[attacker.Slot]);
        }

        if (victim != null && victim.IsValid)
        {
            g_PlayerScale[victim.Slot] = Math.Max(g_PlayerScale[victim.Slot] - 0.03f, 0.5f);   
        }

        // VIP reward logic
        if (adminHeadshotOnly && attacker != null && attacker.IsValid && attacker != victim)
        {
            var actionTracking = attacker.ActionTrackingServices;
            if (actionTracking?.MatchStats == null) return HookResult.Continue;

            if (actionTracking.MatchStats.Kills >= Config.RequiredKills)
            {
                adminHeadshotOnly = false;
                Server.PrintToChatAll($"[{ChatColors.Gold}HS Only{ChatColors.Default}] {attacker.PlayerName} {ChatColors.Green} {Config.Message}");
                if(!string.IsNullOrEmpty(Config.ExecuteCommand))
                {
                    Server.ExecuteCommand(Config.ExecuteCommand);
                }
            }
        }

        return HookResult.Continue;
    }

    private void cmd_AdminHsOnly(CCSPlayerController? player, CommandInfo commandInfo)
    {
        if (!Config.PluginEnabled || Config.AlwaysEnableHsOnly || player == null || !player.IsValid) return;

        if (!AdminManager.PlayerHasPermissions(player, Config.AdminFlagtoForceHsOnly))
        {
            player.PrintToChat($" {ChatColors.DarkRed}Missing permissions!");
            return;
        }

        adminHeadshotOnly = !adminHeadshotOnly;
        Server.PrintToChatAll($"[{ChatColors.Gold}HS Only{ChatColors.Default}] {(adminHeadshotOnly ? ChatColors.Green + "Enabled" : ChatColors.Red + "Disabled")}");
    }

    private void SetPlayerScale(CCSPlayerController player, float scale)
    {
        if (!player.IsValid || player.PlayerPawn.Value == null || Config.ScaleEnabled == false) return;

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
}