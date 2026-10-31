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
        // Placements that only follow the mouse, before a click picks them: a darker blue than the picked preview
        // (Amelia, 2026-10-29), and fainter or dashed too, so the difference doesn't rest on colour alone.
        // Aim lines and attack paths stay PreviewColour while following the mouse, as every aim line is that blue.
        private static readonly Color HoverPlacementColour = new Color(0.3f, 0.55f, 0.95f, 0.8f);
        // Highlight glow: grey for player A's lines, golden-yellow for player B's, to match their chalk.
        private static readonly Color HighlightA = new Color(0.75f, 0.75f, 0.75f, 0.55f);
        private static readonly Color HighlightB = new Color(1f, 0.72f, 0.1f, 0.55f);
        // Lime is used for the durability dot and nothing else: one colour, one meaning (DESIGN.md section 1.1).
        private static readonly Color DurabilityDotColour = new Color(0.6f, 1f, 0.3f, 1f);
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
        /// <summary>Set from OnGUI: the start and direction of the LoV under the mouse, whose aim line is shown.</summary>
        public Vec2? HoverAimFrom { get; set; }
        public Vec2? HoverAimDirection { get; set; }
        /// <summary>Whose LoV the hover aim line belongs to; it passes through that player's own lines.</summary>
        public Player? HoverAimOwner { get; set; }
        /// <summary>Set from OnGUI: the chalkling under the mouse or hovered in a list, whose guard ring or path is shown.</summary>
        public ChalklingBody HoverChalkling { get; set; }

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

        /// <summary>A wide glow under a line in its owner's highlight colour, drawn before the lines so the line itself stays readable on top.</summary>
        private void DrawHighlight(LineBody body)
        {
            Color glowColour = body.Owner == Player.A ? HighlightA : HighlightB;
            // Every highlight reaches the same distance, glow / 2, past the edge of its line.
            const float glow = 0.18f;
            var ward = body as WardBody;
            if (ward != null)
            {
                float width = (ward.Line.IsMainWard ? Board.Config.Geometry.MainWardLineWidth : Board.Config.Geometry.WardLineWidth) + glow;
                for (int i = 0; i < ward.SampleCount; i++)
                    Line(ward.SamplePoint(i), ward.SamplePoint((i + 1) % ward.SampleCount), width, glowColour);
                foreach (var brace in ward.Braces) Line(brace.A, brace.B, ForbiddanceWidth + glow, glowColour);
                return;
            }
            var forbiddance = body as ForbiddanceBody;
            if (forbiddance != null)
            {
                RoundedLine(forbiddance.Segment.A, forbiddance.Segment.B, ForbiddanceWidth + glow, glowColour);
                return;
            }
            var vigor = body as VigorBody;
            if (vigor != null)
            {
                // A glow along the wave itself, plus its aim line in the usual aim line colour.
                Wave(vigor.Position, vigor.Direction, Board.Config.Geometry.VigorDrawnLength, glowColour, false, 0.06f + glow, true);
                AimLine(vigor.Position, vigor.Direction, vigor.Owner, PreviewColour);
                return;
            }
            var chalkling = body as ChalklingBody;
            if (chalkling != null) Disc(chalkling.Position, ChalklingRadius + glow / 2f, glowColour);
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
                Chalkling(chalkling.Position, chalkling.Line.Glyph, colour);
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
                    // Its glyph stays hidden until it is finished, so only a faint dashed circle shows.
                    Ring(sketch.Point.Value, ChalklingRadius, 0.05f, colour, true);
                    return;
            }
        }

        /// <summary>Marks for the line under the mouse, matching the hover readout.</summary>
        private void DrawHover()
        {
            if (HoverWardPoint.HasValue)
            {
                Disc(HoverWardPoint.Value, HoverMarkSize / 2f + 0.03f, Color.black);
                Disc(HoverWardPoint.Value, HoverMarkSize / 2f, DurabilityDotColour);
            }
            if (HoverAimFrom.HasValue && HoverAimDirection.HasValue) AimLine(HoverAimFrom.Value, HoverAimDirection.Value, HoverAimOwner, PreviewColour);
            if (HoverChalkling != null)
            {
                ChalklingReach(HoverChalkling.Owner, HoverChalkling.Line.Glyph, HoverChalkling.Anchor, HoverChalkling.Position, HoverChalkling.GuardDistance);
                // A bound chalkling's bind point is highlighted, the same as in the selected preview of a bound chalkling.
                if (HoverChalkling.BindPoint.HasValue)
                    Disc(HoverChalkling.BindPoint.Value, BindPointSize / 2f + BindPointRingWidth / 2f, PreviewColour);
            }
        }

        /// <summary>
        /// What a chalkling will do, in the aim line blue: a Defend chalkling's guard ring around its anchor
        /// (bind point or spot drawn), or an Attack chalkling's straight path, stopping at the first blocker.
        /// The guard ring takes <paramref name="ringColour"/> (darker while it only follows the mouse); the path, like
        /// every aim line, is always the aim line blue.
        /// </summary>
        private void ChalklingReach(Player owner, Glyph glyph, Vec2 anchor, Vec2 position, float guardDistance, Color? ringColour = null)
        {
            if (glyph == Glyph.Defend)
            {
                Ring(anchor, guardDistance, 0.03f, ringColour ?? PreviewColour, true);
                return;
            }
            DashedPath(Board.ChalklingAttackPath(owner, position), PreviewColour);
        }

        private const float AimDash = 0.3f;
        private const float AimGap = 0.2f;

        /// <summary>
        /// Where a LoV would go, dashed, reflecting off LoF as it would until it reaches a LoW, chalkling or the edge.
        /// Dashes run on around each reflection point, bending there, so the bounce reads as one continuous line.
        /// </summary>
        private void AimLine(Vec2 from, Vec2 direction, Player? owner, Color colour)
        {
            DashedPath(Board.AimPath(from, direction, owner), colour);
        }

        /// <summary>A path of points drawn dashed like the aim line.</summary>
        private void DashedPath(System.Collections.Generic.IList<Vec2> path, Color colour)
        {
            if (path == null) return;
            bool on = true;
            float left = AimDash;
            for (int i = 1; i < path.Count; i++)
            {
                Vec2 start = path[i - 1];
                float length = Vec2.Distance(start, path[i]);
                if (length <= 0f) continue;
                Vec2 step = (path[i] - start) / length;
                float d = 0f;
                while (d < length)
                {
                    float end = Math.Min(length, d + left);
                    if (on) Line(start + step * d, start + step * end, 0.03f, colour);
                    left -= end - d;
                    d = end;
                    if (left <= 1e-4f)
                    {
                        on = !on;
                        left = on ? AimDash : AimGap;
                    }
                }
            }
        }

        private void DrawPreview(PlacementPreview preview)
        {
            var bindPoints = Board.BindPointsOf(preview.Player);
            foreach (int index in preview.BindPoints)
                if (index >= 0 && index < bindPoints.Count)
                    Disc(bindPoints[index], BindPointSize / 2f + BindPointRingWidth / 2f, PreviewColour);
            if (preview.HoverBindPoint.HasValue)
                Disc(preview.HoverBindPoint.Value, BindPointSize / 2f + BindPointRingWidth / 2f, WithAlpha(HoverPlacementColour, 0.6f));
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
            // A main LoW placed freely (MainWardAtHome off) also follows the mouse as a ghost.
            if (preview.GhostWard != null) Outline(preview.GhostWard, true, WithAlpha(HoverPlacementColour, 0.6f));
            if (preview.Ward != null) Outline(preview.Ward, !preview.WardChosen, PreviewColour);
            if (preview.Braces != null)
                foreach (var brace in preview.Braces) Line(brace.A, brace.B, ForbiddanceWidth * 0.7f, PreviewColour);
            if (preview.GhostSegment.HasValue) Line(preview.GhostSegment.Value.A, preview.GhostSegment.Value.B, ForbiddanceWidth, WithAlpha(HoverPlacementColour, 0.6f));
            if (preview.Segment.HasValue) Line(preview.Segment.Value.A, preview.Segment.Value.B, ForbiddanceWidth, PreviewColour);
            Color chalklingColour = preview.ChalklingHovered ? HoverPlacementColour : PreviewColour;
            if (preview.ChalklingAnchor.HasValue && preview.ChalklingGlyph.HasValue)
            {
                Vec2 spot = preview.ChalklingAnchor.Value;
                Chalkling(spot, preview.ChalklingGlyph.Value, chalklingColour);
                ChalklingReach(preview.Player, preview.ChalklingGlyph.Value, spot, spot, preview.ChalklingGuardDistance, chalklingColour);
            }
            // Before a glyph is chosen: the body alone, with no glyph, ring or path.
            else if (preview.ChalklingAnchor.HasValue) Disc(preview.ChalklingAnchor.Value, ChalklingRadius, chalklingColour);
            else if (preview.Point.HasValue) Diamond(preview.Point.Value, 0.25f, PreviewColour);
            // The red cross for an invalid spot is drawn by DuelController in OnGUI, at this frame's mouse position.
            if (preview.From.HasValue)
            {
                Square(preview.From.Value, 0.2f, preview.FromHovered ? HoverPlacementColour : PreviewColour);
                if (preview.To.HasValue)
                {
                    Vec2 direction = (preview.To.Value - preview.From.Value).Normalized;
                    Wave(preview.From.Value, direction, Board.Config.Geometry.VigorDrawnLength, PreviewColour);
                    AimLine(preview.From.Value, direction, preview.Player, PreviewColour);
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
        private static void Wave(Vec2 centre, Vec2 direction, float length, Color colour, bool dotted = false, float width = 0.06f, bool rounded = false)
        {
            const float amplitude = 0.18f;
            const int steps = 16;
            Vec2 side = direction.Perp;
            var points = new Vec2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                float s = (float)i / steps;
                points[i] = centre + direction * ((s - 1f) * length) + side * (amplitude * Mathf.Sin(s * 4f * Mathf.PI));
            }
            if (!rounded)
            {
                for (int i = 1; i <= steps; i++)
                    if (!(dotted && i % 2 == 0)) Line(points[i - 1], points[i], width, colour);
                return;
            }
            // A see-through glow is drawn as one joined strip, so the segments don't overlap and darken at the
            // joints, with round caps on the two ends so it wraps fully around the wave.
            var offsets = new Vec2[steps + 1];
            for (int i = 0; i <= steps; i++)
            {
                Vec2 along = (points[Math.Min(i + 1, steps)] - points[Math.Max(i - 1, 0)]).Normalized;
                offsets[i] = along.Perp * (width / 2f);
            }
            GL.Color(colour);
            for (int i = 1; i <= steps; i++)
            {
                Vertex(points[i - 1] + offsets[i - 1]);
                Vertex(points[i - 1] - offsets[i - 1]);
                Vertex(points[i] - offsets[i]);
                Vertex(points[i] + offsets[i]);
            }
            Cap(points[0], points[0] - points[1], width / 2f, colour);
            Cap(points[steps], points[steps] - points[steps - 1], width / 2f, colour);
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

        /// <summary>A line with round ends, so a glow wraps fully around it.</summary>
        private static void RoundedLine(Vec2 a, Vec2 b, float width, Color colour)
        {
            Line(a, b, width, colour);
            Cap(a, a - b, width / 2f, colour);
            Cap(b, b - a, width / 2f, colour);
        }

        /// <summary>A half disc on the end of a line, bulging towards <paramref name="outward"/>. Half, not whole, so it doesn't overlap the line.</summary>
        private static void Cap(Vec2 centre, Vec2 outward, float radius, Color colour)
        {
            Vec2 forward = outward.Normalized;
            Vec2 side = forward.Perp;
            const int steps = CircleSteps / 2;
            GL.Color(colour);
            for (int i = 0; i < steps; i++)
            {
                float t0 = Mathf.PI * i / steps, t1 = Mathf.PI * (i + 1) / steps;
                Vertex(centre);
                Vertex(centre + (side * Mathf.Cos(t0) + forward * Mathf.Sin(t0)) * radius);
                Vertex(centre + (side * Mathf.Cos(t1) + forward * Mathf.Sin(t1)) * radius);
                Vertex(centre + (side * Mathf.Cos(t1) + forward * Mathf.Sin(t1)) * radius);
            }
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

        /// <summary>A chalkling's body radius, from the rules config so the drawing matches its hit area.</summary>
        public float ChalklingRadius = 0.35f;
        // The glyph is marked in the board's dark, as if scratched into the chalk, so it reads on either player's colour.
        private static readonly Color GlyphMark = new Color(0.1f, 0.12f, 0.12f, 1f);

        /// <summary>
        /// A chalkling: a filled circle in its owner's chalk with its glyph on its back, a cross for Attack
        /// and a diamond for Defend (DESIGN.md section 4.4), so the glyph reads by shape, not colour.
        /// </summary>
        private void Chalkling(Vec2 centre, Glyph glyph, Color colour)
        {
            Disc(centre, ChalklingRadius, colour);
            float r = ChalklingRadius * 0.55f;
            const float width = 0.05f;
            if (glyph == Glyph.Attack)
            {
                Line(centre + new Vec2(-r, -r) * 0.75f, centre + new Vec2(r, r) * 0.75f, width, GlyphMark);
                Line(centre + new Vec2(-r, r) * 0.75f, centre + new Vec2(r, -r) * 0.75f, width, GlyphMark);
                return;
            }
            Vec2 up = new Vec2(0f, r);
            Vec2 right = new Vec2(r, 0f);
            Line(centre - up, centre + right, width, GlyphMark);
            Line(centre + right, centre + up, width, GlyphMark);
            Line(centre + up, centre - right, width, GlyphMark);
            Line(centre - right, centre - up, width, GlyphMark);
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
