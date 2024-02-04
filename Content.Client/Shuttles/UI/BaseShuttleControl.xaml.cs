using System.Numerics;
using Content.Client.UserInterface.Controls;
using Robust.Client.AutoGenerated;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface.XAML;
using Robust.Shared.Collections;
using Robust.Shared.Map.Components;

namespace Content.Client.Shuttles.UI;

/// <summary>
/// Provides common functionality for radar-like displays on shuttle consoles.
/// </summary>
[GenerateTypedNameReferences]
[Virtual]
public partial class BaseShuttleControl : MapGridControl
{
    protected SharedMapSystem Maps;

    protected Font Font;

    public BaseShuttleControl() : this(32f, 32f, 32f)
    {
    }

    public BaseShuttleControl(float minRange, float maxRange, float range) : base(minRange, maxRange, range)
    {
        RobustXamlLoader.Load(this);
        Maps = EntManager.System<SharedMapSystem>();
        Font = new VectorFont(IoCManager.Resolve<IResourceCache>().GetResource<FontResource>("/Fonts/NotoSans/NotoSans-Regular.ttf"), 12);
    }

    protected void DrawCircles(DrawingHandleScreen handle)
    {
        // Equatorial lines
        var gridLines = Color.LightGray.WithAlpha(0.01f);

        // Each circle is this x distance of the last one.
        const float EquatorialMultiplier = 2f;

        var minDistance = MathF.Pow(EquatorialMultiplier, EquatorialMultiplier * 1.5f);
        var maxDistance = MathF.Pow(2f, EquatorialMultiplier * 6f);
        var cornerDistance = MathF.Sqrt(WorldRange * WorldRange + WorldRange * WorldRange);

        var origin = ScalePosition(-new Vector2(Offset.X, -Offset.Y));
        var distOffset = -24f;

        for (var radius = minDistance; radius <= maxDistance; radius *= EquatorialMultiplier)
        {
            if (radius > cornerDistance)
                continue;

            var color = Color.ToSrgb(gridLines).WithAlpha(0.05f);
            var scaledRadius = MinimapScale * radius;
            var text = $"{radius:0}m";
            var textDimensions = handle.GetDimensions(Font, text, UIScale);

            handle.DrawCircle(origin, scaledRadius, color, false);
            handle.DrawString(Font, ScalePosition(new Vector2(0f, -radius)) - new Vector2(0f, textDimensions.Y), text, color);
        }

        const int gridLinesRadial = 8;

        for (var i = 0; i < gridLinesRadial; i++)
        {
            Angle angle = (Math.PI / gridLinesRadial) * i;
            // TODO: Handle distance properly.
            var aExtent = angle.ToVec() * ScaledMinimapRadius * 1.42f;
            var lineColor = Color.MediumSpringGreen.WithAlpha(0.02f);
            handle.DrawLine(origin - aExtent, origin + aExtent, lineColor);
        }
    }

    protected void DrawGrid(DrawingHandleScreen handle, Matrix3 matrix, Entity<MapGridComponent> grid, Color color)
    {
        var rator = Maps.GetAllTilesEnumerator(grid.Owner, grid.Comp);
        var edges = new ValueList<Vector2>();
        var tileTris = new ValueList<Vector2>();
        const bool DrawInterior = true;

        while (rator.MoveNext(out var tileRef))
        {
            // TODO: Short-circuit interior chunk nodes
            // This can be optimised a lot more if required.
            var tileVec = Maps.TileCenterToVector(grid, tileRef.Value.GridIndices);

            /*
             * You may be wondering what the fuck is going on here.
             * Well you see originally I tried drawing the interiors by fixture, but the problem is
             * you get rounding issues and get noticeable aliasing (at least if you don't overdraw and use alpha).
             * Hence per-tile should alleviate it.
             */
            var bl = tileVec;
            var br = tileVec + new Vector2(grid.Comp.TileSize, 0f);
            var tr = tileVec + new Vector2(grid.Comp.TileSize, grid.Comp.TileSize);
            var tl = tileVec + new Vector2(0f, grid.Comp.TileSize);

            var adjustedBL = matrix.Transform(bl);
            var adjustedBR = matrix.Transform(br);
            var adjustedTR = matrix.Transform(tr);
            var adjustedTL = matrix.Transform(tl);

            var scaledBL = ScalePosition(new Vector2(adjustedBL.X, -adjustedBL.Y));
            var scaledBR = ScalePosition(new Vector2(adjustedBR.X, -adjustedBR.Y));
            var scaledTR = ScalePosition(new Vector2(adjustedTR.X, -adjustedTR.Y));
            var scaledTL = ScalePosition(new Vector2(adjustedTL.X, -adjustedTL.Y));

            if (DrawInterior)
            {
                // Draw 2 triangles for the quad.
                tileTris.Add(scaledBL);
                tileTris.Add(scaledBR);
                tileTris.Add(scaledTL);

                tileTris.Add(scaledBR);
                tileTris.Add(scaledTL);
                tileTris.Add(scaledTR);
            }

            // Iterate edges and see which we can draw
            for (var i = 0; i < 4; i++)
            {
                var dir = (DirectionFlag) Math.Pow(2, i);
                var dirVec = dir.AsDir().ToIntVec();

                if (!Maps.GetTileRef(grid.Owner, grid.Comp, tileRef.Value.GridIndices + dirVec).Tile.IsEmpty)
                    continue;

                Vector2 start;
                Vector2 end;
                Vector2 actualStart;
                Vector2 actualEnd;

                // Draw line
                // Could probably rotate this but this might be faster?
                switch (dir)
                {
                    case DirectionFlag.South:
                        start = adjustedBL;
                        end = adjustedBR;

                        actualStart = scaledBL;
                        actualEnd = scaledBR;
                        break;
                    case DirectionFlag.East:
                        start = adjustedBR;
                        end = adjustedTR;

                        actualStart = scaledBR;
                        actualEnd = scaledTR;
                        break;
                    case DirectionFlag.North:
                        start = adjustedTR;
                        end = adjustedTL;

                        actualStart = scaledTR;
                        actualEnd = scaledTL;
                        break;
                    case DirectionFlag.West:
                        start = adjustedTL;
                        end = adjustedBL;

                        actualStart = scaledTL;
                        actualEnd = scaledBL;
                        break;
                    default:
                        throw new NotImplementedException();
                }

                if (start.Length() > CornerRadarRange && end.Length() > CornerRadarRange)
                    continue;

                edges.Add(actualStart);
                edges.Add(actualEnd);
            }
        }

        if (DrawInterior)
        {
            handle.DrawPrimitives(DrawPrimitiveTopology.TriangleList, tileTris.Span, color.WithAlpha(0.05f));
        }

        handle.DrawPrimitives(DrawPrimitiveTopology.LineList, edges.Span, color);
    }
}
