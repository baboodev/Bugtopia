using System.Collections.Generic;

namespace HeartopiaMod
{
    public partial class HeartopiaComplete
    {
        private sealed class LiveFeatureStatusDetail
        {
            public string Label;
            public string Value;
        }

        private sealed class LiveFeatureStatusEntry
        {
            public string Label;
            public string Summary;
            public List<LiveFeatureStatusDetail> Details;
        }

        private List<LiveFeatureStatusEntry> CollectLiveFeatureStatusEntries()
        {
            List<LiveFeatureStatusEntry> entries = new List<LiveFeatureStatusEntry>(24);

            if (this.isRadarActive)
            {
                entries.Add(this.CreateLiveFeatureEntry("Radar", "Active"));
            }

            if (this.autoFarmActive)
            {
                LiveFeatureStatusEntry foraging = this.CreateLiveFeatureEntry(
                    "Foraging",
                    this.GetForagingModeLabel());
                foraging.Details = new List<LiveFeatureStatusDetail>
                {
                    new LiveFeatureStatusDetail
                    {
                        Label = "Status",
                        Value = this.GetForagingStatusDisplayText(false),
                    },
                };
                entries.Add(foraging);
            }
            else if (this.auraFarmEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Aura Farm", "Running"));
            }

            if (AutoFishingFarm.IsEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry(
                    "Fishing Farm",
                    "Active",
                    ("Status", AutoFishingFarm.GetLastStatus()),
                    ("Tool", AutoFishingFarm.GetLastToolStatus()),
                    ("Target", AutoFishingFarm.GetLastTargetStatus())));
            }

            if (InsectNetFarm.IsEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry(
                    "Insect Farm",
                    "Active",
                    ("Status", InsectNetFarm.GetLastStatus()),
                    ("Tool", InsectNetFarm.GetLastToolStatus()),
                    ("Caught", InsectNetFarm.GetSessionCatchCount().ToString())));
            }

            if (BirdNetFarm.IsEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry(
                    "Bird Farm",
                    "Active",
                    ("Status", BirdNetFarm.GetLastStatus()),
                    ("Tool", BirdNetFarm.GetLastToolStatus()),
                    ("Caught", BirdNetFarm.GetSessionCatchCount().ToString()),
                    ("Scared", BirdNetFarm.GetSessionScaredCount().ToString())));
            }

            if (this.netCookEnabled)
            {
                string massCookStatus = string.IsNullOrWhiteSpace(this.netCookStatus)
                    ? "Running"
                    : this.netCookStatus;
                entries.Add(this.CreateLiveFeatureEntry("Mass Cook", massCookStatus));
            }

            if (this.autoSellEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry(
                    "Auto Sell",
                    string.IsNullOrWhiteSpace(this.autoSellStatus) ? "Enabled" : this.autoSellStatus));
            }

            if (this.homelandFarmAutoRunning)
            {
                entries.Add(this.CreateLiveFeatureEntry(
                    "Homeland Farm",
                    string.IsNullOrWhiteSpace(this.homelandFarmLastStatus)
                        ? "Running"
                        : this.homelandFarmLastStatus));
            }

            if (this.puzzleAutoEnabled)
            {
                string puzzleStatus = this.puzzleSolveRunning
                    ? "Solving..."
                    : "Waiting for puzzle target...";
                entries.Add(this.CreateLiveFeatureEntry("Auto Puzzle", puzzleStatus));
            }

            if (this.petPlayAutoCatEnabled || this.petPlayAutoDogEnabled || this.petPlayAutoWashEnabled)
            {
                List<LiveFeatureStatusDetail> petDetails = new List<LiveFeatureStatusDetail>(3);
                if (this.petPlayAutoCatEnabled)
                {
                    petDetails.Add(new LiveFeatureStatusDetail { Label = "Cat Play", Value = "On" });
                }

                if (this.petPlayAutoDogEnabled)
                {
                    petDetails.Add(new LiveFeatureStatusDetail { Label = "Dog Train", Value = "On" });
                }

                if (this.petPlayAutoWashEnabled)
                {
                    petDetails.Add(new LiveFeatureStatusDetail { Label = "Pet Wash", Value = "On" });
                }

                LiveFeatureStatusEntry petCare = this.CreateLiveFeatureEntry("Pet Care", "Active");
                petCare.Details = petDetails;
                entries.Add(petCare);
            }

            if (this.autoCookEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Auto Cook", "Enabled"));
            }

            if (this.autoIceSkatingEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry(
                    "Auto Ice Skating",
                    string.IsNullOrWhiteSpace(this.autoIceSkatingLastStatus)
                        ? "Active"
                        : this.autoIceSkatingLastStatus));
            }

            if (this.iceSkatingSequenceCoroutine != null)
            {
                entries.Add(this.CreateLiveFeatureEntry(
                    "Ice Skating Seq",
                    string.IsNullOrWhiteSpace(this.iceSkatingSequenceLastStatus)
                        ? "Running..."
                        : this.iceSkatingSequenceLastStatus));
            }

            if (this.autoSnowEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Auto Snow", "Active"));
            }

            if (this.gameSpeed != 1.0f)
            {
                entries.Add(this.CreateLiveFeatureEntry("Speed", string.Format("{0:F1}x", this.gameSpeed)));
            }

            if (this.noclipEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Noclip", "Active"));
            }

            if (this.bypassOverlapEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Bypass Overlap", "Active"));
            }

            if (this.birdVacuumEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Bird Vacuum", "Active"));
            }

            if (this.autoJoinFriendEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Auto Join Friend", "Active"));
            }

            if (this.antiAfkEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Anti AFK", "Active"));
            }

            if (this.fastBubbleGenEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Fast Bubble Gen", "Active"));
            }

            if (this.bubbleSpawnAtPlayerEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Bubbles Spawn At Player", "Active"));
            }

            if (this.autoBubbleCollectEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Auto Collect Bubbles", "Active"));
            }

            if (this.bunnyHopEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Bunny Hop", "Active"));
            }

            if (this.analogMoveBridgeEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Analog Move", "Active"));
            }

            if (this.skipShowOffAnimations)
            {
                entries.Add(this.CreateLiveFeatureEntry("Skip Show Off", "Active"));
            }

            if (this.persistentHudEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Persistent HUD", this.persistentHudLastStatus));
            }

            if (this.forceSkateEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Force Skate", "Active"));
            }

            if (this.forceSwimEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Force Swim", "Active"));
            }

            if (this.strangerChatBypassEnabled)
            {
                entries.Add(this.CreateLiveFeatureEntry("Stranger Chat Bypass", "Active"));
            }

            if (this.chatForceTranslateEnabled)
            {
                string summary = this.chatTranslatePostcardBypass ? "Postcard test" : "Active";
                if (this.chatForceTranslateSentCount > 0)
                {
                    summary = "Sent " + this.chatForceTranslateSentCount + " / ok " + this.chatForceTranslateSucceededCount;
                }

                entries.Add(this.CreateLiveFeatureEntry("Chat Translate Unlock", summary));
            }

            return entries;
        }

        private LiveFeatureStatusEntry CreateLiveFeatureEntry(string label, string summary, params (string detailLabel, string detailValue)[] details)
        {
            LiveFeatureStatusEntry entry = new LiveFeatureStatusEntry
            {
                Label = label,
                Summary = summary,
            };

            if (details != null && details.Length > 0)
            {
                entry.Details = new List<LiveFeatureStatusDetail>(details.Length);
                for (int i = 0; i < details.Length; i++)
                {
                    entry.Details.Add(new LiveFeatureStatusDetail
                    {
                        Label = details[i].detailLabel,
                        Value = details[i].detailValue,
                    });
                }
            }

            return entry;
        }

        private int CountLiveFeatureStatusLines(IReadOnlyList<LiveFeatureStatusEntry> entries)
        {
            int lineCount = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                lineCount++;
                List<LiveFeatureStatusDetail> details = entries[i].Details;
                if (details != null)
                {
                    lineCount += details.Count;
                }
            }

            return lineCount;
        }

        private void ConsiderLiveFeatureStatusTextLengths(IReadOnlyList<LiveFeatureStatusEntry> entries, System.Action<string> consider)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                LiveFeatureStatusEntry entry = entries[i];
                consider(entry.Summary);
                List<LiveFeatureStatusDetail> details = entry.Details;
                if (details == null)
                {
                    continue;
                }

                for (int j = 0; j < details.Count; j++)
                {
                    consider(details[j].Value);
                }
            }
        }
    }
}
