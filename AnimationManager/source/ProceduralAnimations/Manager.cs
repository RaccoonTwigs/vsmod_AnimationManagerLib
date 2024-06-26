﻿using AnimationManagerLib.API;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;

#if DEBUG
using ImGuiNET;
using VSImGui;
using VSImGui.API;
#endif

namespace AnimationManagerLib;

public class AnimationManager : API.IAnimationManager
{
    private readonly ICoreClientAPI mClientApi;
    private readonly ISynchronizer mSynchronizer;
    private readonly AnimationApplier mApplier;
    internal readonly AnimationProvider mProvider;

    private readonly Dictionary<AnimationTarget, IComposer> mComposers = new();

    private readonly Dictionary<Guid, AnimationTarget> mEntitiesByRuns = new();
    private readonly Dictionary<Guid, AnimationRequestWithStatus> mRequests = new();
    private readonly Dictionary<AnimationTarget, AnimationFrame> mAnimationFrames = new();
    private readonly HashSet<Guid> mSynchronizedPackets = new();

    internal AnimationManager(ICoreClientAPI api, ISynchronizer synchronizer)
    {
        mClientApi = api;
        mSynchronizer = synchronizer;
        mApplier = new(api);
        mProvider = new(api, this);
#if DEBUG
        //api.ModLoader.GetModSystem<VSImGui.ImGuiModSystem>().Draw += SetUpDebugWindow;
        Api = api;
#endif
    }

    public bool Register(AnimationId id, AnimationData animation) => mProvider.Register(id, animation);
    public Guid Run(AnimationTarget animationTarget, params AnimationRequest[] requests) => Run(Guid.NewGuid(), animationTarget, true, false, requests);
    public Guid Run(AnimationTarget animationTarget, bool synchronize, params AnimationRequest[] requests) => Run(Guid.NewGuid(), animationTarget, synchronize, false, requests);
    public Guid Run(AnimationTarget animationTarget, Guid runId, params AnimationRequest[] requests) => Run(runId, animationTarget, false, false, requests);
    public Guid Run(AnimationTarget animationTarget, AnimationId animationId, params RunParameters[] parameters) => Run(Guid.NewGuid(), animationTarget, true, false, ToRequests(animationId, parameters));
    public Guid Run(AnimationTarget animationTarget, bool synchronize, AnimationId animationId, params RunParameters[] parameters) => Run(Guid.NewGuid(), animationTarget, synchronize, false, ToRequests(animationId, parameters));
    public Guid RunFromPacket(AnimationTarget animationTarget, Guid runId, params AnimationRequest[] requests) => Run(runId, animationTarget, false, true, requests);
    public void Stop(Guid runId) => Stop(runId, true);
    public void Stop(Guid runId, bool synchronize)
    {
        if (mSynchronizedPackets.Contains(runId))
        {
            mSynchronizedPackets.Remove(runId);
            if (synchronize) mSynchronizer.Sync(new AnimationStopPacket(runId));
        }

        if (!mRequests.ContainsKey(runId))
        {
            mEntitiesByRuns.Remove(runId);
            return;
        }

        AnimationTarget animationTarget = mEntitiesByRuns[runId];
        IComposer composer = mComposers[animationTarget];
        AnimationRequest? request = mRequests[runId].Last();
        if (request != null) composer.Stop(request.Value);
        mEntitiesByRuns.Remove(runId);
        mRequests.Remove(runId);
    }

    private Guid Run(Guid id, AnimationTarget animationTarget, bool synchronize, bool fromServer, params AnimationRequest[] requests)
    {
        Debug.Assert(requests.Length > 0);

        ValidateEntities();

        mRequests.Add(id, new(animationTarget, synchronize, requests, this));
        AnimationRequest? request = mRequests[id].Next();
        if (request == null) return Guid.Empty;

        IComposer composer = TryAddComposer(id, animationTarget);

        foreach (AnimationId animationId in requests.Select(request => request.Animation))
        {
            IAnimation? animation = mProvider.Get(animationId, animationTarget);
            if (animation == null)
            {
                mClientApi.Logger.VerboseDebug("[Animation Manager lib] Failed to get animation '{0}' for '{1}' while trying to run request, will skip it", animationId, animationTarget);
                return Guid.Empty;
            }
            composer.Register(animationId, animation);
        }

        if (synchronize && !fromServer && animationTarget.TargetType != AnimationTargetType.EntityFirstPerson && animationTarget.TargetType != AnimationTargetType.HeldItemFp)
        {
            AnimationRunPacket packet = new()
            {
                RunId = id,
                AnimationTarget = animationTarget,
                Requests = requests
            };

            mSynchronizedPackets.Add(id);
            mSynchronizer.Sync(packet);
        }

        composer.Run(request.Value, (complete) => ComposerCallback(id, complete));

        return id;
    }

    public void OnFrameHandler(Vintagestory.API.Common.AnimationManager manager, Entity entity, float dt)
    {
        if (entity == null || !entity.Alive) ValidateEntities();

        if (entity == null) return;

        AnimationTarget animationTarget;

        if (entity is EntityPlayer player)
        {
            /*Vintagestory.API.Common.AnimationManager? tpManager = (Vintagestory.API.Common.AnimationManager?)typeof(Vintagestory.API.Common.EntityPlayer)
                                          .GetField("animManager", BindingFlags.NonPublic | BindingFlags.Instance)
                                          ?.GetValue(player);*/

            Vintagestory.API.Common.AnimationManager? fpManager = (Vintagestory.API.Common.AnimationManager?)typeof(Vintagestory.API.Common.EntityPlayer)
                                          .GetField("selfFpAnimManager", BindingFlags.NonPublic | BindingFlags.Instance)
                                          ?.GetValue(player);

            if (manager == fpManager)
            {
                animationTarget = new(entity.EntityId, AnimationTargetType.EntityFirstPerson);
            }
            else
            {
                animationTarget = new(entity.EntityId, AnimationTargetType.EntityThirdPerson);
            }
        }
        else
        {
            animationTarget = new(entity.EntityId, AnimationTargetType.EntityThirdPerson);
        }



        if (!mComposers.ContainsKey(animationTarget)) return;

        /*if (animationTarget.TargetType == AnimationTargetType.EntityFirstPerson || animationTarget.TargetType == AnimationTargetType.EntityImmersiveFirstPerson)
        {
            dt /= 2; // @TODO
        }*/

        mApplier.Clear();

        mAnimationFrames.Clear();
        TimeSpan timeSpan = TimeSpan.FromSeconds(dt);
        AnimationFrame composition = mComposers[animationTarget].Compose(timeSpan);

        mApplier.AddAnimation(entity.EntityId, composition);
    }
    public void OnFrameHandler(Vintagestory.API.Common.IAnimator animator, Shape shape, Entity entity, float dt)
    {
        if (entity == null || !entity.Alive) ValidateEntities();

        if (entity == null) return;

        AnimationTargetType targetType = AnimationTarget.GetItemTargetType(entity);
        AnimationTarget animationTarget = new(entity.EntityId, targetType);

        if (!mComposers.ContainsKey(animationTarget)) return;

        mApplier.Clear();

        mAnimationFrames.Clear();
        TimeSpan timeSpan = TimeSpan.FromSeconds(dt);
        AnimationFrame composition = mComposers[animationTarget].Compose(timeSpan);
        mApplier.AddAnimation(animator, composition, shape);
    }
    public void OnApplyAnimation(ElementPose pose, ref float weight, Shape? shape = null)
    {
        if (shape == null)
        {
            mApplier.ApplyAnimation(pose, ref weight);
        }
        else
        {
            mApplier.ApplyAnimation(pose, shape, ref weight);
        }
    }
    public void OnApplyAnimation(Entity entity, ElementPose pose, ref float weight, Shape? shape = null)
    {
        if (shape == null)
        {
            mApplier.ApplyAnimation(pose, ref weight);
        }
        else
        {
            mApplier.ApplyAnimation(pose, shape, ref weight);
        }
    }
    public void OnCalculateWeight(ElementPose pose, ref float weight, Shape? shape = null)
    {
        if (shape == null)
        {
            mApplier.CalculateWeight(pose, ref weight);
        }
        else
        {
            mApplier.CalculateWeight(pose, shape, ref weight);
        }
    }
    public void OnCalculateWeight(Entity entity, ElementPose pose, ref float weight, Shape? shape = null)
    {
        if (shape == null)
        {
            mApplier.CalculateWeight(pose, ref weight);
        }
        else
        {
            mApplier.CalculateWeight(pose, shape, ref weight);
        }
    }

    private static AnimationRequest[] ToRequests(AnimationId animationId, params RunParameters[] parameters)
    {
        List<AnimationRequest> requests = new(parameters.Length);
        foreach (RunParameters item in parameters)
        {
            requests.Add(new(animationId, item));
        }
        return requests.ToArray();
    }

    private void OnNullEntity(long entityId)
    {
        int count = 0;
        foreach ((Guid runId, _) in mEntitiesByRuns.Where(entry => entry.Value.EntityId == entityId))
        {
            Stop(runId, synchronize: false);
            count++;
        }
        mClientApi.Logger.Debug($"[Animation Manager lib] Stopped {count} animations for entity: {entityId}");
    }

    private void ValidateEntities()
    {
        foreach ((AnimationTarget target, _) in mComposers)
        {
            Entity? entity = mClientApi.World.GetEntityById(target.EntityId);

            if (entity == null || !entity.Alive)
            {
                OnNullEntity(target.EntityId);
                mComposers.Remove(target);
            }
        }
    }

    private bool ComposerCallback(Guid id, AnimationManagerLib.API.IAnimator.Status status)
    {
        if (!mRequests.ContainsKey(id)) return true;

        switch (status)
        {
            case API.IAnimator.Status.Running:
                RemoveRequest(id);
                return true;
            case API.IAnimator.Status.Stopped:
                if (mRequests[id].Finished()) return true;
                AnimationRequest? request = mRequests[id].Next();
                if (request == null) return true;
                mComposers[mEntitiesByRuns[id]].Run(request.Value, (complete) => ComposerCallback(id, complete));

                return false;
            case API.IAnimator.Status.Finished:
                if (mRequests[id].Finished())
                {
#if DEBUG
                    mProvider.Enqueue(mRequests[id]);
#endif
                    RemoveRequest(id);
                    return true;
                }
                AnimationRequest? request2 = mRequests[id].Next();
                if (request2 == null)
                {
#if DEBUG
                    mProvider.Enqueue(mRequests[id]);
#endif
                    RemoveRequest(id);
                    return true;
                }
                mComposers[mEntitiesByRuns[id]].Run(request2.Value, (complete) => ComposerCallback(id, complete));

                return false;
        }

        return false;
    }

    private void RemoveRequest(Guid id)
    {
        if (mSynchronizedPackets.Contains(id))
        {
            mSynchronizedPackets.Remove(id);
            mSynchronizer.Sync(new AnimationStopPacket(id));
        }
        mEntitiesByRuns.Remove(id);
        mRequests.Remove(id);
    }

    private IComposer TryAddComposer(Guid id, AnimationTarget animationTarget)
    {
        if (mEntitiesByRuns.ContainsKey(id)) return mComposers[mEntitiesByRuns[id]];

        mEntitiesByRuns.Add(id, animationTarget);

        if (mComposers.ContainsKey(animationTarget)) return mComposers[animationTarget];

        Composer composer = new();
        mComposers.Add(animationTarget, composer);
        return composer;
    }

    internal sealed class AnimationRequestWithStatus
    {
        private readonly AnimationRequest[] mRequests;
        private int mNextRequestIndex = 0;
        private readonly API.IAnimationManager mManager;

        public bool Synchronize { get; set; }
        public AnimationTarget AnimationTarget { get; set; }

        public AnimationRequestWithStatus(AnimationTarget animationTarget, bool synchronize, AnimationRequest[] requests, API.IAnimationManager manager)
        {
            mRequests = requests;
            Synchronize = synchronize;
            AnimationTarget = animationTarget;
            mManager = manager;
        }

        public bool IsSingleSet() => mRequests.Length == 1 && mRequests[0].Parameters.Action == AnimationPlayerAction.Set;
        public AnimationRequest? Next() => mNextRequestIndex < mRequests.Length ? mRequests[mNextRequestIndex++] : null;
        public AnimationRequest? Last() => mNextRequestIndex < mRequests.Length ? mRequests[mNextRequestIndex] : mRequests[^1];
        public bool Finished() => mNextRequestIndex >= mRequests.Length;
        public void Repeat() => mManager.Run(AnimationTarget, mRequests);
        public override string ToString() => mRequests.Select(request => request.Animation.ToString()).Aggregate((first, second) => $"{first}, {second}");
    }

#if DEBUG
    public static ICoreClientAPI? Api { get; private set; }

    public CallbackGUIStatus SetUpDebugWindow(float deltaSeconds)
    {

        ImGuiNET.ImGui.Begin("Animation manager");

        mProvider.SetUpDebugWindow();
        ImGuiNET.ImGui.Text(string.Format("Active requests: {0}", mRequests.Count));
        ImGuiNET.ImGui.Text(string.Format("Active composers: {0}", mComposers.Count));
        ImGuiNET.ImGui.NewLine();
        ImGuiNET.ImGui.SeparatorText("Composers:");

        foreach ((AnimationTarget target, IComposer composer) in mComposers)
        {
            bool collapsed = !ImGuiNET.ImGui.CollapsingHeader($"{target}");
            ImGuiNET.ImGui.Indent();
            if (!collapsed) composer.SetUpDebugWindow($"{target}");
            ImGuiNET.ImGui.Unindent();
        }

        ImGuiNET.ImGui.End();


        return CallbackGUIStatus.DontGrabMouse;
    }
#endif
}

internal class AnimationApplier
{
    public Dictionary<ElementPose, (string name, AnimationFrame composition)> Poses { get; private set; } = new();
    public Dictionary<Shape, Dictionary<ElementPose, (string name, AnimationFrame composition)>> PosesByShape { get; private set; } = new();
    static public Dictionary<uint, string> PosesNames { get; set; } = new();

    private readonly ICoreAPI mApi;

    public AnimationApplier(ICoreAPI api) => mApi = api;

    public bool CalculateWeight(ElementPose pose, ref float weight)
    {
        if (pose == null || !Poses.ContainsKey(pose)) return false;

        (string name, AnimationFrame? composition) = Poses[pose];

        composition.Weight(ref weight, Utils.ToCrc32(name));

        return true;
    }

    public bool CalculateWeight(ElementPose pose, Shape shape, ref float weight)
    {
        if (pose == null || !PosesByShape.ContainsKey(shape) || !PosesByShape[shape].ContainsKey(pose)) return false;

        (string name, AnimationFrame? composition) = PosesByShape[shape][pose];

        composition.Weight(ref weight, Utils.ToCrc32(name));

        return true;
    }

    public bool ApplyAnimation(ElementPose pose, ref float weight)
    {
        if (pose == null || !Poses.ContainsKey(pose)) return false;

        (string name, AnimationFrame? composition) = Poses[pose];
        composition.Apply(pose, ref weight, Utils.ToCrc32(name));

        Poses.Remove(pose);

        return true;
    }

    public bool ApplyAnimation(ElementPose pose, Shape shape, ref float weight)
    {
        if (pose == null || !PosesByShape.ContainsKey(shape) || !PosesByShape[shape].ContainsKey(pose)) return false;

        (string name, AnimationFrame? composition) = PosesByShape[shape][pose];
        composition.Apply(pose, ref weight, Utils.ToCrc32(name));

        Poses.Remove(pose);

        return true;
    }

    public void AddAnimation(long entityId, AnimationFrame composition)
    {
        Vintagestory.API.Common.IAnimator? animator = mApi.World.GetEntityById(entityId).AnimManager.Animator;

        if (animator == null)
        {
            return;
        }

        AddAnimation(animator, composition);
    }

    public void AddAnimation(Vintagestory.API.Common.IAnimator animator, AnimationFrame composition)
    {
        foreach ((ElementId id, _) in composition.Elements)
        {
            string name = PosesNames[id.ElementNameHash];
            ElementPose pose = animator.GetPosebyName(name);
            if (pose != null) Poses[pose] = (name, composition);
        }
    }

    public void AddAnimation(Vintagestory.API.Common.IAnimator animator, AnimationFrame composition, Shape shape)
    {
        if (!PosesByShape.ContainsKey(shape))
        {
            PosesByShape[shape] = new();
        }

        foreach ((ElementId id, _) in composition.Elements)
        {
            string name = PosesNames[id.ElementNameHash];
            ElementPose pose = animator.GetPosebyName(name);
            if (pose != null) PosesByShape[shape][pose] = (name, composition);
        }
    }

    public void Clear()
    {
        Poses.Clear();
        foreach ((Shape shape, _) in PosesByShape)
        {
            PosesByShape[shape].Clear();
        }
    }
}

internal class AnimationProvider
{
    internal readonly Dictionary<AnimationId, AnimationData> _animationsToConstruct = new();
    internal readonly Dictionary<(AnimationId, AnimationTarget), IAnimation> _constructedAnimations = new();
    internal readonly Dictionary<AnimationId, IAnimation> _animations = new();
    internal readonly ICoreClientAPI _api;

    public AnimationProvider(ICoreClientAPI api, API.IAnimationManager manager)
    {
        _api = api;
#if DEBUG
        mManager = manager;
#endif
    }

    public bool Register(AnimationId id, AnimationData data)
    {
        if (data.Shape == null)
        {
            return _animationsToConstruct.TryAdd(id, data);
        }
        else
        {
            IAnimation? animation = ConstructAnimation(_api, id, data, data.Shape);
            if (animation == null) return false;
            return _animations.TryAdd(id, animation);
        }
    }

    public IAnimation? Get(AnimationId id, AnimationTarget target)
    {
        if (_animations.ContainsKey(id)) return _animations[id];
        if (_constructedAnimations.ContainsKey((id, target))) return _constructedAnimations[(id, target)];
        if (!_animationsToConstruct.ContainsKey(id)) return null;

        Entity entity = _api.World.GetEntityById(target.EntityId);
        if (entity == null) return null;
        AnimationData data = AnimationData.Entity(_animationsToConstruct[id].Code, entity, _animationsToConstruct[id].Cyclic);
        if (data.Shape == null) return null;
        IAnimation? animation = ConstructAnimation(_api, id, data, data.Shape);
        if (animation == null) return null;
        _constructedAnimations.Add((id, target), animation);
        return _constructedAnimations[(id, target)];
    }

    private int mConstructedAnimationsCounter = 0;
    private IAnimation? ConstructAnimation(ICoreClientAPI api, AnimationId id, AnimationData data, Shape shape)
    {
        Dictionary<uint, Vintagestory.API.Common.Animation> animations = shape.AnimationsByCrc32;
        uint crc32 = Utils.ToCrc32(data.Code);
        if (!animations.ContainsKey(crc32))
        {
#if DEBUG
            api.Logger.Debug($"[Animation Manager lib] Animation '{data.Code}' was not found in shape. Procedural animation: '{id}'.");
#endif
            return null;
        }

        float totalFrames = animations[crc32].QuantityFrames;
        AnimationKeyFrame[] keyFrames = animations[crc32].KeyFrames;

        List<AnimationFrame> constructedKeyFrames = new();
        List<ushort> keyFramesToFrames = new();
        foreach (AnimationKeyFrame frame in keyFrames)
        {
            constructedKeyFrames.Add(new AnimationFrame(frame.Elements, data, id.Category));
            keyFramesToFrames.Add((ushort)frame.Frame);
            AddPosesNames(frame);
        }

        mConstructedAnimationsCounter++;
        return new Animation(id, constructedKeyFrames.ToArray(), keyFramesToFrames.ToArray(), totalFrames, data.Cyclic);
    }

    static private void AddPosesNames(AnimationKeyFrame frame)
    {
        foreach ((string poseName, _) in frame.Elements)
        {
            uint hash = Utils.ToCrc32(poseName);
            if (!AnimationApplier.PosesNames.ContainsKey(hash))
            {
                AnimationApplier.PosesNames[hash] = poseName;
            }
        }
    }
#if DEBUG
    private readonly VSImGui.FixedSizedQueue<AnimationManager.AnimationRequestWithStatus> mLastRequests = new(8);
    private AnimationManager.AnimationRequestWithStatus? mSotredRequest;
    private bool mAnimationEditorToggle = false;
    private int mNewRequestAdded = 0;
    private readonly API.IAnimationManager mManager;
    public void Enqueue(AnimationManager.AnimationRequestWithStatus request)
    {
        if (request.IsSingleSet()) return;
        mLastRequests.Enqueue(request);
        mNewRequestAdded++;
    }
    public void SetUpDebugWindow()
    {
        if (mAnimationEditorToggle) AnimationEditor();

        ImGuiNET.ImGui.Begin("Animation manager");
        if (ImGui.Button($"Show animations editor")) mAnimationEditorToggle = true;
        ImGuiNET.ImGui.Text(string.Format("Registered animations: {0}", _animationsToConstruct.Count + _animations.Count));
        ImGuiNET.ImGui.Text(string.Format("Registered pre-constructed animations: {0}", _animations.Count));
        ImGuiNET.ImGui.Text(string.Format("Registered not pre-constructed animations: {0}", _animationsToConstruct.Count));
        ImGuiNET.ImGui.Text(string.Format("Registered constructed animations: {0}", _constructedAnimations.Count));
        ImGuiNET.ImGui.Text(string.Format($"Constructed animations: {mConstructedAnimationsCounter}"));
        ImGuiNET.ImGui.End();
    }

    private int mCurrentAnimation = 0;
    private int mCurrentRequest = 0;
    private bool mSetCurrentAnimation = false;
    private float mCurrentFrameOverride = 0;
    private bool mOverrideFrame = false;
    private string mAnimationsFilter = "";
    private bool mJsonOutput = false;
    private string mJsonOutputValue = "";
    public void AnimationEditor()
    {
        if (_constructedAnimations.Count == 0) return;
        if (mCurrentAnimation >= _constructedAnimations.Count) mCurrentAnimation = _constructedAnimations.Count - 1;

        string[] animationIds = _constructedAnimations.Select(value => $"Animation: {value.Key.Item1}, Target: {value.Key.Item2}").ToArray();
        IAnimation[] animations = _constructedAnimations.Select(value => value.Value).ToArray();
        string[] requests = mLastRequests.Queue.Select(request => request.ToString()).Reverse().ToArray();

        ImGui.Begin($"Animations editor", ref mAnimationEditorToggle);

        if (requests.Length > 0)
        {
            ImGui.SeparatorText("Last requests");
            if (ImGui.Button($"Repeat request##Animations editor")) mLastRequests.Queue.Reverse().ToArray()[mCurrentRequest].Repeat();
            ImGui.SameLine();
            if (ImGui.Button($"Store request##Animations editor")) mSotredRequest = mLastRequests.Queue.Reverse().ToArray()[mCurrentRequest];
            ImGui.SameLine();
            if (mSotredRequest == null) ImGui.BeginDisabled();
            if (ImGui.Button($"Repeat stored request##Animations editor")) mSotredRequest?.Repeat();
            if (mSotredRequest == null) ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button($"Output to JSON##Animations editor"))
            {
                mJsonOutput = true;
                mJsonOutputValue = (animations[mCurrentAnimation] as ISerializable)?.Serialize().ToString() ?? "";
            }
            ImGui.ListBox($"Last requests##Animations editor", ref mCurrentRequest, requests, requests.Length);
        }

        ImGui.SeparatorText("Animations");
        ImGui.Checkbox($"Set current key frame", ref mSetCurrentAnimation);
        ImGui.SameLine();
        ImGui.Checkbox($"Override frame value", ref mOverrideFrame);
        if (!mOverrideFrame) ImGui.BeginDisabled();
        float maxFrame = (animations[mCurrentAnimation] as Animation).TotalFrames - 1;
        bool frameModified = false;
        if (ImGui.SliderFloat($"Frame##Animations editor", ref mCurrentFrameOverride, 0, maxFrame)) frameModified = mOverrideFrame;
        if (!mOverrideFrame) ImGui.EndDisabled();

        ImGui.InputTextWithHint($"Elements filter##Animations editor", "supports wildcards", ref mAnimationsFilter, 100);

        FilterAnimations(StyleEditor.WildCardToRegular(mAnimationsFilter), animationIds, out string[] names, out int[] indexes);
        ImGui.ListBox($"Constructed animations##Animations editor", ref mCurrentAnimation, names, names.Length);
        mCurrentAnimation = indexes.Length <= mCurrentAnimation ? 0 : indexes[mCurrentAnimation];

        ImGui.SeparatorText("Frames");
        bool modified = animations[mCurrentAnimation].Editor($"Animations editor##{animationIds[mCurrentAnimation]}");

        if (modified || mSetCurrentAnimation && mNewRequestAdded > 1 || frameModified) SetAnimationFrame(animations[mCurrentAnimation]);

        ImGui.End();

        if (mJsonOutput)
        {
            ImGui.Begin($"JSON output##Animations editor", ref mJsonOutput, ImGuiWindowFlags.Modal);
            if (ImGui.Button("Copy##json") || CopyCombination())
            {
                ImGui.SetClipboardText(mJsonOutputValue);
            }
            System.Numerics.Vector2 size = ImGui.GetWindowSize();
            size.X -= 8;
            size.Y -= 34;
            ImGui.InputTextMultiline($"##Animations editor", ref mJsonOutputValue, (uint)mJsonOutputValue.Length * 2, size, ImGuiInputTextFlags.ReadOnly);
            ImGui.End();
        }
    }

    private bool CopyCombination()
    {
        ImGuiIOPtr io = ImGui.GetIO();
        return io.KeyCtrl && io.KeysDown[(int)ImGuiKey.C];
    }

    public void SetAnimationFrame(IAnimation animation)
    {
        float frame = (animation as Animation).CurrentFrame;
        RunParameters runParams = RunParameters.Set(mOverrideFrame ? mCurrentFrameOverride : frame);
        AnimationTarget target = new(_api.World.Player.Entity);

        mManager.Run(target, (animation as Animation).Id, runParams);
        mNewRequestAdded = 0;

    }

    private void FilterAnimations(string filter, string[] animationIds, out string[] names, out int[] indexes)
    {

        names = animationIds;
        int count = 0;
        indexes = animationIds.Select(_ => count++).ToArray();

        if (filter == "") return;

        List<string> newNames = new();
        List<int> newIndexes = new();

        for (int index = 0; index < names.Length; index++)
        {
            if (StyleEditor.Match(filter, names[index]))
            {
                if (mCurrentAnimation == index)
                {
                    mCurrentAnimation = newIndexes.Count;
                }
                newIndexes.Add(index);
                newNames.Add(names[index]);
            }
        }

        names = newNames.ToArray();
        indexes = newIndexes.ToArray();
    }

#endif
}
