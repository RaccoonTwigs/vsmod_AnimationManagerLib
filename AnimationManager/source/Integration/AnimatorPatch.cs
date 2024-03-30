﻿using AnimationManagerLib.CollectibleBehaviors;
using AnimationManagerLib.Integration;
using HarmonyLib;
using System;
using System.Linq;
using System.Reflection;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

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
                typeof(EntityPlayer).GetMethod("Initialize", AccessTools.all),
                prefix: new HarmonyMethod(AccessTools.Method(typeof(AnimatorPatch), nameof(ReplaceAnimationManagers)))
            );

        _manager = manager;
        _coreClientAPI = api;
    }

    public static void Unpatch(string harmonyId)
    {
        new Harmony(harmonyId).Unpatch(typeof(EntityShapeRenderer).GetMethod("RenderHeldItem", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetMethod("updateEyeHeight", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
        new Harmony(harmonyId).Unpatch(typeof(EntityPlayer).GetMethod("Initialize", AccessTools.all), HarmonyPatchType.Prefix, harmonyId);
    }

    private static AnimationManager? _manager;
    private static ICoreClientAPI? _coreClientAPI;
    private static readonly FieldInfo? _animManager = typeof(Vintagestory.API.Common.EntityPlayer).GetField("animManager", BindingFlags.NonPublic | BindingFlags.Instance);
    private static readonly FieldInfo? _selfFpAnimManager = typeof(Vintagestory.API.Common.EntityPlayer).GetField("selfFpAnimManager", BindingFlags.NonPublic | BindingFlags.Instance);

    private static bool RenderHeldItem(EntityShapeRenderer __instance, float dt, bool isShadowPass, bool right)
    {
        if (isShadowPass || !right) return true;

        ItemSlot? slot = (__instance.entity as EntityPlayer)?.RightHandItemSlot;

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

    private static void ReplaceAnimationManagers(EntityPlayer __instance)
    {
        try
        {
            Vintagestory.API.Common.AnimationManager animManager = (Vintagestory.API.Common.AnimationManager)_animManager.GetValue(__instance);
            ProceduralAnimationManager animManagerReplacer = new(_manager, _coreClientAPI, animManager)
            {
                UseFpAnmations = false
            };
            _animManager.SetValue(__instance, animManagerReplacer);

            Vintagestory.API.Common.AnimationManager selfFpAnimManager = (Vintagestory.API.Common.AnimationManager)_selfFpAnimManager.GetValue(__instance);
            ProceduralAnimationManager selfFpAnimManagerReplacer = new(_manager, _coreClientAPI, selfFpAnimManager);
            _selfFpAnimManager.SetValue(__instance, selfFpAnimManagerReplacer);
        }
        catch (Exception exception)
        {
            _coreClientAPI?.Logger.Error($"[Animation Manager lib] Error on replacing animation managers and animators for EntityPlayer.");
            _coreClientAPI?.Logger.VerboseDebug($"[Animation Manager lib] Error on replacing animation managers and animators for EntityPlayer.\nException: {exception}\n");
        }
    }
}
