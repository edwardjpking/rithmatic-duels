using System;
using RithmaticDuels.Rules;
using RithmaticDuels.Rules.Geometry;
using UnityEngine;

namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// Draws the board with simple shapes in chalk colours, straight from the <see cref="ChalkBoard"/>.
    /// Uses Unity's low-level GL calls, so it needs no sprites, prefabs or scene setup. Proper chalk
    /// visuals come with drawing input (build plan step 4).
    /// </summary>
    public sealed class BoardRenderer : MonoBehaviour
    {
        private static readonly Color ChalkA = new Color(0.95f, 0.95f, 0.9f);
        private static readonly Color ChalkB = new Color(1f, 0.82f, 0.4f);
        private static readonly Color Damaged = new Color(0.95f, 0.3f, 0.25f);
        private static readonly Color Faint = new Color(1f, 1f, 1f, 0.15f);
        private static readonly Color CentreWallColour = new Color(0.75f, 0.8f, 0.85f, 0.7f);
        private static readonly Color PreviewColour = new Color(0.6f, 0.9f, 1f, 0.8f);
        private static readonly Color HighlightColour = new Color(1f, 0.4f, 0.85f, 0.5f);
        private static readonly Color HoverColour = new Color(0.6f, 0.9f, 1f, 1f);
        private const float HoverMarkSize = 0.22f;
        private static readonly Color BlockedFill = new Color(0.95f, 0.3f, 0.25f, 0.75f);

        private const float WardWidth = 0.08f;
        private const float MainWardWidth = 0.12f;
        private const float BindPointSize = 0.4f;
        private const float BindPointRingWidth = 0.05f;
        /// <summary>How strongly a drawing in progress shows, compared with a finished line.</summary>
        private const float SketchAlpha = 0.6f;
        private const float DashLength = 0.35f;
        private const float GapLength = 0.25f;

        private Material _material;

        /// <summary>A LoF is drawn as the rectangle the rules treat it as.</summary>
        private float ForbiddanceWidth { get { return Board.Config.Geometry.ForbiddanceWidth; } }

        /// <summary>Set by the duel controller every frame.</summary>
        public ChalkBoard Board { get; set; }
        public Camera BoardCamera { get; set; }
        public PlacementPreview Preview { get; set; }
        /// <summary>A line to pick out with a glow, such as one hovered in a lines list. -1 for none.</summary>
        public int HighlightLineId { get; set; } = -1;
        /// <summary>Set from OnGUI: the LoW outline point whose durability the hover readout shows.</summary>
        public Vec2? HoverWardPoint { get; set; }
        /// <summary>Set from OnGUI: the LoV under the mouse, whose direction of travel gets an arrow.</summary>
        public VigorBody HoverVigor { get; set; }

        private void OnRenderObject()
        {
            if (Board == null || Camera.current != BoardCamera) return;
            EnsureMaterial();
            _material.SetPass(0);

            GL.PushMatrix();
            GL.MultMatrix(Matrix4x4.identity);
            GL.Begin(GL.QUADS);
            DrawBoardOutline();
            foreach (var trail in Board.LastTickTrails)
                for (int i = 1; i < trail.Points.Count; i++)
                    Line(trail.Points[i - 1], trail.Points[i], 0.03f, WithAlpha(Owner(trail.Owner), 0.3f));
            // Drawings in progress go underneath finished lines: they are only a picture until complete.
            DrawSketch(Board.WorkInProgressOf(Player.A));
            DrawSketch(Board.WorkInProgressOf(Player.B));
            var highlighted = HighlightLineId >= 0 ? Board.BodyOf(HighlightLineId) : null;
            if (highlighted != null) DrawHighlight(highlighted);
            foreach (var body in Board.Bodies) DrawBody(body);
            if (Preview != null) DrawPreview(Preview);
            DrawHover();
            GL.End();
            GL.PopMatrix();
        }

        private void DrawBoardOutline()
        {
            float w = Board.Width;
            float h = Board.Height;
            Line(new Vec2(0f, 0f), new Vec2(w, 0f), 0.05f, Faint);
            Line(new Vec2(w, 0f), new Vec2(w, h), 0.05f, Faint);
            Line(new Vec2(w, h), new Vec2(0f, h), 0.05f, Faint);
            Line(new Vec2(0f, h), new Vec2(0f, 0f), 0.05f, Faint);
            // Dashed centre line: each player draws on their own half.
            for (float y = 0f; y < h; y += 1f) Line(new Vec2(w / 2f, y), new Vec2(w / 2f, y + 0.5f), 0.03f, Faint);
            // The centre LoF that stands for the first turns of the duel belongs to neither player.
            if (Board.CentreWallUp) Line(Board.CentreWall.A, Board.CentreWall.B, ForbiddanceWidth, CentreWallColour);
        }

        /// <summary>A wide pink glow under a line (not blue, so it can't be mistaken for a placement preview), drawn before the lines so the line itself stays readable on top.</summary>
        private void DrawHighlight(LineBody body)
        {
            const float glow = 0.18f;
            var ward = body as WardBody;
            if (ward != null)
            {
                float width = (ward.Line.IsMainWard ? Board.Config.Geometry.MainWardLineWidth : Board.Config.Geometry.WardLineWidth) + glow;
                for (int i = 0; i < ward.SampleCount; i++)
                    Line(ward.SamplePoint(i), ward.SamplePoint((i + 1) % ward.SampleCount), width, HighlightColour);
                foreach (var brace in ward.Braces) Line(brace.A, brace.B, ForbiddanceWidth + glow, HighlightColour);
                return;
            }
            var forbiddance = body as ForbiddanceBody;
            if (forbiddance != null)
            {
                Line(forbiddance.Segment.A, forbiddance.Segment.B, ForbiddanceWidth + glow, HighlightColour);
                return;
            }
            var vigor = body as VigorBody;
            if (vigor != null)
            {
                Disc(vigor.Position, Board.Config.Geometry.VigorDrawnLength / 2f + glow, HighlightColour);
                return;
            }
            var chalkling = body as ChalklingBody;
            if (chalkling != null) Disc(chalkling.Position, 0.25f + glow, HighlightColour);
        }

        private void DrawBody(LineBody body)
        {
            Color colour = Owner(body.Owner);

            var ward = body as WardBody;
            if (ward != null)
            {
                // As thick as the rules say, since they keep other lines clear of this thickness.
                float width = ward.Line.IsMainWard ? Board.Config.Geometry.MainWardLineWidth : Board.Config.Geometry.WardLineWidth;
                int count = ward.SampleCount;
                for (int i = 0; i < count; i++)
                {
                    int next = (i + 1) % count;
                    float health = (float)ward.SampleDurability(i) / Math.Max(1, ward.SampleMax(i));
                    Line(ward.SamplePoint(i), ward.SamplePoint(next), width, Color.Lerp(Damaged, colour, health));
                }
                foreach (var brace in ward.Braces) Line(brace.A, brace.B, ForbiddanceWidth * 0.7f, colour);
                foreach (var point in ward.BindPoints) BindPoint(point, colour);
                return;
            }

            var forbiddance = body as ForbiddanceBody;
            if (forbiddance != null)
            {
                Line(forbiddance.Segment.A, forbiddance.Segment.B, ForbiddanceWidth, colour);
                return;
            }

            var vigor = body as VigorBody;
            if (vigor != null)
            {
                Wave(vigor.Position, vigor.Direction, Board.Config.Geometry.VigorDrawnLength, colour);
                return;
            }

            var chalkling = body as ChalklingBody;
            if (chalkling != null)
            {
                if (chalkling.Tether.HasValue) Line(chalkling.Tether.Value, chalkling.Position, 0.02f, WithAlpha(colour, 0.4f));
                Diamond(chalkling.Position, 0.25f, colour);
            }
        }

        /// <summary>
        /// A player's work in progress, which both players can see: the line it will make, dashed and faint
        /// in the owner's colour.
        /// </summary>
        private void DrawSketch(WorkSketch sketch)
        {
            if (sketch == null) return;
            Color colour = WithAlpha(Owner(sketch.Owner), SketchAlpha);

            switch (sketch.Kind)
            {
                case WorkKind.BindPoints:
                    // Bind points still being drawn on the main LoW: faint dashed rings where they will appear.
                    var main = Board.MainWardOf(sketch.Owner);
                    if (main != null && sketch.BindPoints != null)
                        foreach (var point in sketch.BindPoints) UnfinishedBindPoint(point, colour);
                    return;

                case WorkKind.Drawing:
                    break;

                default:
                    return;
            }

            switch (sketch.Type)
            {
                case LineType.Warding:
                    if (sketch.Ward == null) return;
                    Outline(sketch.Ward, true, colour);
                    if (sketch.Braces != null)
                        foreach (var brace in sketch.Braces) DashedLine(brace.A, brace.B, ForbiddanceWidth * 0.7f, colour);
                    if (sketch.BindPoints != null)
                        foreach (var point in sketch.BindPoints) UnfinishedBindPoint(point, colour);
                    return;

                case LineType.Forbiddance:
                    if (sketch.Segment.HasValue) DashedLine(sketch.Segment.Value.A, sketch.Segment.Value.B, ForbiddanceWidth, colour);
                    return;

                case LineType.Vigor:
                    if (sketch.Point.HasValue && sketch.Direction.HasValue) Wave(sketch.Point.Value, sketch.Direction.Value.Normalized, Board.Config.Geometry.VigorDrawnLength, colour, true);
                    return;

                default:
                    if (!sketch.Point.HasValue) return;
                    if (sketch.Tether.HasValue) DashedLine(sketch.Tether.Value, sketch.Point.Value, 0.02f, colour);
                    DiamondOutline(sketch.Point.Value, 0.25f, colour);
                    return;
            }
        }

        /// <summary>Marks for the line under the mouse, matching the hover readout.</summary>
        private void DrawHover()
        {
            if (HoverWardPoint.HasValue)
            {
                Disc(HoverWardPoint.Value, HoverMarkSize / 2f + 0.03f, Color.black);
                Disc(HoverWardPoint.Value, HoverMarkSize / 2f, HoverColour);
            }
            if (HoverVigor != null && HoverVigor.Line.IsActive) Arrowhead(HoverVigor.Position, HoverVigor.Direction, HoverColour);
        }

        /// <summary>An arrowhead whose tip is just ahead of <paramref name="tip"/>, pointing along <paramref name="direction"/>.</summary>
        private static void Arrowhead(Vec2 tip, Vec2 direction, Color colour)
        {
            const float length = 0.45f;
            const float halfWidth = 0.25f;
            Vec2 point = tip + direction * 0.3f;
            Vec2 back = point - direction * length;
            Vec2 side = direction.Perp * halfWidth;
            Line(point, back + side, 0.07f, colour);
            Line(point, back - side, 0.07f, colour);
        }

        private void DrawPreview(PlacementPreview preview)
        {
            var bindPoints = Board.BindPointsOf(preview.Player);
            foreach (int index in preview.BindPoints)
                if (index >= 0 && index < bindPoints.Count)
                    Disc(bindPoints[index], BindPointSize / 2f + BindPointRingWidth / 2f, PreviewColour);
            if (preview.WardBindPoints != null)
                foreach (var point in preview.WardBindPoints) BindPoint(point, PreviewColour);
            // Bind points that can't be picked for this drawing are filled in red, inside their ring.
            if (preview.BlockedBindPoints != null)
                foreach (var point in preview.BlockedBindPoints) Disc(point, BindPointSize / 2f - BindPointRingWidth / 2f, BlockedFill);

            if (preview.ShowMidline)
            {
                // The main LoW snaps to the midline of the player's half.
                float half = Board.Width / 2f;
                float from = preview.Player == Player.A ? 0f : half;
                for (float x = from; x < from + half; x += 1f)
                    Line(new Vec2(x, Board.Height / 2f), new Vec2(x + 0.5f, Board.Height / 2f), 0.03f, WithAlpha(PreviewColour, 0.35f));
            }
            if (preview.Presets != null)
                foreach (var spot in preview.Presets) Diamond(spot, 0.3f, WithAlpha(PreviewColour, 0.6f));
            if (preview.GhostWard != null) Outline(preview.GhostWard, true, WithAlpha(PreviewColour, 0.6f));
            if (preview.Ward != null) Outline(preview.Ward, !preview.WardChosen, PreviewColour);
            if (preview.Braces != null)
                foreach (var brace in preview.Braces) Line(brace.A, brace.B, ForbiddanceWidth * 0.7f, PreviewColour);
            if (preview.Segment.HasValue) Line(preview.Segment.Value.A, preview.Segment.Value.B, ForbiddanceWidth, PreviewColour);
            if (preview.Point.HasValue) Diamond(preview.Point.Value, 0.25f, PreviewColour);
            // The red cross for an invalid spot is drawn by DuelController in OnGUI, at this frame's mouse position.
            if (preview.From.HasValue)
            {
                Square(preview.From.Value, 0.2f, PreviewColour);
                if (preview.To.HasValue)
                {
                    Vec2 direction = (preview.To.Value - preview.From.Value).Normalized;
                    Wave(preview.From.Value, direction, Board.Config.Geometry.VigorDrawnLength, PreviewColour);
                    // Aim line, dashed out to the edge of the board.
                    for (float d = 1f; d < 40f; d += 1f)
                    {
                        Vec2 a = preview.From.Value + direction * d;
                        if (!Board.OnBoard(a)) break;
                        Line(a, a + direction * 0.5f, 0.03f, PreviewColour);
                    }
                }
            }
        }

        /// <summary>A LoW outline for previews: dashed while it is only a suggestion, solid once chosen.</summary>
        private static void Outline(WardShape shape, bool dashed, Color colour)
        {
            const int steps = 64;
            for (int i = 0; i < steps; i++)
            {
                if (dashed && i % 2 == 1) continue;
                float t0 = 2f * Mathf.PI * i / steps;
                float t1 = 2f * Mathf.PI * (i + 1) / steps;
                Line(shape.PointAt(t0), shape.PointAt(t1), dashed ? WardWidth : MainWardWidth, colour);
            }
        }

        /// <summary>A LoV is drawn as a short sine wave along its direction of travel; dotted while in progress.</summary>
        private static void Wave(Vec2 centre, Vec2 direction, float length, Color colour, bool dotted = false)
        {
            const float amplitude = 0.18f;
            const int steps = 16;
            Vec2 side = direction.Perp;
            Vec2 previous = Vec2.Zero;
            for (int i = 0; i <= steps; i++)
            {
                float s = (float)i / steps;
                Vec2 point = centre + direction * ((s - 1f) * length) + side * (amplitude * Mathf.Sin(s * 4f * Mathf.PI));
                if (i > 0 && !(dotted && i % 2 == 0)) Line(previous, point, 0.06f, colour);
                previous = point;
            }
        }

        private static void Line(Vec2 a, Vec2 b, float width, Color colour)
        {
            Vec2 offset = (b - a).Perp.Normalized * (width / 2f);
            GL.Color(colour);
            Vertex(a + offset);
            Vertex(a - offset);
            Vertex(b - offset);
            Vertex(b + offset);
        }

        /// <summary>A line broken into dashes, for drawings in progress.</summary>
        private static void DashedLine(Vec2 a, Vec2 b, float width, Color colour)
        {
            float length = Vec2.Distance(a, b);
            if (length <= 0f) return;
            Vec2 along = (b - a) / length;
            for (float d = 0f; d < length; d += DashLength + GapLength)
                Line(a + along * d, a + along * Math.Min(length, d + DashLength), width, colour);
        }

        /// <summary>The outline of a chalkling's diamond, for one still being drawn.</summary>
        private static void DiamondOutline(Vec2 centre, float radius, Color colour)
        {
            const float width = 0.05f;
            Vec2 up = new Vec2(0f, radius);
            Vec2 right = new Vec2(radius, 0f);
            Line(centre - up, centre + right, width, colour);
            Line(centre + right, centre + up, width, colour);
            Line(centre + up, centre - right, width, colour);
            Line(centre - right, centre - up, width, colour);
        }

        /// <summary>A bind point: a hollow circle with a dot in the middle, so it stands out on top of its line.</summary>
        private static void BindPoint(Vec2 centre, Color colour)
        {
            Ring(centre, BindPointSize / 2f, BindPointRingWidth, colour);
            Disc(centre, 0.05f, colour);
        }

        /// <summary>
        /// A bind point that isn't finished yet: a dashed ring with no dot, so it can't be mistaken for a
        /// finished one (solid ring and dot).
        /// </summary>
        private static void UnfinishedBindPoint(Vec2 centre, Color colour)
        {
            Ring(centre, BindPointSize / 2f, BindPointRingWidth, colour, true);
        }

        private const int CircleSteps = 32;

        private static Vec2 OnCircle(Vec2 centre, float radius, int step)
        {
            float angle = 2f * Mathf.PI * step / CircleSteps;
            return centre + new Vec2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        /// <summary>A circle outline <paramref name="width"/> thick, centred on <paramref name="radius"/>.</summary>
        private static void Ring(Vec2 centre, float radius, float width, Color colour, bool dashed = false)
        {
            float inner = radius - width / 2f;
            float outer = radius + width / 2f;
            GL.Color(colour);
            for (int i = 0; i < CircleSteps; i++)
            {
                // Dashes of four steps on, four off.
                if (dashed && (i / 4) % 2 == 1) continue;
                Vertex(OnCircle(centre, inner, i));
                Vertex(OnCircle(centre, outer, i));
                Vertex(OnCircle(centre, outer, i + 1));
                Vertex(OnCircle(centre, inner, i + 1));
            }
        }

        /// <summary>A filled circle, drawn as thin wedges from the centre.</summary>
        private static void Disc(Vec2 centre, float radius, Color colour)
        {
            GL.Color(colour);
            for (int i = 0; i < CircleSteps; i++)
            {
                // GL draws quads, so each wedge is a quad with its last corner repeated.
                Vertex(centre);
                Vertex(OnCircle(centre, radius, i));
                Vertex(OnCircle(centre, radius, i + 1));
                Vertex(OnCircle(centre, radius, i + 1));
            }
        }

        private static void Square(Vec2 centre, float size, Color colour)
        {
            float h = size / 2f;
            GL.Color(colour);
            Vertex(centre + new Vec2(-h, -h));
            Vertex(centre + new Vec2(h, -h));
            Vertex(centre + new Vec2(h, h));
            Vertex(centre + new Vec2(-h, h));
        }

        private static void Diamond(Vec2 centre, float radius, Color colour)
        {
            GL.Color(colour);
            Vertex(centre + new Vec2(0f, -radius));
            Vertex(centre + new Vec2(radius, 0f));
            Vertex(centre + new Vec2(0f, radius));
            Vertex(centre + new Vec2(-radius, 0f));
        }

        private static void Vertex(Vec2 point)
        {
            GL.Vertex3(point.X, point.Y, 0f);
        }

        private static Color Owner(Player player)
        {
            return player == Player.A ? ChalkA : ChalkB;
        }

        private static Color WithAlpha(Color colour, float alpha)
        {
            colour.a = alpha;
            return colour;
        }

        private void EnsureMaterial()
        {
            if (_material != null) return;
            // Unity's built-in unlit vertex-colour shader, set up for transparent 2D lines.
            _material = new Material(Shader.Find("Hidden/Internal-Colored")) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
            _material.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
            _material.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);
            _material.SetInt("_ZWrite", 0);
            _material.SetInt("_ZTest", (int)UnityEngine.Rendering.CompareFunction.Always);
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
        }
    }
}
