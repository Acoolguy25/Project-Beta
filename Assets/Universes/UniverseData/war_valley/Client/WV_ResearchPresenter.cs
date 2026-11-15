using System;
using System.Collections.Generic;
using System.Text;
using RyanAssets.Client.ClientUI.Build;
using UnityEngine;
using Universes.UniverseData.war_valley.Shared;

namespace Universes.UniverseData.war_valley.Client {
    /// <summary>
    /// What the local commander sees of research outside the research menu: an announcement on the
    /// hint line when a technology finishes, the economy card's "Researching ..." line, and the
    /// build-menu locks that depend on research.
    /// </summary>
    public sealed class WV_ResearchPresenter {
        /// <summary>How often the economy card's research line is rewritten while research runs.</summary>
        public const float SummaryInterval = 0.25f;

        readonly WV_HUD hud;
        readonly Func<int> localClientId;
        readonly Action researchChanged;

        /// <summary>Technologies the local commander already had, so only new completions are announced.</summary>
        readonly HashSet<WV_Tech> knownResearched = new();
        readonly StringBuilder summary = new();
        /// <summary>The research ledger <see cref="knownResearched"/> was taken from; a new round brings a new one.</summary>
        WV_Research trackedResearch;

        /// <param name="researchChanged">Called whenever research moves, for anything else that shows locks (the structure inspector).</param>
        public WV_ResearchPresenter(WV_HUD hud, Func<int> localClientId, Action researchChanged) {
            this.hud = hud;
            this.localClientId = localClientId;
            this.researchChanged = researchChanged;
        }

        /// <summary>
        /// Announces research the local commander has just finished and re-evaluates every lock that
        /// depends on it. The first reading of each round's ledger is taken silently, so joining a
        /// round (or the debug switch that starts it fully researched) does not flood the hint line.
        /// </summary>
        public void Refresh() {
            StructureMenu.NotifyAvailabilityChanged();
            researchChanged?.Invoke();

            WV_Research research = WV_Research.Instance;
            bool baseline = research != trackedResearch;
            trackedResearch = research;
            if (baseline)
                knownResearched.Clear();
            if (research == null)
                return;

            int clientId = localClientId();
            foreach (WV_TechDefinition definition in WV_TechTree.All) {
                if (!research.IsResearched(clientId, definition.Tech) || !knownResearched.Add(definition.Tech) || baseline)
                    continue;
                hud.SetHint($"Research complete: {definition.DisplayName} - {definition.Description}");
            }
        }

        /// <summary>The economy card's one-line view of what the local commander is researching.</summary>
        public void RefreshSummary() {
            WV_Research research = WV_Research.Instance;
            if (research == null) {
                hud.SetResearchSummary(null);
                return;
            }

            int clientId = localClientId();
            summary.Clear();
            foreach (WV_TechDefinition definition in WV_TechTree.All) {
                if (research.GetPhase(clientId, definition.Tech) != WV_ResearchPhase.Researching)
                    continue;
                summary.Append(summary.Length == 0 ? "Researching " : ", ")
                    .Append(definition.DisplayName).Append(' ')
                    .Append(Mathf.FloorToInt(research.GetProgress(clientId, definition.Tech) * 100f)).Append('%');
            }
            hud.SetResearchSummary(summary.Length > 0 ? summary.ToString() : null);
        }
    }
}
