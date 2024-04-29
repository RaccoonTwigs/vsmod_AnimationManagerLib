using OpenTK.Compute.OpenCL;
using System;
using System.Collections.Generic;
using System.Xml.Linq;
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
        Vec4f from = new((float)ForElement.From[0], (float)ForElement.From[1], (float)ForElement.From[2], 1);
        Vec4f to = new((float)ForElement.To[0], (float)ForElement.To[1], (float)ForElement.To[2], 1);
        Vec4f diagonal = to - from;

        PreVertex0 = from;
        PreVertex7 = to;
        PreVertex1 = new(from.X + diagonal.X, from.Y, from.Z, from.W);
        PreVertex2 = new(from.X, from.Y + diagonal.Y, from.Z, from.W);
        PreVertex3 = new(from.X, from.Y, from.Z + diagonal.Z, from.W);
        PreVertex4 = new(from.X + diagonal.X, from.Y + diagonal.Y, from.Z, from.W);
        PreVertex5 = new(from.X, from.Y + diagonal.Y, from.Z + diagonal.Z, from.W);
        PreVertex6 = new(from.X + diagonal.X, from.Y, from.Z + diagonal.Z, from.W);

        float[] elemMatrix = new float[16];
        Mat4f.Identity(elemMatrix);
        Matrixf elementMatrix = new(elemMatrix);
        if (ForElement.ParentElement != null) GetElementTransformMatrix(elementMatrix, ForElement.ParentElement);

        PreVertex0 = Transform(elementMatrix, PreVertex0, 1 / 16f);
        PreVertex1 = Transform(elementMatrix, PreVertex1, 1 / 16f);
        PreVertex2 = Transform(elementMatrix, PreVertex2, 1 / 16f);
        PreVertex3 = Transform(elementMatrix, PreVertex3, 1 / 16f);
        PreVertex4 = Transform(elementMatrix, PreVertex4, 1 / 16f);
        PreVertex5 = Transform(elementMatrix, PreVertex5, 1 / 16f);
        PreVertex6 = Transform(elementMatrix, PreVertex6, 1 / 16f);
        PreVertex7 = Transform(elementMatrix, PreVertex7, 1 / 16f);
    }

    public static Vec4f Transform(Matrixf matrix, Vec4f vector, float scale)
    {
        Vec4f crutch = new();
        Mat4f.MulWithVec4(matrix.Values, vector, crutch);
        return crutch * scale;
    }

    public static string PrintVector(Vec4f vector) => $"({vector.X}, {vector.Y}, {vector.Z})";

    public void GetElementTransformMatrix(Matrixf matrix, ShapeElement element, int depth = 0)
    {
        if (element.ParentElement != null)
        {
            GetElementTransformMatrix(matrix, element.ParentElement, depth + 1);
        }

        matrix.Translate(element.From[0], element.From[1], element.From[2])
            .Translate(element.RotationOrigin[0] / 16, element.RotationOrigin[1] / 16, element.RotationOrigin[2] / 16)
            .RotateX((float)element.RotationX * GameMath.DEG2RAD)
            .RotateY((float)element.RotationY * GameMath.DEG2RAD)
            .RotateZ((float)element.RotationZ * GameMath.DEG2RAD)
            .Translate(0f - element.RotationOrigin[0] / 16, 0f - element.RotationOrigin[1] / 16, 0f - element.RotationOrigin[2] / 16);
    }

    public void Transform(float[] matrix)
    {
        if (Renderer == null) return;
        
        float[] mm = new float[16];

        Mat4f.Identity(mm);

        //Mat4f.Mul(mm, Renderer.ModelMat, matrix);
        mm = Renderer.ModelMat;


        TransformVector(PreVertex0, Vertex0, mm);
        TransformVector(PreVertex1, Vertex1, mm);
        TransformVector(PreVertex2, Vertex2, mm);
        TransformVector(PreVertex3, Vertex3, mm);
        TransformVector(PreVertex4, Vertex4, mm);
        TransformVector(PreVertex5, Vertex5, mm);
        TransformVector(PreVertex6, Vertex6, mm);
        TransformVector(PreVertex7, Vertex7, mm);
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
    private void TransformVector(Vec4f input, Vec4f output, float[] modelMatrix) //, EntityPos playerPos)
    {
        /*Vec4f interm = new(
            input.X + (float)Entity.Pos.X,
            input.Y + (float)Entity.Pos.Y,
            input.Z + (float)Entity.Pos.Z,
            input.W);*/

        Mat4f.MulWithVec4(modelMatrix, input, output);

        /*output.X -= (float)Entity.Pos.X;
        output.Y -= (float)Entity.Pos.Y;
        output.Z -= (float)Entity.Pos.Z;*/
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
