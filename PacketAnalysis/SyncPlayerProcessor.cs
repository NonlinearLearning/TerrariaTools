using System;
using System.IO;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Terraria;
using Terraria.ID;
using Terraria.Localization;
using Terraria.Net;

namespace Terraria.PacketAnalysis
{
    public class SyncPlayerDependency
    {
        public BitsByte DifficultyAndExtraAccessoryFlags { get; set; }
        public BitsByte TorchAndAbilityFlags { get; set; }
        public BitsByte ConsumableFlags { get; set; }
        public int TargetPlayerId { get; set; }
    }

    public class SyncPlayerRawData
    {
        public int SkinVariant { get; set; }
        public int VoiceVariant { get; set; }
        public float VoicePitchOffset { get; set; }
        public int Hair { get; set; }
        public string Name { get; set; }
        public byte HairDye { get; set; }
        public bool[] HideVisibleAccessory { get; set; }
        public byte HideMisc { get; set; }
        public Color HairColor { get; set; }
        public Color SkinColor { get; set; }
        public Color EyeColor { get; set; }
        public Color ShirtColor { get; set; }
        public Color UnderShirtColor { get; set; }
        public Color PantsColor { get; set; }
        public Color ShoeColor { get; set; }
    }

    public class SyncPlayerProcessedData
    {
        public SyncPlayerRawData ValidatedData { get; set; }
        public int PlayerIndex { get; set; }
        public int Difficulty { get; set; }
        public bool ExtraAccessory { get; set; }
        public bool UsingBiomeTorches { get; set; }
        public bool HappyFunTorchTime { get; set; }
        public bool UnlockedBiomeTorches { get; set; }
        public bool UnlockedSuperCart { get; set; }
        public bool EnabledSuperCart { get; set; }
        public bool UsedAegisCrystal { get; set; }
        public bool UsedAegisFruit { get; set; }
        public bool UsedArcaneCrystal { get; set; }
        public bool UsedGalaxyPearl { get; set; }
        public bool UsedGummyWorm { get; set; }
        public bool UsedAmbrosia { get; set; }
        public bool AteArtisanBread { get; set; }
    }

    public class SyncPlayerProcessor : BasePacketProcessor<SyncPlayerDependency, SyncPlayerRawData, SyncPlayerProcessedData>
    {
        protected override SyncPlayerDependency Stage1_ReadDependencies(PacketContext context)
        {
            var reader = context.Reader;
            var dep = new SyncPlayerDependency();

            // Dependency Graph Building
            // In a real comprehensive system, we might register these into a graph object.
            // Here we read them as the "Pre-read list".

            dep.DifficultyAndExtraAccessoryFlags = reader.ReadByte();
            dep.TorchAndAbilityFlags = reader.ReadByte();
            dep.ConsumableFlags = reader.ReadByte();
            dep.TargetPlayerId = reader.ReadByte();

            return dep;
        }

        protected override SyncPlayerRawData Stage2_ReadOrdinaryFlow(PacketContext context, SyncPlayerDependency dependency)
        {
            var reader = context.Reader;
            var raw = new SyncPlayerRawData();

            // Note: We are NOT reading into Main.player[whoAmI] yet. We read into a temporary structure.
            raw.SkinVariant = reader.ReadByte();
            raw.VoiceVariant = reader.ReadByte();
            raw.VoicePitchOffset = reader.ReadSingle();
            raw.Hair = reader.ReadByte();
            raw.Name = reader.ReadString().Trim().Trim();
            raw.HairDye = reader.ReadByte();

            // ReadAccessoryVisibility equivalent logic
            ushort num = reader.ReadUInt16();
            int accessoryLength = 10;
            if (context.WhoAmI >= 0 && context.WhoAmI < Main.player.Length)
            {
                 accessoryLength = Main.player[context.WhoAmI].hideVisibleAccessory.Length;
            }
            raw.HideVisibleAccessory = new bool[accessoryLength];
            for (int i = 0; i < raw.HideVisibleAccessory.Length; i++)
            {
                raw.HideVisibleAccessory[i] = (num & (1 << i)) != 0;
            }

            raw.HideMisc = reader.ReadByte();
            raw.HairColor = reader.ReadRGB();
            raw.SkinColor = reader.ReadRGB();
            raw.EyeColor = reader.ReadRGB();
            raw.ShirtColor = reader.ReadRGB();
            raw.UnderShirtColor = reader.ReadRGB();
            raw.PantsColor = reader.ReadRGB();
            raw.ShoeColor = reader.ReadRGB();

            return raw;
        }

        protected override ProcessingResult<SyncPlayerProcessedData> Stage3_ProcessLogic(SyncPlayerRawData rawData, SyncPlayerDependency dependency, PacketContext context)
        {
            var result = new ProcessingResult<SyncPlayerProcessedData>();
            var processed = new SyncPlayerProcessedData();
            processed.ValidatedData = rawData;

            // Determine the actual player index to modify
            // If server, override TargetPlayerId with WhoAmI to prevent spoofing
            // Wait, original code:
            // if (Main.netMode == 2) targetPlayerId = whoAmI; (Implicitly handled by context.WhoAmI usage later in original code?)
            // Original code: "targetPlayer.whoAmI = whoAmI;"
            // Original code logic: "int targetPlayerId = reader.ReadByte();" then later "targetPlayerId = whoAmI;" is commented out in user snippet but usually enabled on server.
            // Actually, the snippet shows:
            // //targetPlayerId = whoAmI;
            // targetPlayer.whoAmI = whoAmI;

            // So we use context.WhoAmI for the player index to update.
            processed.PlayerIndex = context.WhoAmI;

            // Logic: Clamping
            rawData.SkinVariant = (int)MathHelper.Clamp(rawData.SkinVariant, 0f, PlayerVariantID.Count - 1);
            rawData.VoiceVariant = Utils.Clamp(rawData.VoiceVariant, 1, 4);
            rawData.VoicePitchOffset = Utils.Clamp(rawData.VoicePitchOffset, -1f, 1f);
            if (float.IsNaN(rawData.VoicePitchOffset)) rawData.VoicePitchOffset = 0f;
            if (rawData.Hair >= 228) rawData.Hair = 0;

            // Logic: Extract Flags
            processed.ExtraAccessory = dependency.DifficultyAndExtraAccessoryFlags[2];
            processed.UsingBiomeTorches = dependency.TorchAndAbilityFlags[0];
            processed.HappyFunTorchTime = dependency.TorchAndAbilityFlags[1];
            processed.UnlockedBiomeTorches = dependency.TorchAndAbilityFlags[2];
            processed.UnlockedSuperCart = dependency.TorchAndAbilityFlags[3];
            processed.EnabledSuperCart = dependency.TorchAndAbilityFlags[4];

            processed.UsedAegisCrystal = dependency.ConsumableFlags[0];
            processed.UsedAegisFruit = dependency.ConsumableFlags[1];
            processed.UsedArcaneCrystal = dependency.ConsumableFlags[2];
            processed.UsedGalaxyPearl = dependency.ConsumableFlags[3];
            processed.UsedGummyWorm = dependency.ConsumableFlags[4];
            processed.UsedAmbrosia = dependency.ConsumableFlags[5];
            processed.AteArtisanBread = dependency.ConsumableFlags[6];

            // Logic: Difficulty
            processed.Difficulty = 0;
            if (dependency.DifficultyAndExtraAccessoryFlags[0]) processed.Difficulty = 1;
            if (dependency.DifficultyAndExtraAccessoryFlags[1]) processed.Difficulty = 2;
            if (dependency.DifficultyAndExtraAccessoryFlags[3]) processed.Difficulty = 3;
            if (processed.Difficulty > 3) processed.Difficulty = 3;

            // Logic: Duplicate Name Check (Server Side)
            // Note: This requires access to global Main.player state, which is a dependency.
            // In a strict functional world, we'd pass this in. Here we access Main.player but treating it as read-only for this stage.
            bool isNameDuplicate = false;
            // Assuming Netplay.Clients is accessible
            if (Netplay.Clients[context.WhoAmI].State < 10)
            {
                for (int otherPlayerIndex = 0; otherPlayerIndex < 255; otherPlayerIndex++)
                {
                    if (otherPlayerIndex != dependency.TargetPlayerId &&
                        rawData.Name == Main.player[otherPlayerIndex].name &&
                        Netplay.Clients[otherPlayerIndex].IsActive)
                    {
                        isNameDuplicate = true;
                    }
                }
            }

            // Generate Side Effects based on validation
            if (isNameDuplicate)
            {
                result.SideEffects.Add(new SideEffect
                {
                    Description = "Send Error: Name Duplicate",
                    Execute = () => NetMessage.TrySendData(2, context.WhoAmI, -1, NetworkText.FromKey(Lang.mp[5].Key, rawData.Name))
                });
                result.Success = false; // Stop processing further updates? Original code sends error but doesn't explicitly return/break, but usually error implies no update?
                // Actually original code falls through to "else" for success case.
                // So if error, we do NOT update player.
            }
            else if (rawData.Name.Length > Player.nameLen)
            {
                 result.SideEffects.Add(new SideEffect
                {
                    Description = "Send Error: Name Too Long",
                    Execute = () => NetMessage.TrySendData(2, context.WhoAmI, -1, NetworkText.FromKey("Net.NameTooLong"))
                });
                result.Success = false;
            }
            else if (rawData.Name == "")
            {
                 result.SideEffects.Add(new SideEffect
                {
                    Description = "Send Error: Name Empty",
                    Execute = () => NetMessage.TrySendData(2, context.WhoAmI, -1, NetworkText.FromKey("Net.EmptyName"))
                });
                result.Success = false;
            }
            else if (processed.Difficulty == 3 && !Main.IsJourneyMode)
            {
                 result.SideEffects.Add(new SideEffect
                {
                    Description = "Send Error: Creative Player in Non-Creative World",
                    Execute = () => NetMessage.TrySendData(2, context.WhoAmI, -1, NetworkText.FromKey("Net.PlayerIsCreativeAndWorldIsNotCreative"))
                });
                result.Success = false;
            }
            else if (processed.Difficulty != 3 && Main.IsJourneyMode)
            {
                 result.SideEffects.Add(new SideEffect
                {
                    Description = "Send Error: Non-Creative Player in Creative World",
                    Execute = () => NetMessage.TrySendData(2, context.WhoAmI, -1, NetworkText.FromKey("Net.PlayerIsNotCreativeAndWorldIsCreative"))
                });
                result.Success = false;
            }
            else
            {
                // Success Path
                result.Success = true;
                result.Data = processed;

                // Define Side Effects for Success
                result.SideEffects.Add(new SideEffect
                {
                    Description = "Update Client Name",
                    Execute = () => {
                         Netplay.Clients[context.WhoAmI].Name = rawData.Name;
                    }
                });

                result.SideEffects.Add(new SideEffect
                {
                    Description = "Broadcast Player Sync",
                    Execute = () => {
                         NetMessage.TrySendData(4, -1, context.WhoAmI, null, dependency.TargetPlayerId);
                    }
                });

                // We also need a SideEffect to update the local Player object!
                // This was implicit in the original code by writing directly to targetPlayer.
                result.SideEffects.Insert(0, new SideEffect
                {
                    Description = "Update Local Player Object",
                    Execute = () => ApplyToPlayer(Main.player[context.WhoAmI], processed, rawData)
                });
            }

            return result;
        }

        private void ApplyToPlayer(Player player, SyncPlayerProcessedData processed, SyncPlayerRawData raw)
        {
            player.skinVariant = raw.SkinVariant;
            player.voiceVariant = raw.VoiceVariant;
            player.voicePitchOffset = raw.VoicePitchOffset;
            player.hair = raw.Hair;
            player.name = raw.Name;
            player.hairDye = raw.HairDye;

            // hideVisibleAccessory
            // Need to copy bool array to bitmask or however Player stores it.
            // Player.hideVisibleAccessory is a bool[] usually.
            for(int i=0; i<raw.HideVisibleAccessory.Length && i<player.hideVisibleAccessory.Length; i++)
            {
                player.hideVisibleAccessory[i] = raw.HideVisibleAccessory[i];
            }

            player.hideMisc = raw.HideMisc;
            player.hairColor = raw.HairColor;
            player.skinColor = raw.SkinColor;
            player.eyeColor = raw.EyeColor;
            player.shirtColor = raw.ShirtColor;
            player.underShirtColor = raw.UnderShirtColor;
            player.pantsColor = raw.PantsColor;
            player.shoeColor = raw.ShoeColor;

            player.whoAmI = processed.PlayerIndex;
            player.difficulty = processed.Difficulty;
            player.extraAccessory = processed.ExtraAccessory;

            player.UsingBiomeTorches = processed.UsingBiomeTorches;
            player.happyFunTorchTime = processed.HappyFunTorchTime;
            player.unlockedBiomeTorches = processed.UnlockedBiomeTorches;
            player.unlockedSuperCart = processed.UnlockedSuperCart;
            player.enabledSuperCart = processed.EnabledSuperCart;

            player.usedAegisCrystal = processed.UsedAegisCrystal;
            player.usedAegisFruit = processed.UsedAegisFruit;
            player.usedArcaneCrystal = processed.UsedArcaneCrystal;
            player.usedGalaxyPearl = processed.UsedGalaxyPearl;
            player.usedGummyWorm = processed.UsedGummyWorm;
            player.usedAmbrosia = processed.UsedAmbrosia;
            player.ateArtisanBread = processed.AteArtisanBread;
        }

        protected override void Stage4_ExecuteSideEffects(ProcessingResult<SyncPlayerProcessedData> result, PacketContext context)
        {
            foreach(var effect in result.SideEffects)
            {
                // In a real system we might log this:
                // Console.WriteLine($"Executing: {effect.Description}");
                effect.Execute();
            }
        }

        protected override void GenerateReport(PacketContext context, SyncPlayerDependency dependency, SyncPlayerRawData rawData, ProcessingResult<SyncPlayerProcessedData> result)
        {
            var reportPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PacketAnalysisReport.txt");
            var report = $@"
--- Packet Analysis Report [Message {context.MessageId}] ---
Timestamp: {DateTime.Now}
[Stage 1: Dependencies]
- TargetPlayerId: {dependency.TargetPlayerId}
- Flags Read: 3 bytes

[Stage 2: Raw Data]
- Name: {rawData?.Name}
- Hair: {rawData?.Hair}
- Colors Read: 7

[Stage 3: Logic]
- Success: {result.Success}
- Difficulty: {result.Data?.Difficulty}
- Side Effects Generated: {result.SideEffects.Count}

[Stage 4: Side Effects]
- Executed {result.SideEffects.Count} operations.
------------------------------------------------
";
            try
            {
                File.AppendAllText(reportPath, report);
            }
            catch { /* Ignore logging errors */ }
        }
    }
}
