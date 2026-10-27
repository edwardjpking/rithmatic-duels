namespace RithmaticDuels.Presentation
{
    /// <summary>
    /// The version shown in the top bar of the game, so it is easy to tell whether Unity has picked up the
    /// latest changes. Bump it in every pull request that changes the game (see CLAUDE.md).
    /// </summary>
    public static class GameVersion
    {
        public const string Number = "0.17.1";
        public const string Date = "2026-10-27";
        /// <summary>What this version added, in a few words.</summary>
        public const string Summary = "smooth red cross, one-row buttons, ellipse fit, turning circles, Mark's Cross LoF, LoW touch only where bound";

        public static string Label { get { return "v" + Number + " (" + Date + ": " + Summary + ")"; } }
    }
}
