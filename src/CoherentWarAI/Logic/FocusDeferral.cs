using System.Collections.Generic;

namespace CoherentWarAI.Logic
{
    /// <summary>
    /// Per-realm bookkeeping for the enemy-focus deferral. A realm whose enemies all
    /// read strength 0 (lords not yet on the map, or a permanently strengthless
    /// faction) has nothing to judge a focus by, so it is deferred and re-checked
    /// until one of its enemies becomes measurable. Realms are independent: one
    /// realm becoming measurable never settles another. There is deliberately no
    /// deadline - a realm stays deferred for as long as it has nothing measurable.
    /// </summary>
    public sealed class FocusDeferral<TRealm>
    {
        private readonly HashSet<TRealm> _deferred = new HashSet<TRealm>();

        /// <summary>Whether the realm is currently deferred.</summary>
        public bool IsDeferred(TRealm realm)
        {
            return _deferred.Contains(realm);
        }

        /// <summary>The currently deferred realms (snapshot, safe to mutate this tracker while iterating).</summary>
        public List<TRealm> DeferredRealms()
        {
            return new List<TRealm>(_deferred);
        }

        /// <summary>
        /// Records whether the realm is deferred now. Returns true only when this
        /// changes its state (so callers log transitions, not every hour).
        /// </summary>
        public bool Update(TRealm realm, bool deferred)
        {
            return deferred ? _deferred.Add(realm) : _deferred.Remove(realm);
        }

        /// <summary>Forgets every realm (focus switched off).</summary>
        public void Clear()
        {
            _deferred.Clear();
        }
    }

    /// <summary>The engine-free test for whether a realm has anything to judge a focus by.</summary>
    public static class FocusDeferral
    {
        /// <summary>Whether any enemy strength is above zero, i.e. a focus can be judged.</summary>
        public static bool HasMeasurableEnemy(IEnumerable<float> enemyStrengths)
        {
            if (enemyStrengths == null)
            {
                return false;
            }
            foreach (float strength in enemyStrengths)
            {
                if (strength > 0f)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
