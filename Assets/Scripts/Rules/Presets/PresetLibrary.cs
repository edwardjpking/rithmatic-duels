using System;
using System.Collections.Generic;
using RithmaticDuels.Rules.Geometry;

namespace RithmaticDuels.Rules.Presets
{
    /// <summary>
    /// Every bind point layout and defence a player has (DESIGN.md sections 6.1 and 6.8). Only the player's own
    /// are saved (by the presentation layer, to Application.persistentDataPath, outside the repo); the defaults
    /// ship with the game and are added by <see cref="WithDefaults"/>. Plain data so it can be saved as JSON.
    /// </summary>
    [Serializable]
    public sealed class PresetLibrary
    {
        /// <summary>Format version of the saved file.</summary>
        public int FormatVersion = 1;
        public List<BindPointLayout> Layouts = new List<BindPointLayout>();
        public List<DefencePreset> Presets = new List<DefencePreset>();

        /// <summary>The player's own layouts and defences, with the shipped defaults added in front.</summary>
        public PresetLibrary WithDefaults(RulesConfig config)
        {
            var all = new PresetLibrary { FormatVersion = FormatVersion };
            all.Layouts.AddRange(BindPointLayout.Defaults());
            all.Presets.AddRange(DefaultPresets.All(config));
            foreach (var layout in Layouts) if (!layout.IsDefault) all.Layouts.Add(layout);
            foreach (var preset in Presets) if (!preset.IsDefault) all.Presets.Add(preset);
            return all;
        }

        /// <summary>Just the player's own, for saving.</summary>
        public PresetLibrary CustomOnly()
        {
            var own = new PresetLibrary { FormatVersion = FormatVersion };
            foreach (var layout in Layouts) if (!layout.IsDefault) own.Layouts.Add(layout);
            foreach (var preset in Presets) if (!preset.IsDefault) own.Presets.Add(preset);
            return own;
        }

        public BindPointLayout FindLayout(string name) { return Layouts.Find(l => l.Name == name); }
        public DefencePreset FindPreset(string name) { return Presets.Find(p => p.Name == name); }

        /// <summary>Null if this layout can be saved (valid, and its name isn't taken by another), otherwise why not.</summary>
        public string LayoutSaveProblem(BindPointLayout layout, RulesConfig config, BindPointLayout replacing = null)
        {
            string problem = layout.Problem(config);
            if (problem != null) return problem;
            var existing = FindLayout(layout.Name);
            if (existing != null && existing != replacing) return "A layout with that name already exists.";
            return null;
        }

        /// <summary>Null if this defence can be saved (valid and buildable, and its name isn't taken), otherwise why not.</summary>
        public string PresetSaveProblem(DefencePreset preset, RulesConfig config, DefencePreset replacing = null)
        {
            var existing = FindPreset(preset.Name);
            if (existing != null && existing != replacing) return "A defence with that name already exists.";
            return DefenceCheck.Problem(preset, config);
        }

        /// <summary>Adds or replaces a layout. False (and nothing changes) if it can't be saved.</summary>
        public bool SaveLayout(BindPointLayout layout, RulesConfig config, BindPointLayout replacing = null)
        {
            if (layout.IsDefault || LayoutSaveProblem(layout, config, replacing) != null) return false;
            if (replacing != null) Layouts.Remove(replacing);
            Layouts.Add(layout.Clone());
            return true;
        }

        /// <summary>Adds or replaces a defence. False (and nothing changes) if it can't be saved.</summary>
        public bool SavePreset(DefencePreset preset, RulesConfig config, DefencePreset replacing = null)
        {
            if (preset.IsDefault || PresetSaveProblem(preset, config, replacing) != null) return false;
            if (replacing != null) Presets.Remove(replacing);
            Presets.Add(preset.Clone());
            return true;
        }
    }

    /// <summary>
    /// The defences that ship with the game, one per standard layout (DESIGN.md section 6.8). PROVISIONAL: these are
    /// interpretations of the descriptions in DESIGN.md sections 6.4 to 6.7, to be checked against the diagrams.
    /// </summary>
    public static class DefaultPresets
    {
        public static List<DefencePreset> All(RulesConfig config)
        {
            var layouts = BindPointLayout.Defaults();
            return new List<DefencePreset>
            {
                Ballintain(layouts.Find(l => l.Name == "Four-point circle")),
                Eskridge(layouts.Find(l => l.Name == "Six-point circle"), config),
                Easton(layouts.Find(l => l.Name == "Nine-point circle"), config),
                Jordan(layouts.Find(l => l.Name == "Ellipse"), config),
            };
        }

        private static DefencePreset New(string name, BindPointLayout layout)
        {
            return new DefencePreset { Name = name, Layout = layout.Clone(), IsDefault = true };
        }

        /// <summary>Two LoF joining neighbouring bind points, a LoW at two opposite bind points, a defensive chalkling at one of the others.</summary>
        public static DefencePreset Ballintain(BindPointLayout fourPoint)
        {
            var p = New("Ballintain", fourPoint);
            p.Components.Add(DefenceComponent.ForbiddanceBetween(1, BindRef.Main(0), BindRef.Main(1)));
            p.Components.Add(DefenceComponent.ForbiddanceBetween(2, BindRef.Main(2), BindRef.Main(3)));
            p.Components.Add(DefenceComponent.Ward(3, BindRef.Main(1), 4));
            p.Components.Add(DefenceComponent.Ward(4, BindRef.Main(3), 4));
            p.Components.Add(DefenceComponent.BoundChalkling(5, BindRef.Main(0)));
            return p;
        }

        /// <summary>
        /// Three LoF joining neighbouring bind points, defensive chalklings at two opposite bind points, and a LoW at
        /// the others, each with a LoF across it.
        /// </summary>
        public static DefencePreset Eskridge(BindPointLayout sixPoint, RulesConfig config)
        {
            var p = New("Eskridge", sixPoint);
            p.Components.Add(DefenceComponent.ForbiddanceBetween(1, BindRef.Main(0), BindRef.Main(1)));
            p.Components.Add(DefenceComponent.ForbiddanceBetween(2, BindRef.Main(2), BindRef.Main(3)));
            p.Components.Add(DefenceComponent.ForbiddanceBetween(3, BindRef.Main(4), BindRef.Main(5)));
            p.Components.Add(DefenceComponent.BoundChalkling(4, BindRef.Main(1)));
            p.Components.Add(DefenceComponent.BoundChalkling(5, BindRef.Main(4)));
            int id = 6;
            foreach (int at in new[] { 0, 2, 3, 5 })
            {
                int ward = id++;
                p.Components.Add(DefenceComponent.Ward(ward, BindRef.Main(at), 4, config.Geometry.AttachedWardMinRadius));
                p.Components.Add(DefenceComponent.ForbiddanceBetween(id++, new BindRef(ward, 1), new BindRef(ward, 3)));
            }
            return p;
        }

        /// <summary>LoF round six of the nine sides (three missing), then a LoW at every bind point (where one fits).</summary>
        public static DefencePreset Easton(BindPointLayout ninePoint, RulesConfig config)
        {
            var p = New("Easton", ninePoint);
            int id = 1;
            foreach (int start in new[] { 0, 1, 3, 4, 6, 7 })
                p.Components.Add(DefenceComponent.ForbiddanceBetween(id++, BindRef.Main(start), BindRef.Main(start + 1)));
            // A LoW at every bind point that has room for one with the current placeholder sizes.
            for (int at = 0; at < 9; at++)
            {
                p.Components.Add(DefenceComponent.Ward(id++, BindRef.Main(at), 4, config.Geometry.AttachedWardMinRadius));
                int unused;
                if (DefenceCheck.BuildProblem(p, config, out unused) != null) p.Components.RemoveAt(p.Components.Count - 1);
            }
            return p;
        }

        /// <summary>An anchor line through each end of the ellipse, a cage of LoF round it and chalklings inside.</summary>
        public static DefencePreset Jordan(BindPointLayout ellipse, RulesConfig config)
        {
            var p = New("Jordan", ellipse);
            float a = config.Geometry.EllipseSemiMajor;
            float b = config.Geometry.EllipseSemiMinor;
            float x = a + 0.5f, y = b + 1.5f; // within the longest LoF allowed
            int id = 1;
            p.Components.Add(DefenceComponent.ForbiddanceTangent(id++, BindRef.Main(0), 2f));
            p.Components.Add(DefenceComponent.ForbiddanceTangent(id++, BindRef.Main(1), 2f));
            p.Components.Add(DefenceComponent.ForbiddanceFree(id++, new Vec2(-x, y), new Vec2(x, y)));
            p.Components.Add(DefenceComponent.ForbiddanceFree(id++, new Vec2(-x, -y), new Vec2(x, -y)));
            p.Components.Add(DefenceComponent.ForbiddanceFree(id++, new Vec2(-x, -y), new Vec2(-x, y)));
            p.Components.Add(DefenceComponent.FreeChalkling(id++, new Vec2(0f, b + 0.75f), Glyph.Attack, 0, 0, 1));
            p.Components.Add(DefenceComponent.FreeChalkling(id++, new Vec2(0f, -b - 0.75f), Glyph.Attack, 0, 0, 1));
            p.Components.Add(DefenceComponent.ForbiddanceFree(id++, new Vec2(x, -y), new Vec2(x, y)));
            return p;
        }
    }
}
