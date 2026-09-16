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
using SonsInput = Sons.Input.InputSystem;

namespace FullAuto;

public enum FireMode
{
    Auto,
    Burst,
    Semi
}

public class FullAuto : SonsMod
{
    internal const float MinRpm = 60f;
    internal const float MaxRpm = 3000f;
    internal const float ReloadMultiplier = 2f;

    internal static bool Enabled = true;
    internal static float Rpm = 900f;
    internal static FireMode Mode = FireMode.Auto;
    internal static bool FastReload;
    internal static bool Verbose;

    private static string _configPath;

    public FullAuto()
    {
        HarmonyPatchAll = true;
        OnUpdateCallback = OnUpdate;
    }

    protected override void OnSdkInitialized()
    {
        _configPath = Path.Combine(LoaderEnvironment.UserDataDirectory, "FullAuto.txt");
        Load();
        RLog.Msg($"FullAuto loaded. RPM: {Rpm}. Fast reload: {FastReload}. Guns only. Middle mouse cycles modes. Console: fullauto");
    }

    private void OnUpdate()
    {
        CheckFireInputPatch.UpdateTrigger();

        if (!CheckFireInputPatch.HoldingGun)
            CheckFireInputPatch.RestoreReloadSpeed();

        if (!Enabled || !GameState.IsPlayerControllable || !CheckFireInputPatch.HoldingGun)
            return;

        if (Input.GetMouseButtonDown(2))
        {
            Mode = Mode switch
            {
                FireMode.Auto => FireMode.Semi,
                FireMode.Semi => FireMode.Burst,
                _ => FireMode.Auto
            };
            CheckFireInputPatch.CancelBurst();
            ShowMode();
            RLog.Msg($"FullAuto mode: {ModeName()}");
        }
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
            case "debug":
                Verbose = !Verbose;
                SonsTools.ShowMessage($"FullAuto debug: {Verbose}");
                RLog.Msg($"FullAuto debug: {Verbose}");
                return;
            case "fastreload":
                if (parts.Length > 1 && (parts[1].ToLowerInvariant() == "on" || parts[1].ToLowerInvariant() == "off"))
                {
                    FastReload = parts[1].ToLowerInvariant() == "on";
                    if (!FastReload)
                        CheckFireInputPatch.RestoreReloadSpeed();
                    Save();
                    var text = $"Fast reload {(FastReload ? "ON" : "OFF")}";
                    SonsTools.ShowMessage(text);
                    RLog.Msg(text);
                    return;
                }
                Usage();
                return;
            case "rpm":
                if (parts.Length > 1 && float.TryParse(parts[1], out var value))
                {
                    Rpm = Mathf.Clamp(value, MinRpm, MaxRpm);
                    Save();
                    break;
                }
                Usage();
                return;
            default:
                Usage();
                return;
        }

        CheckFireInputPatch.CancelBurst();
        Announce();
    }

    private static void Usage()
    {
        var text = $"Usage: fullauto [on|off|debug], fullauto rpm <{MinRpm}-{MaxRpm}>, fullauto fastreload on|off";
        SonsTools.ShowMessage(text, 5f);
        RLog.Msg(text);
    }

    private static string ModeName()
    {
        return Mode switch
        {
            FireMode.Auto => "Full-automatic",
            FireMode.Burst => "3-Round Burst",
            _ => "Semi-automatic"
        };
    }

    internal static void ShowMode()
    {
        SonsTools.ShowMessage(ModeName(), 2f);
    }

    internal static void Announce()
    {
        var reload = FastReload ? ", fast reload on" : "";
        var text = Enabled ? $"{ModeName()} ({Rpm} RPM{reload})" : $"FullAuto OFF{reload}";
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

                if (key == "rpm" && float.TryParse(val, out var rpm))
                    Rpm = Mathf.Clamp(rpm, MinRpm, MaxRpm);
                else if (key == "fastreload" && bool.TryParse(val, out var fast))
                    FastReload = fast;
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
            File.WriteAllText(_configPath, $"rpm={Rpm}\nfastreload={FastReload.ToString().ToLowerInvariant()}\n");
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
    private const int BurstSize = 3;
    private const float ReleaseTime = 0.15f;

    private static readonly HashSet<string> Guns = new()
    {
        "CompactPistolWeaponController",
        "RevolverWeaponController",
        "ShotgunWeaponController",
        "RifleAnimatorController"
    };

    private static readonly Dictionary<IntPtr, bool> Allowed = new();
    private static readonly Dictionary<IntPtr, float> FireDelays = new();
    private static readonly HashSet<string> Reported = new();
    private static float _nextShot;
    private static int _burstLeft;
    private static float _lastGunTime = -1f;
    private static IntPtr _lastGunPtr = IntPtr.Zero;
    private static bool _triggerDown;
    private static float _releasedFor;
    private static bool _actionFailed;
    private static bool _holding;
    private static RangedWeaponController _speedGun;
    private static Animator _speedPlayer;
    private static Animator _speedWeapon;
    private static float _playerSpeed = 1f;
    private static float _weaponSpeed = 1f;
    private static bool _loggedHook;

    internal static bool HoldingGun => _lastGunTime >= 0f && Time.time - _lastGunTime < 0.25f;

    internal static void CancelBurst()
    {
        _burstLeft = 0;
        _holding = false;
    }

    private static void UpdateReloadSpeed(RangedWeaponController controller, bool allowed)
    {
        var want = allowed && FullAuto.FastReload && (controller._isReloading || controller.IsReloading());

        if (!want)
        {
            RestoreReloadSpeed();
            return;
        }

        if (_speedGun != null && _speedGun.Pointer == controller.Pointer)
            return;

        RestoreReloadSpeed();

        try
        {
            _speedGun = controller;
            _speedPlayer = controller._playerAnimator;
            _speedWeapon = controller._weaponAnimator;

            if (_speedPlayer)
            {
                _playerSpeed = _speedPlayer.speed;
                _speedPlayer.speed = _playerSpeed * FullAuto.ReloadMultiplier;
            }

            if (_speedWeapon)
            {
                _weaponSpeed = _speedWeapon.speed;
                _speedWeapon.speed = _weaponSpeed * FullAuto.ReloadMultiplier;
            }

            if (FullAuto.Verbose)
                RLog.Msg($"FullAuto fast reload start, player {_playerSpeed} weapon {_weaponSpeed}");
        }
        catch (Exception e)
        {
            RLog.Warning($"FullAuto could not speed up reload: {e.Message}");
        }
    }

    internal static void RestoreReloadSpeed()
    {
        if (_speedGun == null)
            return;

        try
        {
            if (_speedPlayer)
                _speedPlayer.speed = _playerSpeed;
            if (_speedWeapon)
                _speedWeapon.speed = _weaponSpeed;
        }
        catch (Exception e)
        {
            RLog.Warning($"FullAuto could not restore reload speed: {e.Message}");
        }

        _speedGun = null;
        _speedPlayer = null;
        _speedWeapon = null;

        if (FullAuto.Verbose)
            RLog.Msg("FullAuto fast reload end");
    }

    private static void HoldAfterBurst(RangedWeaponController controller)
    {
        var first = !_holding;
        _holding = true;

        try
        {
            controller.ClearTriggeredAttack();
            if (first)
                controller.AttackEndCallback();
            controller._attackState = RangedWeaponController.AttackState.Idle;
        }
        catch (Exception e)
        {
            if (first)
                RLog.Warning($"FullAuto could not end attack: {e.Message}");
        }

        if (first && FullAuto.Verbose)
        {
            RLog.Msg("FullAuto burst done, holding");
            DumpAnimator("player", controller._playerAnimator);
            DumpAnimator("weapon", controller._weaponAnimator);
        }
    }

    private static void DumpAnimator(string label, Animator animator)
    {
        if (!animator)
        {
            RLog.Msg($"FullAuto {label} animator: none");
            return;
        }

        try
        {
            var active = new List<string>();
            foreach (var p in animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Bool || p.type == AnimatorControllerParameterType.Trigger)
                {
                    if (animator.GetBool(p.name))
                        active.Add($"{p.name}({p.type})");
                }
            }
            RLog.Msg($"FullAuto {label} animator active: {string.Join(", ", active)}");
        }
        catch (Exception e)
        {
            RLog.Warning($"FullAuto could not read {label} animator: {e.Message}");
        }
    }

    private static bool FirePressed()
    {
        if (Input.GetMouseButton(0))
            return true;

        if (_actionFailed)
            return false;

        try
        {
            return SonsInput.InputMapping.@default.PrimaryAction.IsPressed();
        }
        catch (Exception e)
        {
            _actionFailed = true;
            RLog.Warning($"FullAuto could not read PrimaryAction: {e.Message}");
            return false;
        }
    }

    internal static void UpdateTrigger()
    {
        if (FirePressed())
        {
            _releasedFor = 0f;
            if (_triggerDown)
                return;

            _triggerDown = true;
            if (FullAuto.Enabled && FullAuto.Mode == FireMode.Burst && HoldingGun && GameState.IsPlayerControllable)
                StartBurst();
            return;
        }

        _releasedFor += Time.unscaledDeltaTime;
        if (_triggerDown && _releasedFor >= ReleaseTime)
        {
            _triggerDown = false;
            _holding = false;
            if (FullAuto.Verbose)
                RLog.Msg("FullAuto trigger released");
        }
    }

    private static void StartBurst()
    {
        if (_burstLeft > 0)
            return;

        _burstLeft = BurstSize;
        _nextShot = 0f;

        if (FullAuto.Verbose)
            RLog.Msg("FullAuto burst start");
    }

    private static bool Prefix(RangedWeaponController __instance)
    {
        if (!_loggedHook)
        {
            _loggedHook = true;
            RLog.Msg("FullAuto: CheckFireInput hook is firing");
        }

        if (!__instance.IsLocalPlayer())
            return true;

        var allowed = IsAllowed(__instance);
        var ptr = __instance.Pointer;

        UpdateReloadSpeed(__instance, allowed);

        if (allowed && FullAuto.Enabled && (!HoldingGun || ptr != _lastGunPtr))
            FullAuto.ShowMode();

        if (allowed)
            _lastGunPtr = ptr;
        _lastGunTime = allowed ? Time.time : -1f;

        if (!FullAuto.Enabled || !allowed)
            return true;

        if (FullAuto.Mode == FireMode.Semi)
            return true;

        var pressed = FirePressed();
        var held = _triggerDown || pressed;

        if (!GameState.IsPlayerControllable)
        {
            _burstLeft = 0;
            return !held;
        }

        if (__instance._isReloading || __instance.IsReloading())
            return Stop();

        var weapon = __instance.GetRangedWeapon();
        if (!weapon)
            return Stop();

        var ammo = weapon.GetAmmo();
        if (ammo == null || ammo.GetRemainingAmmo() <= 0)
            return Stop();

        if (FullAuto.Mode == FireMode.Burst && _burstLeft <= 0)
        {
            if (!held)
                return true;
            HoldAfterBurst(__instance);
            return false;
        }

        if (FullAuto.Mode == FireMode.Auto && !pressed)
            return true;

        if (__instance._mustAimToFire && !__instance.IsAiming)
            return Stop();

        var now = Time.time;
        var interval = 60f / FullAuto.Rpm;

        if (_nextShot < now - interval)
            _nextShot = now;

        var shots = 0;
        while (_nextShot <= now && shots < MaxShotsPerFrame && ammo.GetRemainingAmmo() > 0)
        {
            if (FullAuto.Mode == FireMode.Burst)
            {
                if (_burstLeft <= 0)
                    break;
                _burstLeft--;
            }

            Fire(__instance, ammo);
            _nextShot += interval;
            shots++;
        }

        if (FireDelays.TryGetValue(__instance.Pointer, out var delay))
            __instance._fireDelay = delay;

        if (FullAuto.Mode == FireMode.Burst && _burstLeft <= 0 && held)
            HoldAfterBurst(__instance);

        return false;
    }

    private static bool Stop()
    {
        CancelBurst();
        return true;
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
            RLog.Msg($"FullAuto shot {name} ({FullAuto.Mode}): ammo {before} -> {after}");
    }

    private static bool IsAllowed(RangedWeaponController controller)
    {
        var ptr = controller.Pointer;
        if (Allowed.TryGetValue(ptr, out var allowed))
            return allowed;

        var name = controller.GetIl2CppType().Name;
        allowed = Guns.Contains(name);
        Allowed[ptr] = allowed;
        if (allowed)
            FireDelays[ptr] = controller._fireDelay;
        RLog.Msg($"FullAuto {name}: {(allowed ? "full-auto" : "ignored")}");
        return allowed;
    }
}

