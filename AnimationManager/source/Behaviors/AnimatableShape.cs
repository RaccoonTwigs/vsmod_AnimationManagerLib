﻿using AnimationManagerLib.API;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace AnimationManagerLib;

public sealed class AnimatableShape : ITexPositionSource, IDisposable
{
    public Shape Shape { get; private set; }
    public MultiTextureMeshRef MeshRef { get; private set; }
    public ITextureAtlasAPI Atlas { get; private set; }

    private readonly ICoreClientAPI mClientApi;
    private readonly AnimatableShapeRenderer mRenderer;
    private readonly Dictionary<long, AnimatorBase> mAnimators = new();
    private readonly Dictionary<long, string> mCacheKeys = new();
    private readonly string mCachePrefix;

    private bool mDisposed = false;

    public static AnimatableShape? Create(ICoreClientAPI api, string shapePath)
    {
        string cacheKey = $"shapeEditorCollectibleMeshes-{shapePath.GetHashCode()}";
        AssetLocation shapeLocation = new(shapePath);
        shapeLocation = shapeLocation.WithPathAppendixOnce(".json").WithPathPrefixOnce("shapes/");

        Shape? currentShape = Shape.TryGet(api, shapeLocation);
        currentShape?.ResolveReferences(api.Logger, cacheKey);

        if (currentShape == null) return null;

        return new AnimatableShape(api, cacheKey, currentShape);
    }

    public AnimatorBase? GetAnimator(long entityId)
    {
        if (mAnimators.ContainsKey(entityId)) return mAnimators[entityId];

        RemoveAnimatorsForNonValidEntities();

        string cacheKey = $"{mCachePrefix}.{entityId}";

        AnimatorBase? animator = GetAnimator(mClientApi, cacheKey, Shape);

        if (animator == null) return null;

        mAnimators.Add(entityId, animator);
        mCacheKeys.Add(entityId, cacheKey);
        return animator;
    }

    public void Render(
        IShaderProgram shaderProgram,
        ItemRenderInfo itemStackRenderInfo,
        IRenderAPI render,
        ItemStack itemStack,
        Vec4f lightrgbs,
        Matrixf itemModelMat,
        Entity entity,
        float dt
        ) => mRenderer.Render(shaderProgram, itemStackRenderInfo, render, itemStack, lightrgbs, itemModelMat, entity, dt);

    private AnimatableShape(ICoreClientAPI api, string cacheKey, Shape currentShape)
    {
        mClientApi = api;
        mCachePrefix = cacheKey;
        Shape = currentShape;
        Atlas = api.ItemTextureAtlas;

        MeshData meshData = InitializeMeshData(api, cacheKey, currentShape, this);
        MeshRef = InitializeMeshRef(api, meshData);
        mRenderer = new(api, this);
    }
    private void RemoveAnimatorsForNonValidEntities()
    {
        foreach ((long entityId, var animator) in mAnimators)
        {
            Entity? entity = mClientApi.World.GetEntityById(entityId);

            if (entity == null || !entity.Alive)
            {
                mAnimators.Remove(entityId);
                mCacheKeys.Remove(entityId);
            }
        }
    }
    private static MeshData InitializeMeshData(ICoreClientAPI clientApi, string cacheDictKey, Shape shape, ITexPositionSource texSource)
    {
        shape.ResolveReferences(clientApi.World.Logger, cacheDictKey);
        CacheInvTransforms(shape.Elements);
        shape.ResolveAndFindJoints(clientApi.Logger, cacheDictKey);

        clientApi.Tesselator.TesselateShapeWithJointIds("collectible", shape, out MeshData meshData, texSource, null);

        return meshData;
    }
    private static MultiTextureMeshRef InitializeMeshRef(ICoreClientAPI clientApi, MeshData meshData)
    {
        MultiTextureMeshRef? meshRef = null;

        if (RuntimeEnv.MainThreadId == Environment.CurrentManagedThreadId)
        {
            meshRef = clientApi.Render.UploadMultiTextureMesh(meshData);
        }
        else
        {
            clientApi.Event.EnqueueMainThreadTask(() =>
            {
                meshRef = clientApi.Render.UploadMultiTextureMesh(meshData);
            }, "uploadmesh");
        }

        Debug.Assert(meshRef != null);
        return meshRef;
    }
    private static void CacheInvTransforms(ShapeElement[] elements)
    {
        if (elements == null) return;

        for (int i = 0; i < elements.Length; i++)
        {
            elements[i].CacheInverseTransformMatrix();
            CacheInvTransforms(elements[i].Children);
        }
    }
    private static AnimatorBase? GetAnimator(ICoreClientAPI clientApi, string cacheDictKey, Shape? blockShape)
    {
        if (blockShape == null)
        {
            return null;
        }

        Dictionary<string, AnimCacheEntry>? animationCache;
        clientApi.ObjectCache.TryGetValue("coAnimCache", out object? animCacheObj);
        animationCache = animCacheObj as Dictionary<string, AnimCacheEntry>;
        if (animationCache == null)
        {
            clientApi.ObjectCache["coAnimCache"] = animationCache = new Dictionary<string, AnimCacheEntry>();
        }

        AnimatorBase animator;

        if (animationCache.TryGetValue(cacheDictKey, out AnimCacheEntry? cacheObj))
        {
            animator = clientApi.Side == EnumAppSide.Client ?
                new ClientAnimator(() => 1, cacheObj.RootPoses, cacheObj.Animations, cacheObj.RootElems, blockShape.JointsById) :
                new ServerAnimator(() => 1, cacheObj.RootPoses, cacheObj.Animations, cacheObj.RootElems, blockShape.JointsById)
            ;
        }
        else
        {
            for (int i = 0; blockShape.Animations != null && i < blockShape.Animations.Length; i++)
            {
                blockShape.Animations[i].GenerateAllFrames(blockShape.Elements, blockShape.JointsById);
            }

            animator = clientApi.Side == EnumAppSide.Client ?
                new ClientAnimator(() => 1, blockShape.Animations, blockShape.Elements, blockShape.JointsById) :
                new ServerAnimator(() => 1, blockShape.Animations, blockShape.Elements, blockShape.JointsById)
            ;

            animationCache[cacheDictKey] = new AnimCacheEntry()
            {
                Animations = blockShape.Animations,
                RootElems = (animator as ClientAnimator)?.rootElements,
                RootPoses = (animator as ClientAnimator)?.RootPoses
            };
        }

        return animator;
    }


    public Size2i? AtlasSize => Atlas?.Size;
    public TextureAtlasPosition? this[string textureCode]
    {
        get
        {
            AssetLocation? texturePath = null;
            Shape?.Textures.TryGetValue(textureCode, out texturePath);

            if (texturePath == null)
            {
                texturePath = new AssetLocation(textureCode);
            }

            return GetOrCreateTexPos(texturePath);
        }
    }
    private TextureAtlasPosition? GetOrCreateTexPos(AssetLocation texturePath)
    {
        if (Atlas == null) return null;

        TextureAtlasPosition texturePosition = Atlas[texturePath];

        if (texturePosition == null)
        {
            IAsset texAsset = mClientApi.Assets.TryGet(texturePath.Clone().WithPathPrefixOnce("textures/").WithPathAppendixOnce(".png"));
            if (texAsset != null)
            {
                Atlas.GetOrInsertTexture(texturePath, out _, out texturePosition);
            }
            else
            {
                mClientApi.World.Logger.Warning($"Bullseye.CollectibleBehaviorAnimatable: texture {texturePath}, not no such texture found.");
            }
        }

        return texturePosition;
    }

    public void Dispose()
    {
        if (mDisposed) return;
        mDisposed = true;

        MeshRef.Dispose();
    }
}

public class AnimatableShapeRenderer
{
    private float mTimeAccumulation = 0;
    private readonly ICoreClientAPI mClientApi;
    private readonly AnimatableShape mShape;

    public AnimatableShapeRenderer(ICoreClientAPI api, AnimatableShape shape)
    {
        mClientApi = api;
        mShape = shape;
    }

    public void Render(IShaderProgram shaderProgram, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Vec4f lightrgbs, Matrixf itemModelMat, Entity entity, float dt)
    {
        RenderAnimatableShape(shaderProgram, mClientApi.World, mShape, itemStackRenderInfo, render, itemStack, entity, lightrgbs, itemModelMat);
        SpawnParticles(itemModelMat, itemStack, dt, ref mTimeAccumulation, mClientApi, entity);
    }

    private static void RenderAnimatableShape(IShaderProgram shaderProgram, IWorldAccessor world, AnimatableShape shape, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Entity entity, Vec4f lightrgbs, Matrixf itemModelMat)
    {
        string textureSampleName = "tex";

        shaderProgram.Use();

        AnimatorBase? animator = shape.GetAnimator(entity.EntityId);
        if (animator == null)
        {
            shaderProgram.Stop();
            return;
        }
        FillShaderValues(shaderProgram, itemStackRenderInfo, render, itemStack, lightrgbs, itemModelMat, world, animator);

        if (!itemStackRenderInfo.CullFaces)
        {
            render.GlDisableCullFace();
        }
        render.RenderMultiTextureMesh(shape.MeshRef, textureSampleName);
        if (!itemStackRenderInfo.CullFaces)
        {
            render.GlEnableCullFace();
        }

        shaderProgram.Uniform("damageEffect", 0f);
        shaderProgram.Stop();
    }

    private static void SpawnParticles(Matrixf itemModelMat, ItemStack itemStack, float dt, ref float timeAccumulation, ICoreClientAPI api, Entity entity)
    {
        if (itemStack.Collectible?.ParticleProperties == null) return;

        float windStrength = Math.Max(0f, 1f - api.World.BlockAccessor.GetDistanceToRainFall(entity.Pos.AsBlockPos) / 5f);
        AdvancedParticleProperties[] particleProperties = itemStack.Collectible.ParticleProperties;
        if (itemStack.Collectible == null || api.IsGamePaused)
        {
            return;
        }

        EntityPlayer entityPlayer = api.World.Player.Entity;

        Vec4f vec4f = itemModelMat.TransformVector(new Vec4f(itemStack.Collectible.TopMiddlePos.X, itemStack.Collectible.TopMiddlePos.Y, itemStack.Collectible.TopMiddlePos.Z, 1f));
        timeAccumulation += dt;
        if (particleProperties != null && particleProperties.Length != 0 && timeAccumulation > 0.05f)
        {
            timeAccumulation %= 0.025f;
            foreach (AdvancedParticleProperties advancedParticleProperties in particleProperties)
            {
                advancedParticleProperties.WindAffectednesAtPos = windStrength;
                advancedParticleProperties.WindAffectednes = windStrength;
                advancedParticleProperties.basePos.X = vec4f.X + entity.Pos.X + (0.0 - (entity.Pos.X - entityPlayer.CameraPos.X));
                advancedParticleProperties.basePos.Y = vec4f.Y + entity.Pos.Y + (0.0 - (entity.Pos.Y - entityPlayer.CameraPos.Y));
                advancedParticleProperties.basePos.Z = vec4f.Z + entity.Pos.Z + (0.0 - (entity.Pos.Z - entityPlayer.CameraPos.Z));
                entity.World.SpawnParticles(advancedParticleProperties);
            }
        }
    }

    private static void ZeroTransformCorrection(float[] elementTransforms)
    {
        bool zeroTransform = elementTransforms.Count(value => value == 0) == elementTransforms.Length;
        if (zeroTransform)
        {
            for (int i = 0; i < elementTransforms.Length; i += 4)
            {
                if (elementTransforms[i] == 0)
                {
                    elementTransforms[i] = 1;
                }
            }
        }
    }

    private static void FillShaderValues(IShaderProgram shaderProgram, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Vec4f lightrgbs, Matrixf itemModelMatrix, IWorldAccessor world, AnimatorBase animator)
    {
        FillShaderValues(shaderProgram, itemStackRenderInfo, render, itemStack, lightrgbs, itemModelMatrix, world);

        float[] elementTransforms = animator.TransformationMatrices4x3;

        ZeroTransformCorrection(elementTransforms);

        shaderProgram.UniformMatrices4x3(
            "elementTransforms",
            GlobalConstants.MaxAnimatedElements,
            elementTransforms
        );
    }
    private static void FillShaderValues(IShaderProgram shaderProgram, ItemRenderInfo itemStackRenderInfo, IRenderAPI render, ItemStack itemStack, Vec4f lightrgbs, Matrixf itemModelMatrix, IWorldAccessor world)
    {
        shaderProgram.Uniform("dontWarpVertices", 0);
        shaderProgram.Uniform("addRenderFlags", 0);
        shaderProgram.Uniform("normalShaded", 1);
        shaderProgram.Uniform("rgbaTint", ColorUtil.WhiteArgbVec);
        shaderProgram.Uniform("alphaTest", itemStackRenderInfo.AlphaTest);
        shaderProgram.Uniform("damageEffect", itemStackRenderInfo.DamageEffect);
        shaderProgram.Uniform("overlayOpacity", itemStackRenderInfo.OverlayOpacity);
        if (itemStackRenderInfo.OverlayTexture != null && itemStackRenderInfo.OverlayOpacity > 0f)
        {
            shaderProgram.Uniform("tex2dOverlay", itemStackRenderInfo.OverlayTexture.TextureId);
            shaderProgram.Uniform("overlayTextureSize", new Vec2f(itemStackRenderInfo.OverlayTexture.Width, itemStackRenderInfo.OverlayTexture.Height));
            shaderProgram.Uniform("baseTextureSize", new Vec2f(itemStackRenderInfo.TextureSize.Width, itemStackRenderInfo.TextureSize.Height));
            TextureAtlasPosition textureAtlasPosition = render.GetTextureAtlasPosition(itemStack);
            shaderProgram.Uniform("baseUvOrigin", new Vec2f(textureAtlasPosition.x1, textureAtlasPosition.y1));
        }

        int num = (int)itemStack.Collectible.GetTemperature(world, itemStack);
        float[] incandescenceColorAsColor4f = ColorUtil.GetIncandescenceColorAsColor4f(num);
        int num2 = GameMath.Clamp((num - 500) / 3, 0, 255);
        shaderProgram.Uniform("extraGlow", num2);
        shaderProgram.Uniform("rgbaAmbientIn", render.AmbientColor);
        shaderProgram.Uniform("rgbaLightIn", lightrgbs);
        shaderProgram.Uniform("rgbaGlowIn", new Vec4f(incandescenceColorAsColor4f[0], incandescenceColorAsColor4f[1], incandescenceColorAsColor4f[2], num2 / 255f));
        shaderProgram.Uniform("rgbaFogIn", render.FogColor);
        shaderProgram.Uniform("fogMinIn", render.FogMin);
        shaderProgram.Uniform("fogDensityIn", render.FogDensity);
        shaderProgram.Uniform("normalShaded", itemStackRenderInfo.NormalShaded ? 1 : 0);
        shaderProgram.UniformMatrix("projectionMatrix", render.CurrentProjectionMatrix);
        shaderProgram.UniformMatrix("viewMatrix", render.CameraMatrixOriginf);
        shaderProgram.UniformMatrix("modelMatrix", itemModelMatrix.Values);
        shaderProgram.Uniform("depthOffset", GetDepthOffset(world));
    }
    private static float GetDepthOffset(IWorldAccessor world)
    {
        return (world.Api as ICoreClientAPI)?.Settings.Bool["immersiveFpMode"] ?? false ? 0.0f : -0.3f;
    }
}