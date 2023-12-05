﻿using System.Diagnostics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.Client.NoObf;
using VSImGui;

namespace AnimationManagerLib
{
    public class AnimationManagerLibSystem : ModSystem, API.IAnimationManagerProvider
    {
        public const string HarmonyID = "animationmanagerlib";
        public const string ChannelName = "animationmanagerlib";

        internal delegate void OnBeforeRenderCallback(IAnimator animator, float dt);
        internal IShaderProgram? AnimatedItemShaderProgram => mShaderProgram;
        internal event OnBeforeRenderCallback? OnHeldItemBeforeRender;

        private ICoreAPI? mApi;
        private PlayerModelAnimationManager? mManager;
        private Synchronizer? mSynchronizer;
        private ShaderProgram? mShaderProgram;

        public API.IAnimationManager GetAnimationManager() => mManager ?? throw new System.NullReferenceException();
        public API.ISynchronizer GetSynchronizer() => mSynchronizer ?? throw new System.NullReferenceException();

        public override void Start(ICoreAPI api)
        {
            mApi = api;
            api.RegisterCollectibleBehaviorClass("Animatable", typeof(CollectibleBehaviors.Animatable));
            api.RegisterCollectibleBehaviorClass("AnimatableAttachable", typeof(CollectibleBehaviors.AnimatableAttachable));
            api.RegisterCollectibleBehaviorClass("AnimatableProcedural", typeof(CollectibleBehaviors.AnimatableProcedural));
        }
        public override void StartClientSide(ICoreClientAPI api)
        {
            Patches.AnimatorBasePatch.Patch(HarmonyID);

            api.Event.ReloadShader += LoadAnimatedItemShaders;
            LoadAnimatedItemShaders();

            mSynchronizer = new Synchronizer();
            mManager = new PlayerModelAnimationManager(api, mSynchronizer);
            RegisterHandlers(mManager);
            mSynchronizer.Init(
                api,
                (packet) => mManager.Run(packet.AnimationTarget, packet.RunId, packet.Requests),
                (packet) => mManager.Stop(packet.RunId),
                ChannelName
            );
        }
        public override void StartServerSide(ICoreServerAPI api)
        {
            mSynchronizer = new Synchronizer();
            mSynchronizer.Init(
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
            
            if (mShaderProgram == null) return false;
            
            mShaderProgram.AssetDomain = Mod.Info.ModID;
            clientApi.Shader.RegisterFileShaderProgram("helditemanimated", AnimatedItemShaderProgram);
            mShaderProgram.Compile();

            return true;
        }
        public void OnBeforeRender(IAnimator animator, float dt)
        {
            OnHeldItemBeforeRender?.Invoke(animator, dt);
        }

        private void RegisterHandlers(PlayerModelAnimationManager manager)
        {
            Patches.AnimatorBasePatch.OnElementPoseUsedCallback += manager.OnApplyAnimation;
            Patches.AnimatorBasePatch.OnFrameCallback += manager.OnFrameHandler;
            OnHeldItemBeforeRender += manager.OnFrameHandler;
        }
        private void UnregisterHandlers(PlayerModelAnimationManager? manager)
        {
            if (manager == null) return;
            Patches.AnimatorBasePatch.OnElementPoseUsedCallback -= manager.OnApplyAnimation;
            Patches.AnimatorBasePatch.OnFrameCallback -= manager.OnFrameHandler;
            OnHeldItemBeforeRender -= manager.OnFrameHandler;
        }
        public override void Dispose()
        {
            if (mApi is ICoreClientAPI clientApi)
            {
                clientApi.Event.ReloadShader -= LoadAnimatedItemShaders;
                UnregisterHandlers(mManager);
                Patches.AnimatorBasePatch.Unpatch(HarmonyID);
            }
            base.Dispose();
        }
    }
}
