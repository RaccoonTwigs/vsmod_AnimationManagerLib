using AnimationManagerLib.Patches;
using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.GameContent;

namespace AnimationManagerLib;

public enum CameraSettingsType
{
    FirstPersonHandsPitch,
    FirstPersonHandsYawSpeed,
    IntoxicationEffectIntensity,
    WalkPitchMultiplier,
    WalkBobbingAmplitude,
    WalkBobbingOffset,
    WalkBobbingSprint
}

internal sealed class CameraSettingsManager : IDisposable
{
    private readonly Dictionary<CameraSettingsType, CameraSetting> _settings = new();
    private readonly long _listener;
    private readonly ICoreClientAPI _api;
    private bool _disposed = false;

    public CameraSettingsManager(ICoreClientAPI api)
    {
        _listener = api.World.RegisterGameTickListener(Update, 0);
        _api = api;
    }

    public void Set(string domain, CameraSettingsType setting, float value, float blendingSpeed)
    {
        if (!_settings.ContainsKey(setting))
        {
            _settings.Add(setting, new());
        }

        _settings[setting].Set(domain, value, blendingSpeed);
    }
    private void Update(float dt)
    {
        foreach ((CameraSettingsType setting, CameraSetting value) in _settings)
        {
            SetValue(setting, value.Get(dt));
        }
    }

    private void SetValue(CameraSettingsType setting, float value)
    {
        switch (setting)
        {
            case CameraSettingsType.FirstPersonHandsPitch:
                SetFirstPersonHandsPitch(_api.World.Player, value);
                break;
            case CameraSettingsType.WalkBobbingAmplitude:
                EyeHightController.Amplitude = value;
                break;
            case CameraSettingsType.WalkBobbingOffset:
                EyeHightController.Offset = value;
                break;
            case CameraSettingsType.WalkBobbingSprint:
                EyeHightController.SprintAmplitudeEffect = value;
                break;
        }
    }

    public static void SetFirstPersonHandsPitch(IClientPlayer player, float value)
    {
        if (player.Entity.Properties.Client.Renderer is not EntityPlayerShapeRenderer renderer) return;

        renderer.HeldItemPitchFollowOverride = 0.8f * value;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _api.World.UnregisterGameTickListener(_listener);
    }
}

internal sealed class CameraSetting
{
    private readonly Dictionary<string, CameraSettingValue> _values = new();

    public void Set(string domain, float value, float speed)
    {
        if (!_values.ContainsKey(domain))
        {
            _values[domain] = new(1.0f);
        }

        _values[domain].Set(value, speed);
    }

    public float Get(float dt)
    {
        float result = 1.0f;

        foreach ((_, CameraSettingValue value) in _values)
        {
            result *= value.Get(dt);
        }

        return result;
    }
}

internal sealed class CameraSettingValue
{
    private const float _epsilon = 1e-3f;
    private const float _speedMultiplier = 10.0f;

    private float _value;
    private float _target;
    private float _blendSpeed = 0;
    private bool _updated = true;

    public CameraSettingValue(float value)
    {
        _value = value;
        _target = value;
    }

    public void Set(float target, float speed)
    {
        _target = target;
        _blendSpeed = speed;
        _updated = false;
    }

    public float Get(float dt)
    {
        Update(dt);
        return _value;
    }

    private void Update(float dt)
    {
        if (_updated) return;
        float diff = _target - _value;
        float change = Math.Clamp(diff * dt * _blendSpeed * _speedMultiplier, -Math.Abs(diff), Math.Abs(diff));
        _value += change;
        _updated = Math.Abs(_value - _target) < _epsilon;
    }
}
