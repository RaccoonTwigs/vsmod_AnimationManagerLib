using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace AnimationManagerLib.Integration;

internal class ProceduralAnimationManager : Vintagestory.API.Common.PlayerAnimationManager
{
    public ProceduralAnimationManager(AnimationManager manager, ICoreClientAPI clientApi, Vintagestory.API.Common.AnimationManager previousManager)
    {
        api = clientApi;
        capi = clientApi;
        Triggers = previousManager.Triggers;

        entity = (Entity?)_managerEntity?.GetValue(previousManager);
        if (previousManager.Animator != null) Animator = ProceduralClientAnimator.Create(manager, this, previousManager.Animator as ClientAnimator, entity);
        HeadController = previousManager.HeadController;
        ActiveAnimationsByAnimCode = previousManager.ActiveAnimationsByAnimCode;

        _manager = manager;
    }

    public override void OnClientFrame(float dt)
    {
        if (base.Animator is ClientAnimator && base.Animator is not ProceduralClientAnimator)
        {
            Animator = base.Animator;
        }

        try
        {
            _manager.OnFrameHandler(this, entity, dt);
            base.OnClientFrame(dt);
        }
        catch { }
    }

    public new IAnimator Animator
    {
        get => base.Animator;
        set
        {
            base.Animator = ProceduralClientAnimator.Create(_manager, this, value as ClientAnimator, entity);
        }
    }

    private static readonly FieldInfo? _managerEntity = typeof(Vintagestory.API.Common.AnimationManager).GetField("entity", BindingFlags.NonPublic | BindingFlags.Instance);
    private readonly AnimationManager _manager;
}

internal class ProceduralClientAnimator : ClientAnimator
{
    public ProceduralClientAnimator(ClientAnimator previous, EntityAgent entity, AnimationManager manager, Vintagestory.API.Common.Animation[] animations, Action<string> onAnimationStoppedListener) : base(
        () => entity.Controls.MovespeedMultiplier * entity.GetWalkSpeedMultiplier(),
        previous.RootPoses,
        animations,
        previous.rootElements,
        previous.jointsById,
        onAnimationStoppedListener
        )
    {
        _entity = entity;
        _manager = manager;
        _colliders = entity.GetBehavior<CollidersEntityBehavior>();

        frameByDepthByAnimation = (List<ElementPose>[][])_frameByDepthByAnimation.GetValue(previous);
        nextFrameTransformsByAnimation = (List<ElementPose>[][])_nextFrameTransformsByAnimation.GetValue(previous);
        weightsByAnimationAndElement = (ShapeElementWeights[][][])_weightsByAnimationAndElement.GetValue(previous);
        prevFrameArray = (int[])((int[])_prevFrame.GetValue(previous)).Clone();
        nextFrameArray = (int[])((int[])_nextFrame.GetValue(previous)).Clone();
        localTransformMatrix = (float[])((float[])_localTransformMatrix.GetValue(previous)).Clone();
        weightsByAnimationAndElement_this = (ShapeElementWeights[][][])((ShapeElementWeights[][][])_weightsByAnimationAndElement_this.GetValue(previous)).Clone();
        tmpMatrix = (float[])((float[])_tmpMatrix.GetValue(previous)).Clone();
    }

    public ProceduralClientAnimator(Shape shape, AnimationManager manager, WalkSpeedSupplierDelegate walkSpeedSupplier, List<ElementPose> rootPoses, Vintagestory.API.Common.Animation[] animations, ShapeElement[] rootElements, Dictionary<int, AnimationJoint> jointsById, Action<string> onAnimationStoppedListener = null) : base(
        walkSpeedSupplier,
        rootPoses,
        animations,
        rootElements,
        jointsById,
        onAnimationStoppedListener
        )
    {
        _entity = null;
        _manager = manager;
        _shape = shape;

        frameByDepthByAnimation = (List<ElementPose>[][])_frameByDepthByAnimation.GetValue(this);
        nextFrameTransformsByAnimation = (List<ElementPose>[][])_nextFrameTransformsByAnimation.GetValue(this);
        weightsByAnimationAndElement = (ShapeElementWeights[][][])_weightsByAnimationAndElement.GetValue(this);
        prevFrameArray = (int[])((int[])_prevFrame.GetValue(this)).Clone();
        nextFrameArray = (int[])((int[])_nextFrame.GetValue(this)).Clone();
        localTransformMatrix = (float[])((float[])_localTransformMatrix.GetValue(this)).Clone();
        weightsByAnimationAndElement_this = (ShapeElementWeights[][][])((ShapeElementWeights[][][])_weightsByAnimationAndElement_this.GetValue(this)).Clone();
        tmpMatrix = (float[])((float[])_tmpMatrix.GetValue(this)).Clone();
    }

    public ProceduralClientAnimator(Shape shape, AnimationManager manager, WalkSpeedSupplierDelegate walkSpeedSupplier, Vintagestory.API.Common.Animation[] animations, ShapeElement[] rootElements, Dictionary<int, AnimationJoint> jointsById, Action<string> onAnimationStoppedListener = null) : base(
        walkSpeedSupplier,
        animations,
        rootElements,
        jointsById,
        onAnimationStoppedListener
        )
    {
        _entity = null;
        _manager = manager;
        _shape = shape;

        frameByDepthByAnimation = (List<ElementPose>[][])_frameByDepthByAnimation.GetValue(this);
        nextFrameTransformsByAnimation = (List<ElementPose>[][])_nextFrameTransformsByAnimation.GetValue(this);
        weightsByAnimationAndElement = (ShapeElementWeights[][][])_weightsByAnimationAndElement.GetValue(this);
        prevFrameArray = (int[])((int[])_prevFrame.GetValue(this)).Clone();
        nextFrameArray = (int[])((int[])_nextFrame.GetValue(this)).Clone();
        localTransformMatrix = (float[])((float[])_localTransformMatrix.GetValue(this)).Clone();
        weightsByAnimationAndElement_this = (ShapeElementWeights[][][])((ShapeElementWeights[][][])_weightsByAnimationAndElement_this.GetValue(this)).Clone();
        tmpMatrix = (float[])((float[])_tmpMatrix.GetValue(this)).Clone();
    }

    public static ProceduralClientAnimator Create(AnimationManager manager, ProceduralAnimationManager proceduralManager, ClientAnimator previousAnimator, Entity entity)
    {
        WalkSpeedSupplierDelegate? walkSpeedSupplier = (WalkSpeedSupplierDelegate?)_walkSpeedSupplier?.GetValue(previousAnimator);
        Action<string>? onAnimationStoppedListener = (Action<string>?)_onAnimationStoppedListener?.GetValue(previousAnimator);
        Vintagestory.API.Common.Animation[] animations = (Vintagestory.API.Common.Animation[])previousAnimator.anims.Select(entry => entry.Animation).ToArray().Clone();

        ProceduralClientAnimator result = new(previousAnimator, entity as EntityAgent, manager, animations, proceduralManager.OnAnimationStopped);


        return result;
    }

    protected override void calculateMatrices(float dt)
    {
        if (!base.CalculateMatrices)
        {
            return;
        }

        try
        {
            jointsDone.Clear();
            int num = 0;
            for (int i = 0; i < activeAnimCount; i++)
            {
                RunningAnimation runningAnimation = CurAnims[i];
                weightsByAnimationAndElement[0][i] = runningAnimation.ElementWeights;
                num = Math.Max(num, runningAnimation.Animation.Version);
                Vintagestory.API.Common.AnimationFrame[] array = runningAnimation.Animation.PrevNextKeyFrameByFrame[(int)runningAnimation.CurrentFrame % runningAnimation.Animation.QuantityFrames];
                frameByDepthByAnimation[0][i] = array[0].RootElementTransforms;
                prevFrameArray[i] = array[0].FrameNumber;
                if (runningAnimation.Animation.OnAnimationEnd == EnumEntityAnimationEndHandling.Hold && (int)runningAnimation.CurrentFrame + 1 == runningAnimation.Animation.QuantityFrames)
                {
                    nextFrameTransformsByAnimation[0][i] = array[0].RootElementTransforms;
                    nextFrameArray[i] = array[0].FrameNumber;
                }
                else
                {
                    nextFrameTransformsByAnimation[0][i] = array[1].RootElementTransforms;
                    nextFrameArray[i] = array[1].FrameNumber;
                }
            }

            CalculateMatrices(num, dt, RootPoses, weightsByAnimationAndElement[0], Mat4f.Create(), frameByDepthByAnimation[0], nextFrameTransformsByAnimation[0], 0);

            for (int j = 0; j < GlobalConstants.MaxAnimatedElements; j++)
            {
                if (!jointsById.ContainsKey(j))
                {
                    for (int k = 0; k < 12; k++)
                    {
                        TransformationMatrices4x3[j * 12 + k] = AnimatorBase.identMat4x3[k];
                    }
                }
            }

            foreach (KeyValuePair<string, AttachmentPointAndPose> item in AttachmentPointByCode)
            {
                for (int l = 0; l < 16; l++)
                {
                    item.Value.AnimModelMatrix[l] = item.Value.CachedPose.AnimModelMatrix[l];
                }
            }
        }
        catch (Exception exception)
        {

        }
    }

    private readonly Entity? _entity;
    private readonly CollidersEntityBehavior? _colliders;
    private readonly AnimationManager _manager;
    private readonly Shape? _shape;

    private readonly List<ElementPose>[][] frameByDepthByAnimation;
    private readonly List<ElementPose>[][] nextFrameTransformsByAnimation;
    private readonly ShapeElementWeights[][][] weightsByAnimationAndElement;
    private readonly int[] prevFrameArray;
    private readonly int[] nextFrameArray;
    private readonly float[] localTransformMatrix;
    private readonly ShapeElementWeights[][][] weightsByAnimationAndElement_this;
    private readonly float[] tmpMatrix;

    private static readonly FieldInfo? _walkSpeedSupplier = typeof(Vintagestory.API.Common.ClientAnimator).GetField("WalkSpeedSupplier", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _onAnimationStoppedListener = typeof(Vintagestory.API.Common.ClientAnimator).GetField("onAnimationStoppedListener", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _frameByDepthByAnimation = typeof(Vintagestory.API.Common.ClientAnimator).GetField("frameByDepthByAnimation", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _nextFrameTransformsByAnimation = typeof(Vintagestory.API.Common.ClientAnimator).GetField("nextFrameTransformsByAnimation", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _weightsByAnimationAndElement = typeof(Vintagestory.API.Common.ClientAnimator).GetField("weightsByAnimationAndElement", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _prevFrame = typeof(Vintagestory.API.Common.ClientAnimator).GetField("prevFrame", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _nextFrame = typeof(Vintagestory.API.Common.ClientAnimator).GetField("nextFrame", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _localTransformMatrix = typeof(Vintagestory.API.Common.ClientAnimator).GetField("localTransformMatrix", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
    private static readonly FieldInfo? _weightsByAnimationAndElement_this = typeof(Vintagestory.API.Common.ClientAnimator).GetField("weightsByAnimationAndElement", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
    private static readonly FieldInfo? _activeAnimationCount = typeof(Vintagestory.API.Common.AnimatorBase).GetField("activeAnimCount", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
    private static readonly FieldInfo? _jointsDone = typeof(Vintagestory.API.Common.ClientAnimator).GetField("jointsDone", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
    private static readonly FieldInfo? _tmpMatrix = typeof(Vintagestory.API.Common.ClientAnimator).GetField("tmpMatrix", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

    private bool CalculateMatrices(
        int animVersion,
        float dt,
        List<ElementPose> outFrame,
        ShapeElementWeights[][] weightsByAnimationAndElement,
        float[] modelMatrix,
        List<ElementPose>[] nowKeyFrameByAnimation,
        List<ElementPose>[] nextInKeyFrameByAnimation,
        int depth
    )
    {
        depth++;
        List<ElementPose>[] nowChildKeyFrameByAnimation = frameByDepthByAnimation[depth];
        List<ElementPose>[] nextChildKeyFrameByAnimation = nextFrameTransformsByAnimation[depth];
        ShapeElementWeights[][] childWeightsByAnimationAndElement = weightsByAnimationAndElement_this[depth];


        for (int childPoseIndex = 0; childPoseIndex < outFrame.Count; childPoseIndex++)
        {
            ElementPose outFramePose = outFrame[childPoseIndex];
            ShapeElement elem = outFramePose.ForElement;

            SetMat(outFramePose, modelMatrix);
            Mat4f.Identity(localTransformMatrix);

            outFramePose.Clear();

            float weightSum = SumWeights(childPoseIndex, weightsByAnimationAndElement);
            float weightSumCopy = weightSum;

            try
            {
                if (_entity != null)
                {
                    _manager.OnCalculateWeight(_entity, outFramePose, ref weightSum, _shape);
                }
                else
                {
                    _manager.OnCalculateWeight(outFramePose, ref weightSum, _shape);
                }
            }
            catch (Exception exception)
            {
            }

            CalculateAnimationForElements(
                    nowChildKeyFrameByAnimation,
                    nextChildKeyFrameByAnimation,
                    childWeightsByAnimationAndElement,
                    nowKeyFrameByAnimation,
                    nextInKeyFrameByAnimation,
                    weightsByAnimationAndElement,
                    outFramePose,
                    ref weightSum,
                    childPoseIndex
                    );

            try
            {
                if (_entity != null)
                {
                    _manager.OnApplyAnimation(_entity, outFramePose, ref weightSumCopy, _shape);
                }
                else
                {
                    _manager.OnApplyAnimation(outFramePose, ref weightSumCopy, _shape);
                }
            }
            catch (Exception exception)
            {
            }

            elem.GetLocalTransformMatrix(animVersion, localTransformMatrix, outFramePose);
            Mat4f.Mul(outFramePose.AnimModelMatrix, outFramePose.AnimModelMatrix, localTransformMatrix);

            CalculateElementTransformMatrices(elem, outFramePose);

            #region Colliders
            if (_colliders != null && _colliders.UnprocessedElementsLeft && _colliders.ShapeElementsToProcess.Contains(elem.Name))
            {
                _colliders.Colliders.Add(elem.Name, new ShapeElementCollider(elem));
                _colliders.ShapeElementsToProcess.Remove(elem.Name);
                _colliders.UnprocessedElementsLeft = _colliders.ShapeElementsToProcess.Count > 0;
                Console.WriteLine($"CollidersEntityBehavior: {elem.Name} processed");
            }
            if (_colliders != null && _colliders.Colliders.TryGetValue(elem.Name, out ShapeElementCollider? collider))
            {
                Mat4f.Mul(tmpMatrix, outFramePose.AnimModelMatrix, elem.inverseModelTransform);
                collider?.Transform(tmpMatrix);
            }
            #endregion

            

            if (outFramePose.ChildElementPoses != null)
            {
                CalculateMatrices(
                    animVersion,
                    dt,
                    outFramePose.ChildElementPoses,
                    childWeightsByAnimationAndElement,
                    outFramePose.AnimModelMatrix,
                    nowChildKeyFrameByAnimation,
                    nextChildKeyFrameByAnimation,
                    depth
                );
            }

        }

        return false;
    }

    public float[] GetShapeElementModelMatrix(ShapeElement element)
    {
        List<ShapeElement> parentPath = element.GetParentPath();
        float[] array = Mat4f.Create();
        for (int i = 0; i < parentPath.Count; i++)
        {
            float[] localTransformMatrix = parentPath[i].GetLocalTransformMatrix(0);
            Mat4f.Mul(array, array, localTransformMatrix);
        }

        Mat4f.Mul(array, array, element.GetLocalTransformMatrix(0));
        return array;
    }

    private static void SetMat(ElementPose pose, float[] modelMatrix)
    {
        for (int i = 0; i < 16; i++)
        {
            pose.AnimModelMatrix[i] = modelMatrix[i];
        }
    }

    private float SumWeights(int childPoseIndex, ShapeElementWeights[][] weightsByAnimationAndElement)
    {
        int? activeAnimationCount = (int?)_activeAnimationCount?.GetValue(this); // @TODO replace reflection with something else
        if (activeAnimationCount == null) return 0;

        float weightSum = 0f;
        for (int animationIndex = 0; animationIndex < activeAnimationCount.Value; animationIndex++)
        {
            RunningAnimation animation = CurAnims[animationIndex];
            ShapeElementWeights weight = weightsByAnimationAndElement[animationIndex][childPoseIndex];

            if (weight.BlendMode != EnumAnimationBlendMode.Add)
            {
                weightSum += weight.Weight * animation.EasingFactor;
            }
        }

        return weightSum;
    }

    private void CalculateAnimationForElements(
        List<ElementPose>[] nowChildKeyFrameByAnimation,
        List<ElementPose>[] nextChildKeyFrameByAnimation,
        ShapeElementWeights[][] childWeightsByAnimationAndElement,
        List<ElementPose>[] nowKeyFrameByAnimation,
        List<ElementPose>[] nextInKeyFrameByAnimation,
        ShapeElementWeights[][] weightsByAnimationAndElement,
        ElementPose outFramePose,
        ref float weightSum,
        int childPoseIndex
    )
    {
        int? activeAnimationCount = (int?)_activeAnimationCount?.GetValue(this); // @TODO replace reflection

        if (activeAnimationCount == null || prevFrameArray == null || nextFrameArray == null) return;

        for (int animationIndex = 0; animationIndex < activeAnimationCount.Value; animationIndex++)
        {
            RunningAnimation animation = CurAnims[animationIndex];
            ShapeElementWeights sew = weightsByAnimationAndElement[animationIndex][childPoseIndex];
            CalcBlendedWeight(animation, weightSum / sew.Weight, sew.BlendMode);

            ElementPose nowFramePose = nowKeyFrameByAnimation[animationIndex][childPoseIndex];
            ElementPose nextFramePose = nextInKeyFrameByAnimation[animationIndex][childPoseIndex];

            int prevFrame = prevFrameArray[animationIndex];
            int nextFrame = nextFrameArray[animationIndex];

            // May loop around, so nextFrame can be smaller than prevFrame
            float keyFrameDist = nextFrame > prevFrame ? (nextFrame - prevFrame) : (animation.Animation.QuantityFrames - prevFrame + nextFrame);
            float curFrameDist = animation.CurrentFrame >= prevFrame ? (animation.CurrentFrame - prevFrame) : (animation.Animation.QuantityFrames - prevFrame + animation.CurrentFrame);

            float lerp = curFrameDist / keyFrameDist;

            outFramePose.Add(nowFramePose, nextFramePose, lerp, animation.BlendedWeight);

            nowChildKeyFrameByAnimation[animationIndex] = nowFramePose.ChildElementPoses;
            childWeightsByAnimationAndElement[animationIndex] = sew.ChildElements;

            nextChildKeyFrameByAnimation[animationIndex] = nextFramePose.ChildElementPoses;
        }
    }

    private static void CalcBlendedWeight(RunningAnimation animation, float weightSum, EnumAnimationBlendMode blendMode)
    {
        if (weightSum == 0f)
        {
            animation.BlendedWeight = animation.EasingFactor;
        }
        else
        {
            animation.BlendedWeight = GameMath.Clamp((blendMode == EnumAnimationBlendMode.Add) ? animation.EasingFactor : (animation.EasingFactor / Math.Max(animation.meta.WeightCapFactor, weightSum)), 0f, 1f);
        }
    }

    private void CalculateElementTransformMatrices(ShapeElement element, ElementPose pose)
    {
        if (jointsDone == null || tmpMatrix == null) return;

        if (element.JointId > 0 && !jointsDone.Contains(element.JointId))
        {
            Mat4f.Mul(tmpMatrix, pose.AnimModelMatrix, element.inverseModelTransform);

            int index = 12 * element.JointId;
            TransformationMatrices4x3[index++] = tmpMatrix[0];
            TransformationMatrices4x3[index++] = tmpMatrix[1];
            TransformationMatrices4x3[index++] = tmpMatrix[2];
            TransformationMatrices4x3[index++] = tmpMatrix[4];
            TransformationMatrices4x3[index++] = tmpMatrix[5];
            TransformationMatrices4x3[index++] = tmpMatrix[6];
            TransformationMatrices4x3[index++] = tmpMatrix[8];
            TransformationMatrices4x3[index++] = tmpMatrix[9];
            TransformationMatrices4x3[index++] = tmpMatrix[10];
            TransformationMatrices4x3[index++] = tmpMatrix[12];
            TransformationMatrices4x3[index++] = tmpMatrix[13];
            TransformationMatrices4x3[index] = tmpMatrix[14];

            jointsDone.Add(element.JointId);
        }
    }
}