using System;
using System.Collections.Generic;
using System.Numerics;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;
using VSImGui.Debug;

namespace AnimationManagerLib.Integration;

public sealed class ShapeElementCollider
{
    public Vector4[] ElementVertices { get; } = new Vector4[8];
    public Vector4[] InworldVertices { get; } = new Vector4[8];
    public float[] ElementMatrix { get; private set; } = new float[16];
    

    public ShapeElement ForElement { get; private set; }

    public EntityAgent? Entity { get; set; } = null;
    public EntityShapeRenderer? Renderer { get; set; } = null;

    public ShapeElementCollider(ShapeElement element)
    {
        ForElement = element;
        SetElementVertices();
    }

    public void SetElementVertices()
    {
        Vector4 from = new((float)ForElement.From[0], (float)ForElement.From[1], (float)ForElement.From[2], 1);
        Vector4 to = new((float)ForElement.To[0], (float)ForElement.To[1], (float)ForElement.To[2], 1);
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
        if (ForElement.ParentElement != null) GetElementTransformMatrix(temporaryMatrix, ForElement.ParentElement);

        temporaryMatrix
            .Translate(ForElement.RotationOrigin[0], ForElement.RotationOrigin[1], ForElement.RotationOrigin[2])
            .RotateX((float)ForElement.RotationX * GameMath.DEG2RAD)
            .RotateY((float)ForElement.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)ForElement.RotationZ * GameMath.DEG2RAD)
            .Translate(0f - ForElement.RotationOrigin[0], 0f - ForElement.RotationOrigin[1], 0f - ForElement.RotationOrigin[2]);

        ElementMatrix = temporaryMatrix.Values;
    }
    public static Vec4f Transform(Matrixf matrix, Vec4f vector, float scale)
    {
        Vec4f crutch = new();
        Mat4f.MulWithVec4(matrix.Values, vector, crutch);
        return crutch * scale;
    }

    public static string PrintVector(Vec4f vector) => $"({vector.X}, {vector.Y}, {vector.Z})";

    public void GetElementTransformMatrix(Matrixf matrix, ShapeElement element)
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

    private int? GetIndex(int jointId, int matrixElementIndex)
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

    private float[] GetTransformMatrix(int jointId, float[] TransformationMatrices4x3)
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

    public void GetElementTransformMatrixA(Matrixf matrix, ShapeElement element, float[] TransformationMatrices4x3)
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

    public static void Add(Vec4f to, float x, float y, float z, float factor)
    {
        to.X = to.X + x * factor;
        to.Y = to.Y + y * factor;
        to.Z = to.Z + z * factor;
    }

    public void Transform(float[] transformMatrix4x3)
    {
        if (Renderer == null) return;

        float[] transformMatrix = GetTransformMatrix(ForElement.JointId, transformMatrix4x3);
        Vector4 offset = new(transformMatrix[12], transformMatrix[13], transformMatrix[14], 0);
        Vector4 fullModelOffset = new(-0.5f, 0, -0.5f, 0);

        for (int vertex = 0; vertex < 8; vertex++)
        {
            InworldVertices[vertex] = ElementVertices[vertex] / 16f;
            InworldVertices[vertex] = MultiplyVectorByMatrix(ElementMatrix, InworldVertices[vertex]);
            InworldVertices[vertex] = MultiplyVectorByMatrix(transformMatrix, InworldVertices[vertex]);
            InworldVertices[vertex] += offset + fullModelOffset;
            InworldVertices[vertex] = MultiplyVectorByMatrix(Renderer.ModelMat, InworldVertices[vertex]);
        }
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
    public static Vector4 MultiplyVectorByMatrix(float[] matrix, Vector4 vector)
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

}

public sealed class CollidersEntityBehavior : EntityBehavior
{
    public CollidersEntityBehavior(Entity entity) : base(entity)
    {
        Console.WriteLine("****************************************************************");
        Console.WriteLine($"CollidersEntityBehavior created for: {(entity as EntityPlayer)?.GetName()}");
    }

    public override void Initialize(EntityProperties properties, JsonObject attributes)
    {
        Console.WriteLine($"CollidersEntityBehavior Initialize: {attributes}");

        if (attributes.KeyExists("colliderShapeElements"))
        {
            HasOBBCollider = true;
            ShapeElementsToProcess = new(attributes["colliderShapeElements"].AsArray(Array.Empty<string>()));
            UnprocessedElementsLeft = true;
        }

        Console.WriteLine("****************************************************************");
    }

    public bool HasOBBCollider { get; private set; } = false;
    public bool UnprocessedElementsLeft { get; set; } = false;
    public HashSet<string> ShapeElementsToProcess { get; private set; } = new();
    public Dictionary<string, ShapeElementCollider> Colliders { get; private set; } = new();
    public override string PropertyName() => "animationmanagerlib:colliders";

#if DEBUG
    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, EntityShapeRenderer renderer, int color = ColorUtil.WhiteArgb)
    {
        /*if (!HasOBBCollider) return;
        
        foreach (ShapeElementCollider collider in Colliders.Values)
        {
            collider.Renderer = renderer;
            collider.Entity = entityPlayer;
            collider.SetElementVertices();
            collider.Render(api, entityPlayer, color);
        }*/
    }
    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, EntityPlayerShapeRenderer renderer, int color = ColorUtil.WhiteArgb)
    {
        if (!HasOBBCollider) return;

        foreach (ShapeElementCollider collider in Colliders.Values)
        {
            collider.Renderer = renderer;
            collider.Entity = entityPlayer;
            collider.SetElementVertices();
            collider.Render(api, entityPlayer, color);
        }
    }
#endif
}
