using AnimationManagerLib.API;
using AnimationManagerLib.CollectibleBehaviors;
using AnimationManagerLib.Integration;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using static OpenTK.Graphics.OpenGL.GL;

namespace AnimationManagerLib.Patches;

internal static class AnimatorPatch
{
    public static void Patch(string harmonyId, AnimationManager manager, ICoreClientAPI api)
    {
        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimatorPatch), nameof(RenderHeldItem)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityPlayer).GetMethod("updateEyeHeight", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(EyeHightController), nameof(EyeHightController.UpdateEyeHeight)))
            );

        new Harmony(harmonyId).Patch(
                typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(AnimatorPatch), nameof(RenderColliders)))
            );
        new Harmony(harmonyId).Patch(
                typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all),
                postfix: new HarmonyMethod(AccessTools.Method(typeof(AnimatorPatch), nameof(RenderCollidersPlayer)))
            );

        new Harmony(harmonyId).Patch(
                typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimatorPatch), nameof(AnimatorPatch.ReplaceAnimator)))
            );

        /*new Harmony(harmonyId).Patch(
                typeof(Vintagestory.API.Common.PlayerAnimationManager).GetMethod("OnClientFrame", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimatorPatch), nameof(AnimatorPatch.ReplacePlayerAnimator)))
            );*/

        _manager = manager;
        _coreClientAPI = api;
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetMethod("updateEyeHeight", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(Vintagestory.API.Common.AnimationManager).GetMethod("OnClientFrame", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);

        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Postfix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayerShapeRenderer).GetMethod("DoRender3DOpaque", AccessTools.all), HarmonyPatchType.Postfix, harmonyId);
    }

    private static AnimationManager? _manager;
    private static ICoreClientAPI? _coreClientAPI;
    private static readonly FieldInfo? _animManager = typeof(Vintagestory.API.Common.EntityPlayer).GetField("animManager", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _selfFpAnimManager = typeof(Vintagestory.API.Common.EntityPlayer).GetField("selfFpAnimManager", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _entity = typeof(Vintagestory.API.Common.AnimationManager).GetField("entity", BindingFlags.NonPublic | BindingFlags.Instance);

    private static bool RenderHeldItem(EntityShapeRenderer __instance, float dt, bool isShadowPass, bool right)
    {
        if (isShadowPass) return true;

        ItemSlot? slot;

        if (right)
        {
            slot = (__instance.entity as EntityPlayer)?.RightHandItemSlot;
        }
        else
        {
            slot = (__instance.entity as EntityPlayer)?.LeftHandItemSlot;
        }

        if (slot?.Itemstack?.Item == null) return true;

        Animatable? behavior = slot?.Itemstack?.Item?.GetBehavior<AnimatableProcedural>()
            ?? slot?.Itemstack?.Item?.GetBehavior<AnimatableAttachable>()
            ?? slot?.Itemstack?.Item?.GetBehavior<Animatable>();

        if (behavior == null) return true;

        ItemRenderInfo renderInfo = __instance.capi.Render.GetItemStackRenderInfo(slot, EnumItemRenderTarget.HandTp, dt);

        behavior.BeforeRender(__instance.capi, slot.Itemstack, __instance.entity, EnumItemRenderTarget.HandFp, dt);

        (string textureName, _) = slot.Itemstack.Item.Textures.First();

        TextureAtlasPosition atlasPos = __instance.capi.ItemTextureAtlas.GetPosition(slot.Itemstack.Item, textureName);

        renderInfo.TextureId = atlasPos.atlasTextureId;

        Vec4f? lightrgbs = (Vec4f?)typeof(EntityShapeRenderer)
                                          .GetField("lightrgbs", BindingFlags.NonPublic | BindingFlags.Instance)
                                          ?.GetValue(__instance);

        behavior.RenderHeldItem(__instance.ModelMat, __instance.capi, slot, __instance.entity, lightrgbs, dt, isShadowPass, right, renderInfo);

        return false;
    }

    private static void ReplaceAnimator(Vintagestory.API.Common.AnimationManager __instance, float dt)
    {
        EntityAgent? entity = (Entity?)_entity?.GetValue(__instance) as EntityAgent;

        _manager?.OnFrameHandler(__instance, entity, dt);

        ClientAnimator? animator = __instance.Animator as ClientAnimator;
        if (__instance.Animator is not ProceduralClientAnimator && animator != null && _manager != null)
        {
            if (entity != null)
            {
                __instance.Animator = ProceduralClientAnimator.Create(_manager, __instance, animator, entity);
            }
        }
    }
    private static void ReplacePlayerAnimator(Vintagestory.API.Common.PlayerAnimationManager __instance, float dt)
    {
        EntityAgent? entity = (Entity?)_entity?.GetValue(__instance) as EntityAgent;

        _manager?.OnFrameHandler(__instance, entity, dt);

        ClientAnimator? animator = __instance.Animator as ClientAnimator;
        if (__instance.Animator is not ProceduralClientAnimator && animator != null && _manager != null)
        {
            if (entity != null)
            {
                __instance.Animator = ProceduralClientAnimator.Create(_manager, __instance, animator, entity);
            }
        }
    }

    private static void RenderColliders(EntityShapeRenderer __instance)
    {
#if DEBUG
        IShaderProgram? currentShader = _coreClientAPI?.Render.CurrentActiveShader;
        currentShader?.Stop();
#endif

        __instance.entity?.GetBehavior<CollidersEntityBehavior>()?.Render(_coreClientAPI, __instance.entity as EntityAgent, __instance);


#if DEBUG
        currentShader?.Use();
#endif
    }
    private static void RenderCollidersPlayer(EntityPlayerShapeRenderer __instance)
    {
#if DEBUG
        IShaderProgram? currentShader = _coreClientAPI?.Render.CurrentActiveShader;
        currentShader?.Stop();
#endif

        __instance.entity?.GetBehavior<CollidersEntityBehavior>()?.Render(_coreClientAPI, __instance.entity as EntityAgent, __instance);

#if DEBUG
        currentShader?.Use();
#endif
    }
}
