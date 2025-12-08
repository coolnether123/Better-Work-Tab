using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Spine.UI.SettingsFramework
{
    /// <summary>
    /// Manages favorited settings. Pure C# for portability.
    /// Persists to a simple text file.
    /// </summary>
    public class FavoritesManager
    {
        private static FavoritesManager _instance;
        public static FavoritesManager Instance => _instance ?? (_instance = new FavoritesManager());

        private readonly HashSet<string> _favorites;
        private readonly string _persistPath;
        private bool _isDirty;

        public event Action OnFavoritesChanged;

        private FavoritesManager()
        {
            _favorites = new HashSet<string>();
            _persistPath = null;
            _isDirty = false;
        }

        public void Initialize(string configFolderPath)
        {
            var path = Path.Combine(configFolderPath, "BWT_FavoriteSettings.txt");
            Load(path);
        }

        public bool IsFavorite(string settingId)
        {
            return !string.IsNullOrEmpty(settingId) && _favorites.Contains(settingId);
        }

        public void SetFavorite(string settingId, bool isFavorite)
        {
            if (string.IsNullOrEmpty(settingId))
                return;

            bool changed;
            if (isFavorite)
            {
                changed = _favorites.Add(settingId);
            }
            else
            {
                changed = _favorites.Remove(settingId);
            }

            if (changed)
            {
                _isDirty = true;
                OnFavoritesChanged?.Invoke();
            }
        }

        public void ToggleFavorite(string settingId)
        {
            SetFavorite(settingId, !IsFavorite(settingId));
        }

        public IReadOnlyCollection<string> GetAllFavorites()
        {
            return _favorites.ToList().AsReadOnly();
        }

        public int FavoriteCount => _favorites.Count;

        public void Load(string path)
        {
            _favorites.Clear();

            if (!File.Exists(path))
                return;

            try
            {
                var lines = File.ReadAllLines(path);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                    {
                        _favorites.Add(trimmed);
                    }
                }
            }
            catch
            {
                // Silently fail on read errors
            }
        }

        public void Save(string path)
        {
            if (!_isDirty)
                return;

            try
            {
                File.WriteAllLines(path, _favorites.ToArray());
                _isDirty = false;
            }
            catch
            {
                // Silently fail on write errors
            }
        }

        public void SaveIfDirty(string configFolderPath)
        {
            if (_isDirty)
            {
                var path = Path.Combine(configFolderPath, "BWT_FavoriteSettings.txt");
                Save(path);
            }
        }
    }
}
