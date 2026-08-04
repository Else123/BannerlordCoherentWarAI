using System.Collections.Generic;
using CoherentWarAI.Behaviors;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.Settlements;
using TaleWorlds.Engine;
using TaleWorlds.Library;
using TaleWorlds.MountAndBlade;

namespace CoherentWarAI.UI
{
    /// <summary>
    /// Data source for the unknown-settlement marker overlay: one item per
    /// marked settlement. The marked set itself comes from
    /// <see cref="MapMarkerBehavior"/> and only changes hourly; screen positions
    /// are re-projected every frame here because the camera moves every frame
    /// even when the set does not.
    /// </summary>
    public class UnknownMarkersVM : ViewModel
    {
        private readonly MBBindingList<UnknownSettlementMarkerItemVM> _markers
            = new MBBindingList<UnknownSettlementMarkerItemVM>();

        private readonly Dictionary<Settlement, UnknownSettlementMarkerItemVM> _bySettlement
            = new Dictionary<Settlement, UnknownSettlementMarkerItemVM>();

        [DataSourceProperty]
        public MBBindingList<UnknownSettlementMarkerItemVM> Markers
        {
            get { return _markers; }
        }

        /// <summary>Reconciles the marker list against the current marked set and re-projects every marker.</summary>
        public void Tick(Camera camera)
        {
            HashSet<Settlement> wanted = MapMarkerBehavior.MarkedSettlements;

            // Drop markers for settlements no longer marked (captured, made
            // peace, scouted again, or the feature was switched off mid-game).
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                Settlement settlement = _markers[i].Settlement;
                if (!wanted.Contains(settlement))
                {
                    _markers.RemoveAt(i);
                    _bySettlement.Remove(settlement);
                }
            }

            foreach (Settlement settlement in wanted)
            {
                if (!_bySettlement.ContainsKey(settlement))
                {
                    UnknownSettlementMarkerItemVM item = new UnknownSettlementMarkerItemVM(settlement);
                    _bySettlement[settlement] = item;
                    _markers.Add(item);
                }
            }

            for (int i = 0; i < _markers.Count; i++)
            {
                _markers[i].UpdateScreenPosition(camera);
            }
        }
    }

    /// <summary>One question-mark icon over one unscouted enemy settlement.</summary>
    public class UnknownSettlementMarkerItemVM : ViewModel
    {
        public Settlement Settlement { get; private set; }

        private float _screenX;
        private float _screenY;
        private bool _isVisibleOnScreen;

        public UnknownSettlementMarkerItemVM(Settlement settlement)
        {
            Settlement = settlement;
        }

        [DataSourceProperty]
        public float ScreenX
        {
            get
            {
                return _screenX;
            }
            set
            {
                if (_screenX != value)
                {
                    _screenX = value;
                    OnPropertyChangedWithValue(value, "ScreenX");
                }
            }
        }

        [DataSourceProperty]
        public float ScreenY
        {
            get
            {
                return _screenY;
            }
            set
            {
                if (_screenY != value)
                {
                    _screenY = value;
                    OnPropertyChangedWithValue(value, "ScreenY");
                }
            }
        }

        [DataSourceProperty]
        public bool IsVisibleOnScreen
        {
            get
            {
                return _isVisibleOnScreen;
            }
            set
            {
                if (_isVisibleOnScreen != value)
                {
                    _isVisibleOnScreen = value;
                    OnPropertyChangedWithValue(value, "IsVisibleOnScreen");
                }
            }
        }

        /// <summary>
        /// Rides above the settlement so the icon does not sit on top of its own
        /// nameplate. Matches the offset vanilla gives a town nameplate at a
        /// middling camera height.
        /// </summary>
        private const float HeightOffset = 4f;

        /// <summary>
        /// Must match SuggestedWidth in the prefab. The projection gives the point
        /// the settlement sits at; the icon is placed by its top-left corner, so
        /// half its width is taken off to centre it - the same correction vanilla's
        /// tracker widget applies.
        /// </summary>
        private const float IconWidth = 32f;

        /// <summary>
        /// Projects the settlement to screen space the same way vanilla's own
        /// SettlementNameplateVM does: terrain height under the settlement, a
        /// vertical offset, then world-to-screen.
        /// </summary>
        public void UpdateScreenPosition(Camera camera)
        {
            if (Settlement == null || camera == null || Campaign.Current == null
                || Campaign.Current.MapSceneWrapper == null)
            {
                IsVisibleOnScreen = false;
                return;
            }

            CampaignVec2 position = Settlement.Position;
            float height = 0f;
            Campaign.Current.MapSceneWrapper.GetHeightAtPoint(in position, ref height);

            Vec2 flat = position.ToVec2();
            Vec3 worldPosition = new Vec3(flat.X, flat.Y, height + HeightOffset, -1f);
            if (!worldPosition.IsValidXYZW)
            {
                IsVisibleOnScreen = false;
                return;
            }

            float x = 0f;
            float y = 0f;
            float w = 0f;
            MBWindowManager.WorldToScreenInsideUsableArea(camera, worldPosition, ref x, ref y, ref w);

            // Vanilla reads visibility off w alone: negative means the point is
            // behind the camera, where the projected x/y are meaningless.
            IsVisibleOnScreen = w >= 0f;
            ScreenX = x - IconWidth * 0.5f;
            ScreenY = y;
        }
    }
}
