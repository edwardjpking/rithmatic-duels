using System.Linq;
using System.Reflection;
using NUnit.Framework;

namespace RithmaticDuels.Rules.Tests
{
    /// <summary>
    /// Guards the project layout from CLAUDE.md: the rules engine stays plain C#
    /// so it can be tested here and reused by UI and AI.
    /// </summary>
    public class AssemblySetupTests
    {
        [Test]
        public void RulesAssembly_DoesNotReferenceUnity()
        {
            var rules = Assembly.Load("RithmaticDuels.Rules");

            var unityReferences = rules.GetReferencedAssemblies()
                .Select(reference => reference.Name)
                .Where(name => name.StartsWith("UnityEngine") || name.StartsWith("UnityEditor"))
                .ToList();

            Assert.That(unityReferences, Is.Empty);
        }
    }
}
