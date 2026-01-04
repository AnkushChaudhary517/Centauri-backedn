using CentauriSeo.Core.Models.Enums;

namespace CentauriSeo.Core.Models.Utilities
{
    public static class StructureDetector
    {
        private static readonly string[] Coordinators = { " and ", " but ", " or ", ";" };
        private static readonly string[] Subordinators =
            { " because ", " although ", " since ", " if ", " when ", " which ", " that " };

        public static SentenceStructure Detect(string s)
        {
            bool hasCoordinator = Coordinators.Any(s.Contains);
            bool hasSubordinator = Subordinators.Any(s.Contains);

            if (!s.Contains(' ')) return SentenceStructure.Fragment;
            if (hasCoordinator && hasSubordinator) return SentenceStructure.CompoundComplex;
            if (hasSubordinator) return SentenceStructure.Complex;
            if (hasCoordinator) return SentenceStructure.Compound;

            return SentenceStructure.Simple;
        }
    }

}
