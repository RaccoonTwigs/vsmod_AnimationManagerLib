using System;
using System.Collections.Generic;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace AnimationManagerLib.Integration;

public sealed class ShapeElementCollider
{
    public Vec4f Vertex0 { get; private set; } = new();
    public Vec4f Vertex1 { get; private set; } = new();
    public Vec4f Vertex2 { get; private set; } = new();
    public Vec4f Vertex3 { get; private set; } = new();
    public Vec4f Vertex4 { get; private set; } = new();
    public Vec4f Vertex5 { get; private set; } = new();
    public Vec4f Vertex6 { get; private set; } = new();
    public Vec4f Vertex7 { get; private set; } = new();

    public Vec4f PreVertex0 { get; private set; } = new();
    public Vec4f PreVertex1 { get; private set; } = new();
    public Vec4f PreVertex2 { get; private set; } = new();
    public Vec4f PreVertex3 { get; private set; } = new();
    public Vec4f PreVertex4 { get; private set; } = new();
    public Vec4f PreVertex5 { get; private set; } = new();
    public Vec4f PreVertex6 { get; private set; } = new();
    public Vec4f PreVertex7 { get; private set; } = new();

    public Vec4f Origin { get; private set; } = new();
    public int JointId { get; private set; } = new();

    public ShapeElement ForElement { get; private set; }

    public EntityAgent? Entity { get; set; } = null;
    public EntityShapeRenderer? Renderer { get; set; } = null;

    public ShapeElementCollider(ShapeElement element)
    {
        ForElement = element;
        JointId = element.JointId;
        SetElementVertices();
    }

    public void SetElementVertices()
    {
        Vec4f from = new((float)ForElement.From[0], (float)ForElement.From[1], (float)ForElement.From[2], 1);
        Vec4f to = new((float)ForElement.To[0], (float)ForElement.To[1], (float)ForElement.To[2], 1);
        Vec4f diagonal = to - from;
        Vec4f origin = new((float)ForElement.RotationOrigin[0], (float)ForElement.RotationOrigin[1], (float)ForElement.RotationOrigin[2], 1);

        PreVertex0 = from;
        PreVertex7 = to;
        PreVertex1 = new(from.X + diagonal.X, from.Y, from.Z, from.W);
        PreVertex2 = new(from.X, from.Y + diagonal.Y, from.Z, from.W);
        PreVertex3 = new(from.X, from.Y, from.Z + diagonal.Z, from.W);
        PreVertex4 = new(from.X + diagonal.X, from.Y + diagonal.Y, from.Z, from.W);
        PreVertex5 = new(from.X, from.Y + diagonal.Y, from.Z + diagonal.Z, from.W);
        PreVertex6 = new(from.X + diagonal.X, from.Y, from.Z + diagonal.Z, from.W);

        Matrixf elementMatrix = new Matrixf().Identity();
        if (ForElement.ParentElement != null) GetElementTransformMatrix(elementMatrix, ForElement.ParentElement);

        elementMatrix
            .Translate(ForElement.RotationOrigin[0], ForElement.RotationOrigin[1], ForElement.RotationOrigin[2])
            .RotateX((float)ForElement.RotationX * GameMath.DEG2RAD)
            .RotateY((float)ForElement.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)ForElement.RotationZ * GameMath.DEG2RAD)
            .Translate(0f - ForElement.RotationOrigin[0], 0f - ForElement.RotationOrigin[1], 0f - ForElement.RotationOrigin[2]);

        PreVertex0 = Transform(elementMatrix, PreVertex0, 1 / 16f);
        PreVertex1 = Transform(elementMatrix, PreVertex1, 1 / 16f);
        PreVertex2 = Transform(elementMatrix, PreVertex2, 1 / 16f);
        PreVertex3 = Transform(elementMatrix, PreVertex3, 1 / 16f);
        PreVertex4 = Transform(elementMatrix, PreVertex4, 1 / 16f);
        PreVertex5 = Transform(elementMatrix, PreVertex5, 1 / 16f);
        PreVertex6 = Transform(elementMatrix, PreVertex6, 1 / 16f);
        PreVertex7 = Transform(elementMatrix, PreVertex7, 1 / 16f);
        Origin = Transform(elementMatrix, origin, 1 / 16f);
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

    /*
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
     */

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
            int? transformMatricesIndex = GetIndex(JointId, elementIndex);
            if (transformMatricesIndex != null)
            {
                transformMatrix[elementIndex] = TransformationMatrices4x3[transformMatricesIndex.Value];
            }
        }
        return transformMatrix;
    }

    private List<int> GetJointIds()
    {
        List<ShapeElement> parents = ForElement.GetParentPath();
        List<int> jointIds = new() { JointId };

        foreach (ShapeElement parent in parents)
        {
            if (jointIds[^1] != parent.JointId)
            {
                jointIds.Add(parent.JointId);
            }
        }

        return jointIds;
    }

    private float[] GetTransfromMatricesFromParents(float[] TransformationMatrices4x3)
    {
        float[] transformMatrix = new float[16];
        Mat4f.Identity(transformMatrix);

        List<int> jointIds = GetJointIds();

        foreach (int jointId in jointIds)
        {
            float[] parentTransform = GetTransformMatrix(jointId, TransformationMatrices4x3);
            Mat4f.Mul(transformMatrix, parentTransform, transformMatrix);
        }

        return transformMatrix;
    }

    public void TransformByJoint(float[] TransformationMatrices4x3)
    {
        float[] transformMatrix = GetTransformMatrix(JointId, TransformationMatrices4x3);
        Vec4f zeroVector = new(0, 0, 0, 0);
        float[] mm = new float[16];
        Mat4f.Identity(mm);
        //Mat4f.Mul(mm, Renderer.ModelMat, transformMatrix);
        //mm = Renderer.ModelMat;
        //mm = transformMatrix;

        mm = transformMatrix;

        TransformVector(PreVertex0, Vertex0, mm, Origin);
        TransformVector(PreVertex1, Vertex1, mm, Origin);
        TransformVector(PreVertex2, Vertex2, mm, Origin);
        TransformVector(PreVertex3, Vertex3, mm, Origin);
        TransformVector(PreVertex4, Vertex4, mm, Origin);
        TransformVector(PreVertex5, Vertex5, mm, Origin);
        TransformVector(PreVertex6, Vertex6, mm, Origin);
        TransformVector(PreVertex7, Vertex7, mm, Origin);

        mm = Renderer.ModelMat;

        TransformVector(Vertex0, Vertex0, mm, zeroVector);
        TransformVector(Vertex1, Vertex1, mm, zeroVector);
        TransformVector(Vertex2, Vertex2, mm, zeroVector);
        TransformVector(Vertex3, Vertex3, mm, zeroVector);
        TransformVector(Vertex4, Vertex4, mm, zeroVector);
        TransformVector(Vertex5, Vertex5, mm, zeroVector);
        TransformVector(Vertex6, Vertex6, mm, zeroVector);
        TransformVector(Vertex7, Vertex7, mm, zeroVector);
    }

    public void Transform(float[] matrix)
    {
        if (Renderer == null) return;

        float[] mm = new float[16];

        Mat4f.Identity(mm);

        Mat4f.Mul(mm, matrix, Renderer.ModelMat);
        //mm = Renderer.ModelMat;
        //mm = matrix;


        TransformVector(PreVertex0, Vertex0, mm, Origin);
        TransformVector(PreVertex1, Vertex1, mm, Origin);
        TransformVector(PreVertex2, Vertex2, mm, Origin);
        TransformVector(PreVertex3, Vertex3, mm, Origin);
        TransformVector(PreVertex4, Vertex4, mm, Origin);
        TransformVector(PreVertex5, Vertex5, mm, Origin);
        TransformVector(PreVertex6, Vertex6, mm, Origin);
        TransformVector(PreVertex7, Vertex7, mm, Origin);
    }

#if DEBUG
    public void Render(ICoreClientAPI api, EntityAgent entityPlayer, int color = ColorUtil.WhiteArgb)
    {
        BlockPos playerPos = entityPlayer.Pos.AsBlockPos;
        EntityPos entityPos = entityPlayer.Pos;
        Vec3f deltaPos = entityPos.XYZFloat - new Vec3f(playerPos.X, playerPos.Y, playerPos.Z);

        //RenderLine(api, Vertex0, Vertex7, playerPos, deltaPos, ColorUtil.ToRgba(255, 0, 255, 255));

        RenderLine(api, Vertex0, Vertex1, playerPos, deltaPos, ColorUtil.ToRgba(255, 0, 0, 255));
        RenderLine(api, Vertex0, Vertex2, playerPos, deltaPos, ColorUtil.ToRgba(255, 0, 255, 0));
        RenderLine(api, Vertex0, Vertex3, playerPos, deltaPos, ColorUtil.ToRgba(255, 255, 0, 0));

        RenderLine(api, Vertex4, Vertex7, playerPos, deltaPos, color);
        RenderLine(api, Vertex5, Vertex7, playerPos, deltaPos, color);
        RenderLine(api, Vertex6, Vertex7, playerPos, deltaPos, color);

        RenderLine(api, Vertex1, Vertex4, playerPos, deltaPos, color);
        RenderLine(api, Vertex2, Vertex5, playerPos, deltaPos, color);
        RenderLine(api, Vertex3, Vertex6, playerPos, deltaPos, color);

        RenderLine(api, Vertex2, Vertex4, playerPos, deltaPos, color);
        RenderLine(api, Vertex3, Vertex5, playerPos, deltaPos, color);
        RenderLine(api, Vertex1, Vertex6, playerPos, deltaPos, color);
    }

    private static void RenderLine(ICoreClientAPI api, Vec4f start, Vec4f end, BlockPos playerPos, Vec3f deltaPos, int color)
    {
        api.Render.RenderLine(playerPos, start.X + deltaPos.X, start.Y + deltaPos.Y, start.Z + deltaPos.Z, end.X + deltaPos.X, end.Y + deltaPos.Y, end.Z + deltaPos.Z, color);
    }
#endif
    private void TransformVector(Vec4f input, Vec4f output, float[] modelMatrix, Vec4f origin) //, EntityPos playerPos)
    {
        Vec4f interm = new(
            input.X - origin.X,
            input.Y - origin.Y,
            input.Z - origin.Z,
            input.W);

        Mat4f.MulWithVec4(modelMatrix, interm, output);

        output.X = output.X + origin.X;
        output.Y = output.Y + origin.Y;
        output.Z = output.Z + origin.Z;
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
