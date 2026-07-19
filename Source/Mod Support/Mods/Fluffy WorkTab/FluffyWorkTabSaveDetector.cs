using System.Xml;

namespace Better_Work_Tab.ModSupport.Mods.FluffyWorkTab
{
    internal readonly struct FluffyWorkTabSaveEvidence
    {
        internal FluffyWorkTabSaveEvidence(
            bool hasPriorityManager,
            bool hasFavouriteManager,
            bool listedInModMetadata)
        {
            HasPriorityManager = hasPriorityManager;
            HasFavouriteManager = hasFavouriteManager;
            ListedInModMetadata = listedInModMetadata;
        }

        internal bool HasPriorityManager { get; }
        internal bool HasFavouriteManager { get; }
        internal bool ListedInModMetadata { get; }
        internal bool Detected => HasPriorityManager || HasFavouriteManager || ListedInModMetadata;
    }

    /// <summary>
    /// Finds save-local evidence left by Fluffy Work Tab without requiring its assembly
    /// to be active. This intentionally ignores Fluffy's global favourites directory:
    /// global files cannot prove that a particular colony used the mod.
    /// </summary>
    internal static class FluffyWorkTabSaveDetector
    {
        internal const string PriorityManagerClass = "WorkTab.PriorityManager";
        internal const string FavouriteManagerClass = "WorkTab.FavouriteManager";

        internal static FluffyWorkTabSaveEvidence Detect(XmlDocument document)
        {
            if (document == null)
            {
                return default;
            }

            bool hasPriorityManager = HasComponent(document, PriorityManagerClass);
            bool hasFavouriteManager = HasComponent(document, FavouriteManagerClass);
            bool listedInModMetadata = false;
            XmlNodeList modIdNodes = document.SelectNodes("//meta/modIds/li");
            if (modIdNodes != null)
            {
                foreach (XmlNode modIdNode in modIdNodes)
                {
                    if (FluffyWorkTabIdentity.IsKnownPackageId(modIdNode?.InnerText))
                    {
                        listedInModMetadata = true;
                        break;
                    }
                }
            }

            return new FluffyWorkTabSaveEvidence(
                hasPriorityManager,
                hasFavouriteManager,
                listedInModMetadata);
        }

        private static bool HasComponent(XmlDocument document, string className)
        {
            return document.SelectSingleNode(
                "//components/li[@Class='" + className + "']") != null;
        }
    }
}
