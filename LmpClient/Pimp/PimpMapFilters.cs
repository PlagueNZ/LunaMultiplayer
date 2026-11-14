using LmpClient.Systems.SettingsSys;
using System;

namespace LmpClient.Pimp
{
    /// <summary>
    /// PiMP map filter memory (14 Nov 2026). KSP keeps the map's vessel-type filters (which icons
    /// show) in the save's flight state. LMP builds a blank game with a new flight state at every
    /// join (MainSystem.CreateBlankGame) and set the filters to its own default each time, so a
    /// player's choices were gone after every restart. Now the choice is kept in this install's LMP
    /// settings whenever it changes, and the blank game starts from it.
    /// </summary>
    internal static class PimpMapFilters
    {
        /// <summary>The state upstream LMP set at every join.</summary>
        private const int DefaultState = -1026;

        public static void Register() => GameEvents.OnMapViewFiltersModified.Add(OnModified);

        public static void Unregister() => GameEvents.OnMapViewFiltersModified.Remove(OnModified);

        /// <summary>The filter state a new blank game starts with.</summary>
        public static int StartState => SettingsSystem.CurrentSettings.MapViewFilterState != 0
            ? SettingsSystem.CurrentSettings.MapViewFilterState
            : DefaultState;

        private static void OnModified(MapViewFiltering.VesselTypeFilter filter)
        {
            try
            {
                var state = MapViewFiltering.GetFilterState();
                if (state == 0 || state == SettingsSystem.CurrentSettings.MapViewFilterState) return;
                SettingsSystem.CurrentSettings.MapViewFilterState = state;
                SettingsSystem.SaveSettings();
            }
            catch (Exception e)
            {
                LunaLog.LogError($"[LMP]: [PiMP] Map filters not saved: {e.Message}");
            }
        }
    }
}
