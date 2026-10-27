using CoherentWarAI.Behaviors;
using CoherentWarAI.Logic;
using CoherentWarAI.Settings;
using TaleWorlds.CampaignSystem;
using TaleWorlds.CampaignSystem.ComponentInterfaces;
using TaleWorlds.CampaignSystem.GameComponents;
using TaleWorlds.CampaignSystem.Party;
using TaleWorlds.Core;
using TaleWorlds.Library;
using TaleWorlds.Localization;

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
    public class CoherentArmyManagementModel : ArmyManagementCalculationModel
    {
        // Previously registered model of this type; protected member inherited via
        // MBGameModel<T>, accessed unqualified (see CoherentTargetScoreModel).
        private ArmyManagementCalculationModel Base => BaseModel;

        public string BaseModelName => Base?.GetType().Name;

        public override bool CanLordCreateArmy(MobileParty leaderParty, out MBList<MobileParty> possibleArmyMembers)
        {
            bool vanillaAllows = Base.CanLordCreateArmy(leaderParty, out possibleArmyMembers);
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

            // Decision D1 (Slice 2a): army raising keys on Offense specifically, not
            // "holds any domain" - a Defense or Naval holder is busy, not offensive,
            // and "one attacks, the rest defend" is the doctrine this model exists to
            // enforce. Feature off: today's expression, verbatim.
            bool holdsOffense = settings.EnableChainOfCommand
                ? CommandBehavior.HoldsDomain(leaderParty.LeaderHero, Domain.Offense)
                : WarPostureBehavior.IsMarshal(leaderParty);
            if (MarshalPlanner.MayRaiseArmy(holdsOffense, doctrineEnabled: true))
            {
                return true;
            }

            // Vetoed by doctrine: do not hand back a member list for an army that
            // will not be raised.
            possibleArmyMembers = null;
            return false;
        }

        // Forwarded to the previously registered model - no behaviour of our own.
        public override float AIMobilePartySizeRatioToCallToArmy => Base.AIMobilePartySizeRatioToCallToArmy;
        public override float PlayerMobilePartySizeRatioToCallToArmy => Base.PlayerMobilePartySizeRatioToCallToArmy;
        public override float MinimumNeededFoodInDaysToCallToArmy => Base.MinimumNeededFoodInDaysToCallToArmy;
        public override float MaximumDistanceToCallToArmy => Base.MaximumDistanceToCallToArmy;
        public override int InfluenceValuePerGold => Base.InfluenceValuePerGold;
        public override int AverageCallToArmyCost => Base.AverageCallToArmyCost;
        public override int CohesionThresholdForDispersion => Base.CohesionThresholdForDispersion;
        public override float MaximumWaitTime => Base.MaximumWaitTime;
        public override bool CanPlayerCreateArmy(out TextObject disabledReason) => Base.CanPlayerCreateArmy(out disabledReason);
        public override int CalculatePartyInfluenceCost(MobileParty armyLeaderParty, MobileParty party) => Base.CalculatePartyInfluenceCost(armyLeaderParty, party);
        public override float DailyBeingAtArmyInfluenceAward(MobileParty armyMemberParty) => Base.DailyBeingAtArmyInfluenceAward(armyMemberParty);
        public override int CalculateTotalInfluenceCost(Army army, float percentage) => Base.CalculateTotalInfluenceCost(army, percentage);
        public override float GetPartySizeScore(MobileParty party) => Base.GetPartySizeScore(party);
        public override bool CheckPartyEligibility(MobileParty party, out TextObject explanation) => Base.CheckPartyEligibility(party, out explanation);
        public override int GetPartyRelation(Hero hero) => Base.GetPartyRelation(hero);
        public override ExplainedNumber CalculateDailyCohesionChange(Army army, bool includeDescriptions = false) => Base.CalculateDailyCohesionChange(army, includeDescriptions);
        public override int CalculateNewCohesion(Army army, PartyBase newParty, int calculatedCohesion, int sign) => Base.CalculateNewCohesion(army, newParty, calculatedCohesion, sign);
        public override int GetCohesionBoostInfluenceCost(Army army, int percentageToBoost = 100) => Base.GetCohesionBoostInfluenceCost(army, percentageToBoost);
    }
}
