﻿using ProtoBuf;
using System;

namespace AnimationManagerLib.API
{
    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public struct AnimationRunPacket
    {
        public Guid RunId { get; set; }
        public AnimationTarget AnimationTarget { get; set; }
        public AnimationRequest[] Requests { get; set; }
    }

    [ProtoContract(ImplicitFields = ImplicitFields.AllPublic)]
    public struct AnimationStopPacket
    {
        public Guid RunId { get; set; }

        public AnimationStopPacket(Guid runId) => RunId = runId;
    }

    public struct AnimationRunMetadata
    {
        public AnimationPlayerAction Action { get; set; }
        public TimeSpan Duration { get; set; }
        public float? StartFrame { get; set; }
        public float? TargetFrame { get; set; }
        public ProgressModifierType Modifier { get; set; }

        public AnimationRunMetadata(AnimationRequest request)
        {
            Action = request.Parameters.Action;
            Duration = request.Parameters.Duration;
            StartFrame = request.Parameters.StartFrame;
            TargetFrame = request.Parameters.TargetFrame;
            Modifier = request.Parameters.Modifier;
        }
        public static implicit operator AnimationRunMetadata(AnimationRequest request) => new(request);
    }

    public interface IHasDebugWindow
    {
        void SetUpDebugWindow();
    }

    public interface IAnimation
    {
        public AnimationFrame Play(float progress, float? startFrame = null, float? endFrame = null);
        public AnimationFrame Blend(float progress, float? targetFrame, AnimationFrame endFrame);
        public AnimationFrame Blend(float progress, AnimationFrame startFrame, AnimationFrame endFrame);
    }

    public interface IAnimator : IHasDebugWindow
    {
        enum Status
        {
            Running,
            Stopped,
            Finished
        }
        
        public void Init(Category category);
        public void Run(AnimationRunMetadata parameters, IAnimation animation);
        public AnimationFrame Calculate(TimeSpan timeElapsed, out Status status);
    }

    public interface IComposer : IHasDebugWindow
    {
        delegate bool IfRemoveAnimator(bool complete);

        void SetAnimatorType<TAnimator>()
            where TAnimator : IAnimator;
        bool Register(AnimationId id, IAnimation animation);
        void Run(AnimationRequest request, IfRemoveAnimator finishCallback);
        void Stop(AnimationRequest request);
        AnimationFrame Compose(TimeSpan timeElapsed);
    }

    public interface ISynchronizer
    {
        public delegate void AnimationRunHandler(AnimationRunPacket request);
        public delegate void AnimationStopHandler(AnimationStopPacket request);
        void Init(Vintagestory.API.Common.ICoreAPI api, AnimationRunHandler? runHandler, AnimationStopHandler? stopHandler, string channelName);
        void Sync(AnimationRunPacket request);
        void Sync(AnimationStopPacket request);
    }
}