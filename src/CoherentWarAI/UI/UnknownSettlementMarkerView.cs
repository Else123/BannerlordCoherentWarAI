using SandBox.GauntletUI.Map;
using SandBox.View.Map;
using TaleWorlds.Engine.GauntletUI;

namespace CoherentWarAI.UI
{
    /// <summary>
    /// Draws a question mark over enemy settlements our kingdom has not recently
    /// scouted - the same fog-of-war knowledge the AI scores targets by, made
    /// visible, so a player can see why the AI is or is not marching on a fief.
    ///
    /// Derives from MapView directly and borrows the nameplate layer that the
    /// basic map view already owns. Subclassing GauntletMapBasicView would have
    /// been shorter, but that type creates its own layers in CreateLayout - a
    /// second instance would add a second copy of the whole map UI to the screen.
    /// This is purely additive: it registers a new view rather than overriding an
    /// existing one, so it coexists with other UI mods.
    /// </summary>
    public class UnknownSettlementMarkerView : MapView
    {
        private const string MovieName = "CoherentWarAIUnknownMarkers";

        private UnknownMarkersVM _dataSource;
        private GauntletMovieIdentifier _movie;

        protected override void CreateLayout()
        {
            base.CreateLayout();

            GauntletMapBasicView basicView = MapScreen.GetMapView<GauntletMapBasicView>();
            if (basicView == null || basicView.GauntletNameplateLayer == null)
            {
                // The map is not laid out the way we expect - most likely another
                // mod replaced the basic view. Draw nothing rather than guess.
                return;
            }

            Layer = basicView.GauntletNameplateLayer;
            _dataSource = new UnknownMarkersVM();
            _movie = basicView.GauntletNameplateLayer.LoadMovie(MovieName, _dataSource);
        }

        protected override void OnMapScreenUpdate(float dt)
        {
            base.OnMapScreenUpdate(dt);

            // Screen positions move every frame even when the marked set does not,
            // because the camera pans and zooms constantly. Only which settlements
            // are marked at all is throttled, over in MapMarkerBehavior.
            if (_dataSource == null || MapScreen == null || MapScreen.MapCameraView == null)
            {
                return;
            }
            _dataSource.Tick(MapScreen.MapCameraView.Camera);
        }

        protected override void OnFinalize()
        {
            base.OnFinalize();

            // The layer belongs to the basic view, so only the movie we added to it
            // is ours to release. Clearing Layer keeps the base from finalizing a
            // layer another view still owns.
            GauntletMapBasicView basicView = MapScreen != null ? MapScreen.GetMapView<GauntletMapBasicView>() : null;
            if (_movie != null && basicView != null && basicView.GauntletNameplateLayer != null)
            {
                basicView.GauntletNameplateLayer.ReleaseMovie(_movie);
            }

            _movie = null;
            _dataSource = null;
            Layer = null;
        }
    }
}
