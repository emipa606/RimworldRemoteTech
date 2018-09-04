﻿using System.Collections.Generic;
using System.Linq;
using System.Text;
using RimWorld;
using Verse;
using Verse.Sound;

namespace RemoteExplosives {
	/*
	 * Finds remote explosive charges in range and detonates them on command.
	 * Can be upgraded with a component to unlock the ability to use channels.
	 */
	public class Building_DetonatorTable : Building, IPawnDetonateable, IRedButtonFeverTarget {
		private const string ChannelsBasicUpgradeId = "ChannelsBasic";
		private const string ChannelsAdvancedUpgradeId = "ChannelsAdvanced";

		private CompUpgrade channelsBasic;
		private CompUpgrade channelsAdvanced;
		private CompChannelSelector channels;
		private CompPowerTrader power;

		// saved
		private bool wantDetonation;

		public override void ExposeData() {
			base.ExposeData();
			Scribe_Values.Look(ref wantDetonation, "wantDetonation");
		}

		public override void SpawnSetup(Map map, bool respawningAfterLoad) {
			base.SpawnSetup(map, respawningAfterLoad);
			channelsBasic = this.TryGetUpgrade(ChannelsBasicUpgradeId);
			channelsAdvanced = this.TryGetUpgrade(ChannelsAdvancedUpgradeId);
			channels = GetComp<CompChannelSelector>();
			power = GetComp<CompPowerTrader>();
			ConfigureChannelComp();
		}

		public override IEnumerable<Gizmo> GetGizmos() {
			Command detonate;
			if (CanDetonateImmediately()) {
				detonate = new Command_Action {
					action = DoDetonation,
					defaultLabel = "Detonator_detonateNow_label".Translate(),
				};
			} else {
				detonate = new Command_Toggle {
					toggleAction = DetonateToggleAction,
					isActive = () => wantDetonation,
					defaultLabel = "DetonatorTable_detonate_label".Translate(),
				};
			}
			detonate.icon = Resources.Textures.rxUIDetonate;
			detonate.defaultDesc = "DetonatorTable_detonate_desc".Translate();
			detonate.hotKey = Resources.KeyBinging.rxRemoteTableDetonate;
			yield return detonate;

			var c = channels?.GetChannelGizmo();
			if (c != null) yield return c;

			foreach (var g in base.GetGizmos()) {
				yield return g;
			}
		}

		private void DetonateToggleAction() {
			wantDetonation = !wantDetonation;
		}

		protected override void ReceiveCompSignal(string signal) {
			base.ReceiveCompSignal(signal);
			if(signal == CompUpgrade.UpgradeCompleteSignal) ConfigureChannelComp();
			if(signal == CompChannelSelector.ChannelChangedSignal) RemoteExplosivesUtility.ReportPowerUse(this, 2f);
		}

		private bool CanDetonateImmediately() {
			if (def.hasInteractionCell) {
				var manningPawn = InteractionCell.GetFirstPawn(Map);
				if (manningPawn != null && manningPawn.Drafted) return true;
			}
			return false;
		}

		private void ConfigureChannelComp() {
			var channelsLevel = RemoteExplosivesUtility.ChannelType.None;
			if (channelsAdvanced != null && channelsAdvanced.Complete) {
				channelsLevel = RemoteExplosivesUtility.ChannelType.Advanced;
			} else if (channelsBasic != null && channelsBasic.Complete) {
				channelsLevel = RemoteExplosivesUtility.ChannelType.Basic;
			}
			channels?.Configure(false, false, true, channelsLevel);
		}

		public bool UseInteractionCell {
			get { return true; }
		}

		public bool WantsDetonation {
			get { return wantDetonation; }
			set { wantDetonation = value; }
		}

		private bool IsPowered {
			get { return power == null || power.PowerOn; }
		}

		public void DoDetonation() {
			wantDetonation = false;
			if (!IsPowered) {
				PlayNeedPowerEffect();
				return;
			}
			RemoteExplosivesUtility.ReportPowerUse(this, 20f);
			SoundDefOf.FlickSwitch.PlayOneShot(this);
			RemoteExplosivesUtility.TriggerReceiversInNetworkRange(this, channels?.Channel ?? RemoteExplosivesUtility.DefaultChannel);
		}

		public override string GetInspectString() {
			if (!Spawned) return string.Empty;
			var stringBuilder = new StringBuilder();
			stringBuilder.Append(base.GetInspectString());
			if (channels != null) {
				channels.ChannelPopulation.TryGetValue(channels.Channel, out List<IWirelessDetonationReceiver> list);
				stringBuilder.AppendLine();
				stringBuilder.Append("DetonatorTable_inrange".Translate());
				stringBuilder.Append(": " + (list!=null?list.Count:0));
				stringBuilder.AppendLine();
				if (RemoteExplosivesUtility.GetChannelsUnlockLevel() > RemoteExplosivesUtility.ChannelType.None) {
					stringBuilder.Append(RemoteExplosivesUtility.GetCurrentChannelInspectString(channels.Channel));
				}
			}
			return stringBuilder.ToString();
		}

		// quick detonation option for drafted pawns
		public override IEnumerable<FloatMenuOption> GetFloatMenuOptions(Pawn selPawn) {
			var opt = RemoteExplosivesUtility.TryMakeDetonatorFloatMenuOption(selPawn, this);
			if (opt != null) yield return opt;

			foreach (var option in base.GetFloatMenuOptions(selPawn)) {
				yield return option;
			}
		}

		public bool RedButtonFeverCanInteract {
			get { return IsPowered; }
		}

		public void RedButtonFeverDoInteraction(Pawn p) {
			if (channels != null) {
				// switch to a non-empty channel
				var channelWithReceivers = channels.ChannelPopulation.Where(kv => kv.Value.Count > 0).Select(kv => kv.Key).RandomElementWithFallback(-1);
				if (channelWithReceivers > -1) {
					channels.Channel = channelWithReceivers;
				}
			}
			DoDetonation();
		}

		private void PlayNeedPowerEffect() {
			var info = SoundInfo.InMap(this);
			info.volumeFactor = 3f;
			SoundDefOf.Power_OffSmall.PlayOneShot(info);
		}
	}
}
