using System;
using System.Collections.Generic;

namespace RithmaticDuels.Rules.Geometry
{
    /// <summary>Where a completed line sits on the board. One per line on the ground.</summary>
    public abstract class LineBody
    {
        public Line Line { get; private set; }
        public int LineId { get { return Line.Id; } }
        public Player Owner { get { return Line.Owner; } }

        protected LineBody(Line line)
        {
            Line = line;
        }
    }

    /// <summary>
    /// A Line of Warding with durability tracked at evenly spaced points around its outline
    /// (DESIGN.md section 4.1). Each point's maximum depends on the local curvature.
    /// </summary>
    public sealed class WardBody : LineBody
    {
        private readonly float[] _params;
        private readonly float[] _arc;
        private readonly int[] _max;
        private readonly int[] _durability;

        public WardShape Shape { get; private set; }
        public IReadOnlyList<Vec2> BindPoints { get; internal set; }
        /// <summary>A Mark's Cross's crossed LoF inside its circle. Empty for any other LoW.</summary>
        public IReadOnlyList<Segment> Braces { get; internal set; } = new Segment[0];

        public float Perimeter { get; private set; }
        public int SampleCount { get { return _params.Length; } }

        /// <summary>Durability of the weakest point, which is what the rules core tracks.</summary>
        public int Weakest
        {
            get
            {
                int weakest = int.MaxValue;
                foreach (var value in _durability) weakest = Math.Min(weakest, value);
                return weakest;
            }
        }

        /// <summary>The maximum durability of the weakest point.</summary>
        public int WeakestMax
        {
            get
            {
                int index = 0;
                for (int i = 1; i < _durability.Length; i++)
                    if (_durability[i] < _durability[index] || (_durability[i] == _durability[index] && _max[i] < _max[index])) index = i;
                return _max[index];
            }
        }

        public bool IsBroken { get { return Weakest <= 0; } }

        public WardBody(Line line, WardShape shape, IReadOnlyList<Vec2> bindPoints, RulesConfig config) : base(line)
        {
            Shape = shape;
            BindPoints = bindPoints;
            int samples = Math.Max(8, config.Geometry.WardSamples);
            _params = new float[samples];
            _arc = new float[samples];
            _max = new int[samples];
            _durability = new int[samples];

            Vec2 previous = shape.PointAt(0f);
            for (int i = 0; i < samples; i++)
            {
                float t = (float)(2.0 * Math.PI * i / samples);
                Vec2 point = shape.PointAt(t);
                _params[i] = t;
                _arc[i] = i == 0 ? 0f : _arc[i - 1] + Vec2.Distance(previous, point);
                previous = point;

                _max[i] = PointDurability(shape, t, line.ModifierSteps, config);
                _durability[i] = _max[i];
            }
            Perimeter = _arc[samples - 1] + Vec2.Distance(previous, shape.PointAt(0f));
        }

        /// <summary>Durability at a point = base x clamp(sqrt(k / k_ref), min, max), plus the modifier.</summary>
        public static int PointDurability(WardShape shape, float t, int modifierSteps, RulesConfig config)
        {
            var warding = config.Warding;
            double scale = Math.Sqrt(shape.CurvatureAt(t) / warding.ReferenceCurvature);
            scale = Math.Max(warding.MinCurvatureScale, Math.Min(warding.MaxCurvatureScale, scale));
            int value = (int)Math.Round(warding.Durability * scale) + modifierSteps * warding.DurabilityPerModifierStep;
            return Math.Max(config.MinimumDurabilityAfterModifier, value);
        }

        /// <summary>The durability of the weakest point a LoW of this shape would have with no modifier.</summary>
        public static int WeakestDurability(WardShape shape, RulesConfig config)
        {
            int samples = Math.Max(8, config.Geometry.WardSamples);
            int weakest = int.MaxValue;
            for (int i = 0; i < samples; i++)
                weakest = Math.Min(weakest, PointDurability(shape, (float)(2.0 * Math.PI * i / samples), 0, config));
            return weakest;
        }

        public Vec2 SamplePoint(int index) { return Shape.PointAt(_params[index]); }
        public int SampleDurability(int index) { return _durability[index]; }
        public int SampleMax(int index) { return _max[index]; }

        /// <summary>Damage every point within <paramref name="halfWidth"/> of the impact, measured along the outline.</summary>
        public void ApplyHit(Vec2 impact, int damage, float halfWidth)
        {
            float t = Shape.ParamOf(impact);
            int nearest = (int)Math.Round(t / (2.0 * Math.PI) * _params.Length) % _params.Length;
            float centre = _arc[nearest];
            for (int i = 0; i < _params.Length; i++)
            {
                float along = Math.Abs(_arc[i] - centre);
                along = Math.Min(along, Perimeter - along);
                if (along <= halfWidth + 1e-4f) _durability[i] = Math.Max(0, _durability[i] - damage);
            }
        }

        /// <summary>Whole-line changes (chalk fatigue is negative) act on every point.</summary>
        public void ApplyEverywhere(int delta)
        {
            for (int i = 0; i < _durability.Length; i++)
                _durability[i] = Math.Max(0, Math.Min(_max[i], _durability[i] + delta));
        }
    }

    /// <summary>A Line of Forbiddance: a straight segment.</summary>
    public sealed class ForbiddanceBody : LineBody
    {
        public Segment Segment { get; private set; }

        /// <summary>
        /// For a LoF joining two bind points of one of its owner's LoW, that LoW's line id: it shields the
        /// arc between them. -1 for any other LoF.
        /// </summary>
        public int ChordOf { get; private set; }
        public bool IsChord { get { return ChordOf >= 0; } }

        public ForbiddanceBody(Line line, Segment segment, int chordOf = -1) : base(line)
        {
            Segment = segment;
            ChordOf = chordOf;
        }
    }

    /// <summary>A Line of Vigor in flight.</summary>
    public sealed class VigorBody : LineBody
    {
        public Vec2 Position { get; internal set; }
        /// <summary>Unit direction of travel.</summary>
        public Vec2 Direction { get; internal set; }
        /// <summary>Where it went during the last tick, for drawing its trail.</summary>
        public List<Vec2> LastPath { get; private set; }

        public VigorBody(Line line, Vec2 position, Vec2 direction) : base(line)
        {
            Position = position;
            Direction = direction.Normalized;
            LastPath = new List<Vec2>();
        }
    }

    /// <summary>A chalkling. Idle until chalkling behaviour arrives (build plan step 5), but it can be hit and pushed.</summary>
    public sealed class ChalklingBody : LineBody
    {
        public Vec2 Position { get; internal set; }
        /// <summary>The bind point a bound chalkling is tethered to.</summary>
        public Vec2? Tether { get; private set; }

        public ChalklingBody(Line line, Vec2 position, Vec2? tether) : base(line)
        {
            Position = position;
            Tether = tether;
        }
    }
}
