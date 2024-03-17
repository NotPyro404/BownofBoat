using System.Collections.Generic;
using System.Linq;
using TOHE.Modules;
using TOHE.Roles.Crewmate;
using UnityEngine;
using TOHE.Roles.AddOns.Common;
using static TOHE.Translator;

namespace TOHE.Roles.Impostor;

internal class Vampire : RoleBase
{
    private class BittenInfo(byte vampierId, float killTimer)
    {
        public byte VampireId = vampierId;
        public float KillTimer = killTimer;
    }

    //===========================SETUP================================\\
    private const int Id = 5000;
    private static readonly HashSet<byte> playerIdList = [];
    public static bool HasEnabled => playerIdList.Count > 0;
    public override bool IsEnable => HasEnabled;
    public override CustomRoles ThisRoleBase => CustomRoles.Impostor;
    //==================================================================\\

    private static OptionItem OptionKillDelay;
    private static OptionItem CanVent;
    private static OptionItem VampiressChance;

    private static float KillDelay;
    private static readonly Dictionary<byte, BittenInfo> BittenPlayers = [];

    public static void SetupCustomOption()
    {
        Options.SetupRoleOptions(Id, TabGroup.ImpostorRoles, CustomRoles.Vampire);
        OptionKillDelay = FloatOptionItem.Create(Id + 10, "VampireKillDelay", new(1f, 60f, 1f), 10f, TabGroup.ImpostorRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Vampire])
            .SetValueFormat(OptionFormat.Seconds);
        CanVent = BooleanOptionItem.Create(Id + 11, "CanVent", true, TabGroup.ImpostorRoles, false).SetParent(Options.CustomRoleSpawnChances[CustomRoles.Vampire]);
        VampiressChance = IntegerOptionItem.Create(Id + 12, "VampiressChance", new(0, 100, 5), 25, TabGroup.ImpostorRoles, false)
            .SetParent(Options.CustomRoleSpawnChances[CustomRoles.Vampire])
            .SetValueFormat(OptionFormat.Percent);
    }
    public override void Init()
    {
        playerIdList.Clear();
        BittenPlayers.Clear();

        KillDelay = OptionKillDelay.GetFloat();
    }
    public override void Add(byte playerId)
    {
        playerIdList.Add(playerId);
    }

    public static bool CheckCanUseVent() => CanVent.GetBool();
    public override bool CanUseImpostorVentButton(PlayerControl pc) => CheckCanUseVent();

    public static bool CheckSpawnVampiress()
    {
        var Rand = IRandom.Instance;
        return Rand.Next(0, 100) < VampiressChance.GetInt();
    }

    public override bool OnCheckMurderAsKiller(PlayerControl killer, PlayerControl target)
    {
        if (target.Is(CustomRoles.Bait)) return true;
        if (Guardian.CannotBeKilled(target)) return true;
        if (target.Is(CustomRoles.Opportunist) && target.AllTasksCompleted() && Options.OppoImmuneToAttacksWhenTasksDone.GetBool()) return false;

        if (killer.Is(CustomRoles.Vampire))
        {
            killer.SetKillCooldown();
            killer.RPCPlayCustomSound("Bite");

            if (!BittenPlayers.ContainsKey(target.PlayerId))
            {
                BittenPlayers.Add(target.PlayerId, new(killer.PlayerId, 0f));
            }
        }
        else // Vampiress
        {
            return killer.CheckDoubleTrigger(target, () =>
            {
                killer.SetKillCooldown();
                killer.RPCPlayCustomSound("Bite");

                if (!BittenPlayers.ContainsKey(target.PlayerId))
                {
                    BittenPlayers.Add(target.PlayerId, new(killer.PlayerId, 0f));
                }
            });
        }
        return false;
    }

    public override void OnFixedUpdate(PlayerControl vampire)
    {
        var vampireID = vampire.PlayerId;
        List<byte> targetList = new(BittenPlayers.Where(b => b.Value.VampireId == vampireID).Select(b => b.Key));

        for (var id = 0; id < targetList.Count; id++)
        {
            var targetId = targetList[id];
            var bitten = BittenPlayers[targetId];

            if (bitten.KillTimer >= KillDelay)
            {
                var target = Utils.GetPlayerById(targetId);
                KillBitten(vampire, target);
                BittenPlayers.Remove(targetId);
            }
            else
            {
                bitten.KillTimer += Time.fixedDeltaTime;
                BittenPlayers[targetId] = bitten;
            }
        }
    }
    public static void KillBitten(PlayerControl vampire, PlayerControl target, bool isButton = false)
    {
        if (vampire == null || target == null || target.Data.Disconnected) return;
        if (target.IsAlive())
        {
            Main.PlayerStates[target.PlayerId].deathReason = PlayerState.DeathReason.Bite;
            target.SetRealKiller(vampire);
            target.RpcMurderPlayerV3(target);

            Logger.Info($"{target.name} self-kill while being bitten by Vampire.", "Vampire");
            if (!isButton && vampire.IsAlive())
            {
                RPC.PlaySoundRPC(vampire.PlayerId, Sounds.KillSound);
                if (target.Is(CustomRoles.Trapper))
                    vampire.TrapperKilled(target);
                vampire.Notify(GetString("VampireTargetDead"));
                vampire.SetKillCooldown();
            }
        }
        else
        {
            Logger.Info($"{target.name} was dead after being bitten by Vampire", "Vampire");
        }
    }

    public override void OnReportDeadBody(PlayerControl reporter, PlayerControl deadBody)
    {
        foreach (var targetId in BittenPlayers.Keys)
        {
            var target = Utils.GetPlayerById(targetId);
            var vampire = Utils.GetPlayerById(BittenPlayers[targetId].VampireId);
            KillBitten(vampire, target);
        }
        BittenPlayers.Clear();
    }
    public override void SetAbilityButtonText(HudManager hud, byte playerId)
    {
        hud.KillButton?.OverrideText(GetString("VampireBiteButtonText"));
    }
}
