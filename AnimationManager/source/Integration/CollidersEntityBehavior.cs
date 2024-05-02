using System;
using System.Collections.Generic;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AnimationManagerLib.Integration;

public sealed class ShapeElementCollider
{
    public const int VertexCount = 8;
    public Vector4[] ElementVertices { get; } = new Vector4[VertexCount];
    public Vector4[] InworldVertices { get; } = new Vector4[VertexCount];
    public float[] ElementMatrix { get; private set; } = new float[16];
    public int JointId { get; private set; }

    public EntityShapeRenderer? Renderer { get; set; } = null;

    public ShapeElementCollider(ShapeElement element)
    {
        JointId = element.JointId;
        SetElementVertices(element);
    }

    public void Transform(float[] transformMatrix4x3)
    {
        if (Renderer == null) return;

        float[] transformMatrix = GetTransformMatrix(JointId, transformMatrix4x3);
        Vector4 offset = new(transformMatrix[12], transformMatrix[13], transformMatrix[14], 0);
        Vector4 fullModelOffset = new(-0.5f, 0, -0.5f, 0);

        for (int vertex = 0; vertex < VertexCount; vertex++)
        {
            InworldVertices[vertex] = ElementVertices[vertex] / 16f;
            InworldVertices[vertex] = MultiplyVectorByMatrix(ElementMatrix, InworldVertices[vertex]);
            InworldVertices[vertex] = MultiplyVectorByMatrix(transformMatrix, InworldVertices[vertex]);
            InworldVertices[vertex] += offset + fullModelOffset;
            InworldVertices[vertex] = MultiplyVectorByMatrix(Renderer.ModelMat, InworldVertices[vertex]);
        }
    }

    private void SetElementVertices(ShapeElement element)
    {
        Vector4 from = new((float)element.From[0], (float)element.From[1], (float)element.From[2], 1);
        Vector4 to = new((float)element.To[0], (float)element.To[1], (float)element.To[2], 1);
        Vector4 diagonal = to - from;

        ElementVertices[0] = from;
        ElementVertices[7] = to;
        ElementVertices[1] = new(from.X + diagonal.X, from.Y, from.Z, from.W);
        ElementVertices[2] = new(from.X, from.Y + diagonal.Y, from.Z, from.W);
        ElementVertices[3] = new(from.X, from.Y, from.Z + diagonal.Z, from.W);
        ElementVertices[4] = new(from.X + diagonal.X, from.Y + diagonal.Y, from.Z, from.W);
        ElementVertices[5] = new(from.X, from.Y + diagonal.Y, from.Z + diagonal.Z, from.W);
        ElementVertices[6] = new(from.X + diagonal.X, from.Y, from.Z + diagonal.Z, from.W);

        Mat4f.Identity(ElementMatrix);

        Matrixf temporaryMatrix = new(ElementMatrix);
        if (element.ParentElement != null) GetElementTransformMatrix(temporaryMatrix, element.ParentElement);

        temporaryMatrix
            .Translate(element.RotationOrigin[0], element.RotationOrigin[1], element.RotationOrigin[2])
            .RotateX((float)element.RotationX * GameMath.DEG2RAD)
            .RotateY((float)element.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)element.RotationZ * GameMath.DEG2RAD)
            .Translate(0f - element.RotationOrigin[0], 0f - element.RotationOrigin[1], 0f - element.RotationOrigin[2]);

        ElementMatrix = temporaryMatrix.Values;
    }

    private static void GetElementTransformMatrix(Matrixf matrix, ShapeElement element)
    {
        if (element.ParentElement != null)
        {
            GetElementTransformMatrix(matrix, element.ParentElement);
        }

        matrix
            .Translate(element.RotationOrigin[0], element.RotationOrigin[1], element.RotationOrigin[2])
            .RotateX((float)element.RotationX * GameMath.DEG2RAD)
            .RotateY((float)element.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)element.RotationZ * GameMath.DEG2RAD)
            .Translate(0f - element.RotationOrigin[0], 0f - element.RotationOrigin[1], 0f - element.RotationOrigin[2])
            .Translate(element.From[0], element.From[1], element.From[2]);
    }
    private static int? GetIndex(int jointId, int matrixElementIndex)
    {
        int index = 12 * jointId;
        int offset = matrixElementIndex switch
        {
            0 => 0,
            1 => 1,
            2 => 2,
            4 => 3,
            5 => 4,
            6 => 5,
            8 => 6,
            9 => 7,
            10 => 8,
            12 => 9,
            13 => 10,
            14 => 11,
            _ => -1
        };

        if (offset < 0) return null;

        return index + offset;
    }
    private static float[] GetTransformMatrix(int jointId, float[] TransformationMatrices4x3)
    {
        float[] transformMatrix = new float[16];
        Mat4f.Identity(transformMatrix);
        for (int elementIndex = 0; elementIndex < 16; elementIndex++)
        {
            int? transformMatricesIndex = GetIndex(jointId, elementIndex);
            if (transformMatricesIndex != null)
            {
                transformMatrix[elementIndex] = TransformationMatrices4x3[transformMatricesIndex.Value];
            }
        }
        return transformMatrix;
    }
    private static void GetElementTransformMatrixA(Matrixf matrix, ShapeElement element, float[] TransformationMatrices4x3)
    {
        if (element.ParentElement != null)
        {
            GetElementTransformMatrixA(matrix, element.ParentElement, TransformationMatrices4x3);
        }

        matrix
            .Translate(element.RotationOrigin[0], element.RotationOrigin[1], element.RotationOrigin[2])
            .RotateX((float)element.RotationX * GameMath.DEG2RAD)
            .RotateY((float)element.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)element.RotationZ * GameMath.DEG2RAD)
            .Translate(0f - element.RotationOrigin[0], 0f - element.RotationOrigin[1], 0f - element.RotationOrigin[2])
            .Translate(element.From[0], element.From[1], element.From[2]);
    }
    private static Vector4 MultiplyVectorByMatrix(float[] matrix, Vector4 vector)
    {
        Vector4 result = new(0, 0, 0, 0);
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < 4; j++)
            {
                result[i] += matrix[4 * j + i] * vector[j];
            }
        }
        return result;
    }


#if DEBUG
    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, int color = ColorUtil.WhiteArgb)
    {
        BlockPos playerPos = entityPlayer.Pos.AsBlockPos;
        EntityPos entityPos = entityPlayer.Pos;
        Vec3f deltaPos = entityPos.XYZFloat - new Vec3f(playerPos.X, playerPos.Y, playerPos.Z);

        RenderLine(api, InworldVertices[0], InworldVertices[1], playerPos, deltaPos, ColorUtil.ToRgba(255, 0, 0, 255));
        RenderLine(api, InworldVertices[0], InworldVertices[2], playerPos, deltaPos, ColorUtil.ToRgba(255, 0, 255, 0));
        RenderLine(api, InworldVertices[0], InworldVertices[3], playerPos, deltaPos, ColorUtil.ToRgba(255, 255, 0, 0));

        RenderLine(api, InworldVertices[4], InworldVertices[7], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[5], InworldVertices[7], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[6], InworldVertices[7], playerPos, deltaPos, color);

        RenderLine(api, InworldVertices[1], InworldVertices[4], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[2], InworldVertices[5], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[3], InworldVertices[6], playerPos, deltaPos, color);

        RenderLine(api, InworldVertices[2], InworldVertices[4], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[3], InworldVertices[5], playerPos, deltaPos, color);
        RenderLine(api, InworldVertices[1], InworldVertices[6], playerPos, deltaPos, color);
    }

    private static void RenderLine(ICoreClientAPI api, Vector4 start, Vector4 end, BlockPos playerPos, Vec3f deltaPos, int color)
    {
        api.Render.RenderLine(playerPos, start.X + deltaPos.X, start.Y + deltaPos.Y, start.Z + deltaPos.Z, end.X + deltaPos.X, end.Y + deltaPos.Y, end.Z + deltaPos.Z, color);
    }
#endif
}

public sealed class CollidersEntityBehavior : EntityBehavior
{
    public CollidersEntityBehavior(Entity entity) : base(entity)
    {
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        if (attributes.KeyExists("colliderShapeElements"))
        {
            HasOBBCollider = true;
            ShapeElementsToProcess = new(attributes["colliderShapeElements"].AsArray(Array.Empty<string>()));
            UnprocessedElementsLeft = true;
        }
    }

    public bool HasOBBCollider { get; private set; } = false;
    public bool UnprocessedElementsLeft { get; set; } = false;
    public HashSet<string> ShapeElementsToProcess { get; private set; } = new();
    public Dictionary<string, ShapeElementCollider> Colliders { get; private set; } = new();
    public override string PropertyName() => "animationmanagerlib:colliders";

#if DEBUG
    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, EntityShapeRenderer renderer, int color = ColorUtil.WhiteArgb)
    {
        if (!HasOBBCollider) return;

        foreach (ShapeElementCollider collider in Colliders.Values)
        {
            collider.Renderer = renderer;
            collider.Render(api, entityPlayer, color);
        }
    }
#endif
}
