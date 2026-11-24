using FishNet;
using FishNet.Transporting;
using RyanAssets.GameFeel;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// Client half of doctrines and the campaign: shows each hand the server deals, sends the pick,
    /// shows the pick landing on the field, keeps the campaign card current, fills the tier choice and
    /// tells the server when the round summary has been read. It only draws what the server sends;
    /// every rule runs on the server.
    /// </summary>
    public sealed class WV_DoctrinePresenter {
        readonly WV_HUD hud;
        int offerId;

        public WV_DoctrinePresenter(WV_HUD hud) {
            this.hud = hud;
            if (hud.DoctrinePanel != null) {
                hud.DoctrinePanel.CardChosen += Pick;
                hud.DoctrinePanel.RerollClicked += Reroll;
                hud.DoctrinePanel.Hide();
            }
            hud.SummaryClosed += SendSummaryRead;
        }

        public void Dispose() {
            if (hud != null)
                hud.SummaryClosed -= SendSummaryRead;
            if (hud != null && hud.DoctrinePanel != null) {
                hud.DoctrinePanel.CardChosen -= Pick;
                hud.DoctrinePanel.RerollClicked -= Reroll;
            }
        }

        public void HandleOffer(WV_DoctrineOffer offer, Channel channel) {
            offerId = offer.offerId;
            if (hud.DoctrinePanel == null)
                return;
            hud.DoctrinePanel.Show(offer);
            GameSfx.Play(SfxCue.Whoosh);
        }

        public void HandleState(WV_DoctrineState state, Channel channel) {
            WV_DoctrineDefinition picked = state.picked != byte.MaxValue && WV_DoctrineCatalog.IsValid(state.picked)
                ? WV_DoctrineCatalog.Get((WV_DoctrineId)state.picked) : null;
            byte[] ranks = state.ranks;
            bool flew = false;
            if (state.pendingOfferId == 0 || state.pendingOfferId != offerId) {
                offerId = state.pendingOfferId;
                if (hud.DoctrinePanel != null && state.pendingOfferId == 0) {
                    if (picked != null && hud.DoctrinePanel.Visible) {
                        // The chosen card flies to its slot on the campaign card, and the slot fills as it lands.
                        WV_CampaignCard card = hud.CampaignCard;
                        hud.DoctrinePanel.PlayPick(state.picked, card != null ? card.SlotFor(state.picked) : null,
                            () => {
                                if (card != null)
                                    card.SetDoctrines(ranks, state.picked);
                            });
                        flew = true;
                    } else {
                        hud.DoctrinePanel.Hide();
                    }
                }
            }
            if (!flew && hud.CampaignCard != null)
                hud.CampaignCard.SetDoctrines(ranks, state.picked);
            if (picked != null) {
                GameSfx.Play(picked.Rarity == WV_DoctrineRarity.Elite ? SfxCue.LevelUp : SfxCue.Purchase);
                hud.SetHint($"Doctrine: {picked.Title} - {picked.Description}");
            }
        }

        public void HandleCampaign(WV_CampaignStatus status, Channel channel) {
            if (hud.CampaignCard != null)
                hud.CampaignCard.SetStatus(status);
            if (hud.TierChoice != null)
                hud.TierChoice.SetStatus(status);
        }

        public void HandleBallot(WV_TierBallot ballot, Channel channel) {
            if (hud.TierChoice != null)
                hud.TierChoice.SetBallot(ballot);
        }

        public void HandleEffect(WV_DoctrineEffect effect, Channel channel) =>
            WV_DoctrineEffects.Play(effect, hud.HealthBars);

        public void HandleBriefing(WV_RoundBriefing briefing, Channel channel) {
            if (hud.TierChoice != null)
                hud.TierChoice.ShowChosen((WV_DifficultyTier)briefing.difficulty, briefing.difficultyName);
            if (hud.WaveDrama != null)
                hud.WaveDrama.ShowBriefing(briefing);
        }

        /// <summary>The commander is done with the round summary: the next vote need not wait for them.</summary>
        void SendSummaryRead() {
            if (InstanceFinder.ClientManager != null && InstanceFinder.ClientManager.Started)
                InstanceFinder.ClientManager.Broadcast(new WV_SummaryRead());
        }

        void Pick(int index) => Send(new WV_DoctrinePickRequest { offerId = offerId, choice = (byte)index });

        void Reroll() => Send(new WV_DoctrinePickRequest { offerId = offerId, reroll = true });

        void Send(WV_DoctrinePickRequest request) {
            if (offerId == 0 || InstanceFinder.ClientManager == null || !InstanceFinder.ClientManager.Started)
                return;
            GameSfx.Play(SfxCue.UiClick);
            if (hud.DoctrinePanel != null)
                hud.DoctrinePanel.Lock();
            InstanceFinder.ClientManager.Broadcast(request);
        }
    }
}
