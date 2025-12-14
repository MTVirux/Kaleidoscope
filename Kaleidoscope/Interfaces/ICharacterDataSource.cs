namespace Kaleidoscope.Interfaces
{
    /// <summary>
    /// Interface for providing character selection data.
    /// Implement this interface to allow components like CharacterPickerWidget
    /// to work with different data sources.
    /// </summary>
    public interface ICharacterDataSource
    {
        /// <summary>
        /// Gets the list of available character IDs.
        /// </summary>
        List<ulong> AvailableCharacters { get; }

        /// <summary>
        /// Gets the currently selected character ID. 0 means "All" characters.
        /// </summary>
        ulong SelectedCharacterId { get; }

        /// <summary>
        /// Refreshes the list of available characters from the data source.
        /// </summary>
        void RefreshAvailableCharacters();

        /// <summary>
        /// Gets all stored character names from the data source.
        /// </summary>
        /// <returns>List of tuples containing character ID and name.</returns>
        List<(ulong cid, string? name)> GetAllStoredCharacterNames();

        /// <summary>
        /// Gets a display name for the provided character ID.
        /// </summary>
        /// <param name="characterId">The character ID to look up.</param>
        /// <returns>A human-readable display name, or the ID as a string if no name is found.</returns>
        string GetCharacterDisplayName(ulong characterId);

        /// <summary>
        /// Loads data for a specific character.
        /// </summary>
        /// <param name="characterId">The character ID to load data for.</param>
        void LoadForCharacter(ulong characterId);

        /// <summary>
        /// Loads aggregated data across all characters.
        /// </summary>
        void LoadAllCharacters();
    }
}
