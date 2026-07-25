using CoherentWarAI.Behaviors;
using CoherentWarAI.Logic;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Library;

namespace CoherentWarAI.Models
{
    /// <summary>
    /// Offensives are led, not improvised.
    ///
    /// Vanilla lets any lord raise an army whenever he fancies it, so a large realm
    /// puts out a scatter of small parties that are beaten one at a time. Only the
    /// lords appointed to lead a realm's offensives may raise a host here; everyone
    /// else is available to be called into one, which is what turns that scatter
    /// into a few armies worth the name.
    ///
    /// Defensive lords are unaffected - they were never going to raise an army - and
    /// the player is never restricted.
    /// </summary>
    public class CoherentArmyManagementModel : DefaultArmyManagementCalculationModel
    {
        public override bool CanLordCreateArmy(MobileParty leaderParty, out MBList<MobileParty> possibleArmyMembers)
        {
            bool vanillaAllows = base.CanLordCreateArmy(leaderParty, out possibleArmyMembers);
            if (!vanillaAllows)
            {
                return false;
            }

            CoherentWarAISettings settings = CoherentWarAISettings.Current;
            if (settings == null || !settings.EnableMarshalDoctrine)
            {
                return true;
            }

            // Never constrain the player's own clan - army raising is a decision the
            // player makes, not something for the AI doctrine to veto.
            if (leaderParty?.LeaderHero == null || leaderParty.LeaderHero.Clan == Clan.PlayerClan)
            {
                return true;
            }

            // Rulers stay free to raise the royal host even outside a marshal slot;
            // a kingdom that cannot muster under its own king would be stranger than
            // the scatter this is meant to fix.
            if (leaderParty.MapFaction?.Leader == leaderParty.LeaderHero)
            {
                return true;
            }

            // Only kingdoms appoint marshals. Landless or mercenary clans would
            // otherwise be permanently barred from ever forming a host.
            if (!(leaderParty.MapFaction is Kingdom))
            {
                return true;
            }

            if (MarshalPlanner.MayRaiseArmy(WarPostureBehavior.IsMarshal(leaderParty), doctrineEnabled: true))
            {
                return true;
            }

            // Vetoed by doctrine: do not hand back a member list for an army that
            // will not be raised.
            possibleArmyMembers = null;
            return false;
        }
    }
}
