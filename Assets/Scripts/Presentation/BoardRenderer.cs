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
        private static readonly Color PreviewColour = new Color(0.6f, 0.9f, 1f, 0.8f);

        private const float WardWidth = 0.08f;
        private const float MainWardWidth = 0.12f;
        private const float ForbiddanceWidth = 0.14f;
        private const float BindPointSize = 0.4f;

        private Material _material;

        /// <summary>Set by the duel controller every frame.</summary>
        public ChalkBoard Board { get; set; }
        public Camera BoardCamera { get; set; }
        public PlacementPreview Preview { get; set; }

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
            foreach (var body in Board.Bodies) DrawBody(body);
            if (Preview != null) DrawPreview(Preview);
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
        }

        private void DrawBody(LineBody body)
        {
            Color colour = Owner(body.Owner);

            var ward = body as WardBody;
            if (ward != null)
            {
                float width = ward.Line.IsMainWard ? MainWardWidth : WardWidth;
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
                Wave(vigor.Position, vigor.Direction, colour);
                return;
            }

            var chalkling = body as ChalklingBody;
            if (chalkling != null)
            {
                if (chalkling.Tether.HasValue) Line(chalkling.Tether.Value, chalkling.Position, 0.02f, WithAlpha(colour, 0.4f));
                Diamond(chalkling.Position, 0.25f, colour);
            }
        }

        private void DrawPreview(PlacementPreview preview)
        {
            var bindPoints = Board.BindPointsOf(preview.Player);
            foreach (int index in preview.BindPoints)
                if (index >= 0 && index < bindPoints.Count) Square(bindPoints[index], BindPointSize, PreviewColour);
            if (preview.WardBindPoints != null)
                foreach (var point in preview.WardBindPoints) BindPoint(point, PreviewColour);

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
            if (preview.From.HasValue)
            {
                Square(preview.From.Value, 0.2f, PreviewColour);
                if (preview.To.HasValue)
                {
                    Vec2 direction = (preview.To.Value - preview.From.Value).Normalized;
                    Wave(preview.From.Value, direction, PreviewColour);
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

        /// <summary>A LoV is drawn as a short sine wave along its direction of travel.</summary>
        private static void Wave(Vec2 centre, Vec2 direction, Color colour)
        {
            const float length = 1.2f;
            const float amplitude = 0.18f;
            const int steps = 16;
            Vec2 side = direction.Perp;
            Vec2 previous = Vec2.Zero;
            for (int i = 0; i <= steps; i++)
            {
                float s = (float)i / steps;
                Vec2 point = centre + direction * ((s - 1f) * length) + side * (amplitude * Mathf.Sin(s * 4f * Mathf.PI));
                if (i > 0) Line(previous, point, 0.06f, colour);
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

        /// <summary>A bind point: a hollow square with a dot in the middle, so it stands out on top of its line.</summary>
        private static void BindPoint(Vec2 centre, Color colour)
        {
            float h = BindPointSize / 2f;
            const float width = 0.05f;
            Line(centre + new Vec2(-h, -h), centre + new Vec2(h, -h), width, colour);
            Line(centre + new Vec2(h, -h), centre + new Vec2(h, h), width, colour);
            Line(centre + new Vec2(h, h), centre + new Vec2(-h, h), width, colour);
            Line(centre + new Vec2(-h, h), centre + new Vec2(-h, -h), width, colour);
            Square(centre, 0.1f, colour);
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
