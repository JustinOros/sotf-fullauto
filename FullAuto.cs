using System.Collections.Generic;
using System.IO;
using HarmonyLib;
using RedLoader;
using RedLoader.Utils;
using Sons.Weapon;
using SonsSdk;
using SonsSdk.Attributes;
using TheForest.Utils;
using UnityEngine;

namespace FullAuto;

public class FullAuto : SonsMod
{
    internal const float MinRpm = 60f;
    internal const float MaxRpm = 3000f;

    internal static bool Enabled = true;
    internal static float Rpm = 900f;
    internal static bool Verbose;

    private static string _configPath;

    public FullAuto()
    {
        HarmonyPatchAll = true;
    }

    protected override void OnSdkInitialized()
    {
        _configPath = Path.Combine(LoaderEnvironment.UserDataDirectory, "FullAuto.txt");
        Load();
        RLog.Msg($"FullAuto loaded. Enabled: {Enabled}, RPM: {Rpm}. Guns only. Console: fullauto");
    }

    [DebugCommand("fullauto")]
    private static void FullAutoCommand(string args)
    {
        var parts = (args ?? string.Empty).Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);

        if (parts.Length == 0)
        {
            Announce();
            return;
        }

        switch (parts[0].ToLowerInvariant())
        {
            case "on":
                Enabled = true;
                break;
            case "off":
                Enabled = false;
                break;
            case "toggle":
                Enabled = !Enabled;
                break;
            case "debug":
                Verbose = !Verbose;
                SonsTools.ShowMessage($"FullAuto debug: {Verbose}");
                RLog.Msg($"FullAuto debug: {Verbose}");
                return;
            case "rpm":
                if (parts.Length > 1 && float.TryParse(parts[1], out var value))
                {
                    Rpm = Mathf.Clamp(value, MinRpm, MaxRpm);
                    break;
                }
                Usage();
                return;
            default:
                Usage();
                return;
        }

        Save();
        Announce();
    }

    private static void Usage()
    {
        var text = $"Usage: fullauto [on|off|toggle|debug] or fullauto rpm <{MinRpm}-{MaxRpm}>";
        SonsTools.ShowMessage(text, 5f);
        RLog.Msg(text);
    }

    internal static void Announce()
    {
        var text = $"FullAuto {(Enabled ? "ON" : "OFF")} ({Rpm} RPM)";
        SonsTools.ShowMessage(text);
        RLog.Msg(text);
    }

    private static void Load()
    {
        try
        {
            if (!File.Exists(_configPath))
                return;

            foreach (var line in File.ReadAllLines(_configPath))
            {
                var kv = line.Split('=', 2);
                if (kv.Length != 2)
                    continue;

                var key = kv[0].Trim().ToLowerInvariant();
                var val = kv[1].Trim();

                if (key == "enabled" && bool.TryParse(val, out var enabled))
                    Enabled = enabled;
                else if (key == "rpm" && float.TryParse(val, out var rpm))
                    Rpm = Mathf.Clamp(rpm, MinRpm, MaxRpm);
            }
        }
        catch (Exception e)
        {
            RLog.Error($"FullAuto could not read config: {e.Message}");
        }
    }

    private static void Save()
    {
        try
        {
            File.WriteAllText(_configPath, $"enabled={Enabled}\nrpm={Rpm}\n");
        }
        catch (Exception e)
        {
            RLog.Error($"FullAuto could not write config: {e.Message}");
        }
    }
}

[HarmonyPatch(typeof(RangedWeaponController), nameof(RangedWeaponController.CheckFireInput))]
internal static class CheckFireInputPatch
{
    private const int MaxShotsPerFrame = 4;

    private static readonly HashSet<string> Guns = new()
    {
        "CompactPistolWeaponController",
        "RevolverWeaponController",
        "ShotgunWeaponController",
        "RifleAnimatorController"
    };

    private static readonly Dictionary<IntPtr, bool> Allowed = new();
    private static readonly HashSet<string> Reported = new();
    private static float _nextShot;
    private static bool _loggedHook;

    private static bool Prefix(RangedWeaponController __instance)
    {
        if (!_loggedHook)
        {
            _loggedHook = true;
            RLog.Msg("FullAuto: CheckFireInput hook is firing");
        }

        if (!FullAuto.Enabled || !__instance.IsLocalPlayer() || !IsAllowed(__instance))
            return true;

        if (!Input.GetMouseButton(0) || !GameState.IsPlayerControllable)
            return true;

        if (__instance._isReloading || __instance.IsReloading())
            return true;

        if (__instance._mustAimToFire && !__instance.IsAiming)
            return true;

        var weapon = __instance.GetRangedWeapon();
        if (!weapon)
            return true;

        var ammo = weapon.GetAmmo();
        if (ammo == null || ammo.GetRemainingAmmo() <= 0)
            return true;

        var now = Time.time;
        var interval = 60f / FullAuto.Rpm;

        if (_nextShot < now - interval)
            _nextShot = now;

        var shots = 0;
        while (_nextShot <= now && shots < MaxShotsPerFrame && ammo.GetRemainingAmmo() > 0)
        {
            Fire(__instance, ammo);
            _nextShot += interval;
            shots++;
        }

        return false;
    }

    private static void Fire(RangedWeaponController controller, RangedWeapon.Ammo ammo)
    {
        var before = ammo.GetRemainingAmmo();

        controller._fireDelay = 0f;
        controller._nextFireDelay = 0f;
        controller.FireWeapon();

        if (controller._playFireAudio == RangedWeaponController.FireAudio.OnAnimShootCallback)
        {
            controller.TriggerShotFiredAudio();
            if (controller.ShouldShowMuzzleFlash())
                controller.OnMuzzleFlash();
        }

        controller._attackState = RangedWeaponController.AttackState.Idle;

        var after = ammo.GetRemainingAmmo();
        var name = controller.GetIl2CppType().Name;

        if (Reported.Add(name))
            RLog.Msg($"FullAuto first shot with {name}: ammo {before} -> {after}, audio {controller._playFireAudio}");

        if (FullAuto.Verbose)
            RLog.Msg($"FullAuto shot {name}: ammo {before} -> {after}");
    }

    private static bool IsAllowed(RangedWeaponController controller)
    {
        var ptr = controller.Pointer;
        if (Allowed.TryGetValue(ptr, out var allowed))
            return allowed;

        var name = controller.GetIl2CppType().Name;
        allowed = Guns.Contains(name);
        Allowed[ptr] = allowed;
        RLog.Msg($"FullAuto {name}: {(allowed ? "full-auto" : "ignored")}");
        return allowed;
    }
}


