using AnimationManagerLib.Integration;
using HarmonyLib;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using Vintagestory.GameContent;

namespace AnimationManagerLib;

public class AnimationManagerLibSystem : ModSystem, API.IAnimationManagerSystem
{
    public const string HarmonyID = "animationmanagerlib";
    public const string ChannelName = "animationmanagerlib";

    internal delegate void OnBeforeRenderCallback(Vintagestory.API.Common.IAnimator animator, Entity entity, float dt);
    internal IShaderProgram? AnimatedItemShaderProgram => mShaderProgram;
    internal IShaderProgram? AnimatedItemShaderProgramFirstPerson => mShaderProgramFirstPerson;
    internal AnimationManager? AnimationManager => mManager;


    internal event OnBeforeRenderCallback? OnHeldItemBeforeRender;

    private ICoreAPI? mApi;
    private AnimationManager? mManager;
    private ShaderProgram? mShaderProgram;
    private ShaderProgram? mShaderProgramFirstPerson;
    private readonly Dictionary<string, int> mSuppressedAnimations = new();
    private CameraSettingsManager? mCameraSettingsManager;

    public bool Register(API.AnimationId id, API.AnimationData animation) => mManager?.Register(id, animation) ?? false;
    public Guid Run(API.AnimationTarget animationTarget, API.AnimationSequence sequence, bool synchronize = true) => mManager?.Run(animationTarget, synchronize, sequence.Requests) ?? Guid.Empty;
    public void Stop(Guid runId) => mManager?.Stop(runId);
    public void SetCameraSetting(string domain, CameraSettingsType setting, float value, float blendingSpeed) => mCameraSettingsManager?.Set(domain, setting, value, blendingSpeed);
    public void ResetCameraSetting(string domain, CameraSettingsType setting, float blendingSpeed) => mCameraSettingsManager?.Set(domain, setting, 1, blendingSpeed);

    public override void Start(ICoreAPI api)
    {
        mApi = api;
        api.RegisterCollectibleBehaviorClass("Animatable", typeof(CollectibleBehaviors.Animatable));
        api.RegisterCollectibleBehaviorClass("AnimatableAttachable", typeof(CollectibleBehaviors.AnimatableAttachable));
        api.RegisterCollectibleBehaviorClass("AnimatableProcedural", typeof(CollectibleBehaviors.AnimatableProcedural));
    }
    public override void StartClientSide(ICoreClientAPI api)
    {
        Patches.AnimatorPatch.Patch(HarmonyID);

        api.Event.ReloadShader += LoadAnimatedItemShaders;
        LoadAnimatedItemShaders();

        Synchronizer synchronizer = new();
        mManager = new AnimationManager(api, synchronizer);
        RegisterHandlers(mManager);
        synchronizer.Init(
            api,
            (packet) => mManager.Run(packet.AnimationTarget, packet.RunId, packet.Requests),
            (packet) => mManager.Stop(packet.RunId),
            ChannelName
        );

        mCameraSettingsManager = new(api);

        PatchManagersReplacer();
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
    public bool LoadAnimatedItemShaders()
    {
        if (mApi is not ICoreClientAPI clientApi) return false;

        mShaderProgram = clientApi.Shader.NewShaderProgram() as ShaderProgram;
        mShaderProgramFirstPerson = clientApi.Shader.NewShaderProgram() as ShaderProgram;

        if (mShaderProgram == null || mShaderProgramFirstPerson == null) return false;

        mShaderProgram.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandard", AnimatedItemShaderProgram);
        mShaderProgram.Compile();

        mShaderProgramFirstPerson.AssetDomain = Mod.Info.ModID;
        clientApi.Shader.RegisterFileShaderProgram("customstandardfirstperson", AnimatedItemShaderProgramFirstPerson);
        mShaderProgramFirstPerson.Compile();

        return true;
    }
    public void OnBeforeRender(Vintagestory.API.Common.IAnimator animator, Entity entity, float dt)
    {
        OnHeldItemBeforeRender?.Invoke(animator, entity, dt);
    }
    public void Suppress(string code)
    {
        if (!mSuppressedAnimations.ContainsKey(code)) mSuppressedAnimations.Add(code, 0);

        mSuppressedAnimations[code] += 1;

        if (mSuppressedAnimations[code] > 0 && !Patches.AnimatorPatch.SuppressedAnimations.Contains(code)) Patches.AnimatorPatch.SuppressedAnimations.Add(code);
    }
    public void Unsuppress(string code)
    {
        if (!mSuppressedAnimations.ContainsKey(code)) mSuppressedAnimations.Add(code, 0);

        mSuppressedAnimations[code] = Math.Max(mSuppressedAnimations[code]--, 0);

        if (mSuppressedAnimations[code] == 0 && Patches.AnimatorPatch.SuppressedAnimations.Contains(code)) Patches.AnimatorPatch.SuppressedAnimations.Remove(code);
    }

    private void RegisterHandlers(AnimationManager manager)
    {
        Patches.AnimatorPatch.OnElementPoseUsedCallback += manager.OnApplyAnimation;
        Patches.AnimatorPatch.OnCalculateWeightCallback += manager.OnCalculateWeight;
        Patches.AnimatorPatch.OnFrameCallback += manager.OnFrameHandler;
        OnHeldItemBeforeRender += manager.OnFrameHandler;
    }
    private void UnregisterHandlers(AnimationManager? manager)
    {
        if (manager == null) return;
        Patches.AnimatorPatch.OnElementPoseUsedCallback -= manager.OnApplyAnimation;
        Patches.AnimatorPatch.OnCalculateWeightCallback -= manager.OnCalculateWeight;
        Patches.AnimatorPatch.OnFrameCallback -= manager.OnFrameHandler;
        OnHeldItemBeforeRender -= manager.OnFrameHandler;
    }

    public override void Dispose()
    {
        if (mApi is ICoreClientAPI clientApi)
        {
            clientApi.Event.ReloadShader -= LoadAnimatedItemShaders;
            UnregisterHandlers(mManager);
            Patches.AnimatorPatch.Unpatch(HarmonyID);
            Patches.AnimatorPatch.SuppressedAnimations.Clear();
            UnpatchManagersReplacer();
        }
        base.Dispose();
    }

    private void PatchManagersReplacer()
    {
        new Harmony(HarmonyID).Patch(
                typeof(EntityPlayer).GetMethod("Initialize", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimationManagerLibSystem), nameof(ReplaceAnimationManagers)))
            );

        _manager = mManager;
        _coreClientAPI = mApi as ICoreClientAPI;
    }
    private void UnpatchManagersReplacer()
    {
        new Harmony(HarmonyID).Unpatch(typeof(EntityPlayer).GetMethod("Initialize", AccessTools.all), HarmonyPatchType.Prefix, HarmonyID);
    }

    private static readonly FieldInfo? _animManager = typeof(Vintagestory.API.Common.EntityPlayer).GetField("animManager", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _selfFpAnimManager = typeof(Vintagestory.API.Common.EntityPlayer).GetField("selfFpAnimManager", BindingFlags.NonPublic | BindingFlags.Instance);

    private static AnimationManager _manager;
    private static ICoreClientAPI _coreClientAPI;
    private static void ReplaceAnimationManagers(EntityPlayer __instance)
    {
        Vintagestory.API.Common.AnimationManager animManager = (Vintagestory.API.Common.AnimationManager)_animManager.GetValue(__instance);
        ProceduralAnimationManager animManagerReplacer = new(_manager, _coreClientAPI, animManager);
        animManagerReplacer.UseFpAnmations = false;
        _animManager.SetValue(__instance, animManagerReplacer);

        Vintagestory.API.Common.AnimationManager selfFpAnimManager = (Vintagestory.API.Common.AnimationManager)_selfFpAnimManager.GetValue(__instance);
        ProceduralAnimationManager selfFpAnimManagerReplacer = new(_manager, _coreClientAPI, selfFpAnimManager);
        _selfFpAnimManager.SetValue(__instance, selfFpAnimManagerReplacer);
    }
}