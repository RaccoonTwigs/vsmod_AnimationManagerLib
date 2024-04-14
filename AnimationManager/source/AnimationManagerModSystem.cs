using System;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;

namespace AnimationManagerLib;

public class AnimationManagerLibSystem : ModSystem, API.IAnimationManagerSystem
{
    public const string HarmonyID = "animationmanagerlib";
    public const string ChannelName = "animationmanagerlib";

    public bool Register(API.AnimationId id, API.AnimationData animation) => _manager?.Register(id, animation) ?? false;
    public Guid Run(API.AnimationTarget animationTarget, API.AnimationSequence sequence, bool synchronize = true) => _manager?.Run(animationTarget, synchronize, sequence.Requests) ?? Guid.Empty;
    public void Stop(Guid runId) => _manager?.Stop(runId);
    public void SetCameraSetting(string domain, CameraSettingsType setting, float value, float blendingSpeed) => _cameraSettingsManager?.Set(domain, setting, value, blendingSpeed);
    public void ResetCameraSetting(string domain, CameraSettingsType setting, float blendingSpeed) => _cameraSettingsManager?.Set(domain, setting, 1, blendingSpeed);

    public override void Start(ICoreAPI api)
    {
        _api = api;
        api.RegisterCollectibleBehaviorClass("Animatable", typeof(CollectibleBehaviors.Animatable));
        api.RegisterCollectibleBehaviorClass("AnimatableAttachable", typeof(CollectibleBehaviors.AnimatableAttachable));
        api.RegisterCollectibleBehaviorClass("AnimatableProcedural", typeof(CollectibleBehaviors.AnimatableProcedural));
    }
    public override void StartClientSide(ICoreClientAPI api)
    {
        api.Event.ReloadShader += LoadAnimatedItemShaders;
        LoadAnimatedItemShaders();

        Synchronizer synchronizer = new();
        _manager = new AnimationManager(api, synchronizer);
        synchronizer.Init(
            api,
            (packet) => _manager.RunFromPacket(packet.AnimationTarget, packet.RunId, packet.Requests),
            (packet) => _manager.Stop(packet.RunId),
            ChannelName
        );

        _cameraSettingsManager = new(api);

        Patches.AnimatorPatch.Patch(HarmonyID, _manager, api);
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
        Synchronizer synchronizer = new();
        synchronizer.Init(
            api,
            null,
            null,
            ChannelName
        );
    }

    [Obsolete]
    public void Suppress(string code)
    {
    }
    [Obsolete]
    public void Unsuppress(string code)
    {
    }

    public override void Dispose()
    {
        if (_api is ICoreClientAPI clientApi)
        {
            clientApi.Event.ReloadShader -= LoadAnimatedItemShaders;
            Patches.AnimatorPatch.Unpatch(HarmonyID);
            _cameraSettingsManager?.Dispose();
        }
        base.Dispose();
    }

    internal IShaderProgram? AnimatedItemShaderProgram => _shaderProgram;
    internal IShaderProgram? AnimatedItemShaderProgramFirstPerson => _shaderProgramFirstPerson;
    internal AnimationManager? AnimationManager => _manager;

    private ICoreAPI? _api;
    private AnimationManager? _manager;
    private ShaderProgram? _shaderProgram;
    private ShaderProgram? _shaderProgramFirstPerson;
    private CameraSettingsManager? _cameraSettingsManager;

    private bool LoadAnimatedItemShaders()
    {
        if (_api is not ICoreClientAPI clientApi) return false;

        _shaderProgram = clientApi.Shader.NewShaderProgram() as ShaderProgram;
        _shaderProgramFirstPerson = clientApi.Shader.NewShaderProgram() as ShaderProgram;

        if (_shaderProgram == null || _shaderProgramFirstPerson == null) return false;

        _shaderProgram.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandard", AnimatedItemShaderProgram);
        _shaderProgram.Compile();

        _shaderProgramFirstPerson.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandardfirstperson", AnimatedItemShaderProgramFirstPerson);
        _shaderProgramFirstPerson.Compile();

        return true;
    }
    internal void OnBeforeRender(Vintagestory.API.Common.IAnimator animator, Entity entity, float dt, Shape shape)
    {
        _manager?.OnFrameHandler(animator, shape, entity, dt);
    }
}