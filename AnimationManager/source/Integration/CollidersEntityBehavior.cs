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

    public Matrixf ElementMatrix { get; set; } = new();

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

        ElementMatrix = new Matrixf().Identity();
        ElementMatrix.Translate(-8, 0, -8);
        if (ForElement.ParentElement != null) GetElementTransformMatrix(ElementMatrix, ForElement.ParentElement);

        Origin = origin;

        ElementMatrix
            .Translate(ForElement.RotationOrigin[0], ForElement.RotationOrigin[1], ForElement.RotationOrigin[2])
            .RotateX((float)ForElement.RotationX * GameMath.DEG2RAD)
            .RotateY((float)ForElement.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)ForElement.RotationZ * GameMath.DEG2RAD)
            .Translate(0f - ForElement.RotationOrigin[0], 0f - ForElement.RotationOrigin[1], 0f - ForElement.RotationOrigin[2]);
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

    private float[] GetTransformMatrix(float[] TransformationMatrices4x3)
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

    private Vector3 GetTransfromMatricesFromParentsAltAlt(float[] TransformationMatrices4x3)
    {
        Vector3 result = new(0, 0, 0);

        List<int> jointIds = GetJointIds();

        foreach (int jointId in jointIds)
        {
            float[] parentTransform = GetTransformMatrix(jointId, TransformationMatrices4x3);
            result.X += parentTransform[12];
            result.Y += parentTransform[13];
            result.Z += parentTransform[14];
        }

        return result;
    }

    private float[] GetTransfromMatricesFromParentsAlt(float[] TransformationMatrices4x3)
    {
        float[] transformMatrix = new float[16];
        Mat4f.Identity(transformMatrix);

        List<ShapeElement> parents = ForElement.GetParentPath();
        foreach (ShapeElement parent in parents)
        {
            float[] local = parent.GetLocalTransformMatrix(1);
            Mat4f.Mul(transformMatrix, local, transformMatrix);

            if (parent.JointId > 0)
            {
                float[] parentTransform = GetTransformMatrix(parent.JointId, TransformationMatrices4x3);
                Mat4f.Mul(transformMatrix, parentTransform, transformMatrix);
            }
        }

        return transformMatrix;
    }

    private float[] GetParentTransfromMatrices(float[] TransformationMatrices4x3)
    {
        float[] transformMatrix = new float[16];
        Mat4f.Identity(transformMatrix);

        List<int> jointIds = GetJointIds();
        jointIds.Remove(0);

        foreach (int jointId in jointIds)
        {
            float[] parentTransform = GetTransformMatrix(jointId, TransformationMatrices4x3);
            Mat4f.Mul(transformMatrix, parentTransform, transformMatrix);
            break;
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

    public void Transform(float[] transformMatrix4x3, float[] localMatrix)
    {
        if (Renderer == null) return;

        float[] transformMatrix = GetTransformMatrix(ForElement.JointId, transformMatrix4x3);

        //DebugWidgets.Text("test", "test", ForElement.Name.GetHashCode(), $"{ForElement.Name}: {transformMatrix[12]:F3}, {transformMatrix[13]:F3}, {transformMatrix[14]:F3}");

        Vec4f zeroVector = new(0, 0, 0, 0);

        float factor1 = 1f;
        float factor2 = 1f / 16f;
        float factor3 = 16;

        Vertex0 = new Vec4f(PreVertex0.X * factor1, PreVertex0.Y * factor1, PreVertex0.Z * factor1, 1 * factor1);
        Vertex1 = new Vec4f(PreVertex1.X * factor1, PreVertex1.Y * factor1, PreVertex1.Z * factor1, 1 * factor1);
        Vertex2 = new Vec4f(PreVertex2.X * factor1, PreVertex2.Y * factor1, PreVertex2.Z * factor1, 1 * factor1);
        Vertex3 = new Vec4f(PreVertex3.X * factor1, PreVertex3.Y * factor1, PreVertex3.Z * factor1, 1 * factor1);
        Vertex4 = new Vec4f(PreVertex4.X * factor1, PreVertex4.Y * factor1, PreVertex4.Z * factor1, 1 * factor1);
        Vertex5 = new Vec4f(PreVertex5.X * factor1, PreVertex5.Y * factor1, PreVertex5.Z * factor1, 1 * factor1);
        Vertex6 = new Vec4f(PreVertex6.X * factor1, PreVertex6.Y * factor1, PreVertex6.Z * factor1, 1 * factor1);
        Vertex7 = new Vec4f(PreVertex7.X * factor1, PreVertex7.Y * factor1, PreVertex7.Z * factor1, 1 * factor1);

        TransformVector(Vertex0, Vertex0, transformMatrix, zeroVector);
        TransformVector(Vertex1, Vertex1, transformMatrix, zeroVector);
        TransformVector(Vertex2, Vertex2, transformMatrix, zeroVector);
        TransformVector(Vertex3, Vertex3, transformMatrix, zeroVector);
        TransformVector(Vertex4, Vertex4, transformMatrix, zeroVector);
        TransformVector(Vertex5, Vertex5, transformMatrix, zeroVector);
        TransformVector(Vertex6, Vertex6, transformMatrix, zeroVector);
        TransformVector(Vertex7, Vertex7, transformMatrix, zeroVector);

        DebugWidgets.Text("test", "test", ForElement.Name.GetHashCode(), $"{ForElement.Name} current ({ForElement.JointId}): {transformMatrix[12]:F3}, {transformMatrix[13]:F3}, {transformMatrix[14]:F3}, factor: {factor3 * Vertex0.W}");

        Add(Vertex0, transformMatrix[12], transformMatrix[13], transformMatrix[14], factor3 * Vertex0.W);
        Add(Vertex1, transformMatrix[12], transformMatrix[13], transformMatrix[14], factor3 * Vertex1.W);
        Add(Vertex2, transformMatrix[12], transformMatrix[13], transformMatrix[14], factor3 * Vertex2.W);
        Add(Vertex3, transformMatrix[12], transformMatrix[13], transformMatrix[14], factor3 * Vertex3.W);
        Add(Vertex4, transformMatrix[12], transformMatrix[13], transformMatrix[14], factor3 * Vertex4.W);
        Add(Vertex5, transformMatrix[12], transformMatrix[13], transformMatrix[14], factor3 * Vertex5.W);
        Add(Vertex6, transformMatrix[12], transformMatrix[13], transformMatrix[14], factor3 * Vertex6.W);
        Add(Vertex7, transformMatrix[12], transformMatrix[13], transformMatrix[14], factor3 * Vertex7.W);

        if (ForElement.ParentElement?.JointId > 0)
        {
            float[] parentTransformMatrix = GetTransfromMatricesFromParents(transformMatrix4x3);

            DebugWidgets.Text("test", "test", ForElement.Name.GetHashCode() + 1, $"{ForElement.Name} parent ({ForElement.ParentElement.JointId}): {parentTransformMatrix[12]:F3}, {parentTransformMatrix[13]:F3}, {parentTransformMatrix[14]:F3}, factor: {-1 * factor3 * Vertex0.W}");

            Add(Vertex0, parentTransformMatrix[12], parentTransformMatrix[13], parentTransformMatrix[14], -1 * factor3 * Vertex0.W);
            Add(Vertex1, parentTransformMatrix[12], parentTransformMatrix[13], parentTransformMatrix[14], -1 * factor3 * Vertex1.W);
            Add(Vertex2, parentTransformMatrix[12], parentTransformMatrix[13], parentTransformMatrix[14], -1 * factor3 * Vertex2.W);
            Add(Vertex3, parentTransformMatrix[12], parentTransformMatrix[13], parentTransformMatrix[14], -1 * factor3 * Vertex3.W);
            Add(Vertex4, parentTransformMatrix[12], parentTransformMatrix[13], parentTransformMatrix[14], -1 * factor3 * Vertex4.W);
            Add(Vertex5, parentTransformMatrix[12], parentTransformMatrix[13], parentTransformMatrix[14], -1 * factor3 * Vertex5.W);
            Add(Vertex6, parentTransformMatrix[12], parentTransformMatrix[13], parentTransformMatrix[14], -1 * factor3 * Vertex6.W);
            Add(Vertex7, parentTransformMatrix[12], parentTransformMatrix[13], parentTransformMatrix[14], -1 * factor3 * Vertex7.W);
        }

        Vertex0 = Transform(ElementMatrix, Vertex0, factor2);
        Vertex1 = Transform(ElementMatrix, Vertex1, factor2);
        Vertex2 = Transform(ElementMatrix, Vertex2, factor2);
        Vertex3 = Transform(ElementMatrix, Vertex3, factor2);
        Vertex4 = Transform(ElementMatrix, Vertex4, factor2);
        Vertex5 = Transform(ElementMatrix, Vertex5, factor2);
        Vertex6 = Transform(ElementMatrix, Vertex6, factor2);
        Vertex7 = Transform(ElementMatrix, Vertex7, factor2);

        TransformVector(Vertex0, Vertex0, Renderer.ModelMat, zeroVector);
        TransformVector(Vertex1, Vertex1, Renderer.ModelMat, zeroVector);
        TransformVector(Vertex2, Vertex2, Renderer.ModelMat, zeroVector);
        TransformVector(Vertex3, Vertex3, Renderer.ModelMat, zeroVector);
        TransformVector(Vertex4, Vertex4, Renderer.ModelMat, zeroVector);
        TransformVector(Vertex5, Vertex5, Renderer.ModelMat, zeroVector);
        TransformVector(Vertex6, Vertex6, Renderer.ModelMat, zeroVector);
        TransformVector(Vertex7, Vertex7, Renderer.ModelMat, zeroVector);
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
