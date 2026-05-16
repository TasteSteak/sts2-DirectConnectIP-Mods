using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using Godot;

namespace DirectConnectIP.Helpers
{
    public class ModConfigManager
    {
        private const string ConfigFileName = "config.ini";
        private const string SectionProfile = "Profile";
        private const string SectionHistory = "ServerHistory";
        private const string SectionFeatures = "Features";
        private const int MaxHistoryCount = 3;

        private string _absoluteConfigPath;
        private readonly Random _random = new();

        private readonly string[] _presetNames = ["鸡煲大王", "鸡煲小王", "深情的鸡煲", "等我启动", "第五强"];

        public bool IsSteamAvailable { get; }

        public string LocalPlayerName { get; private set; }
        public ulong LocalPlayerId { get; private set; }
        public List<string> RecentServers { get; private set; } = [];

        public bool EnableOfflineTakeover { get; private set; }
        public bool EnableAndroidCompatFix { get; private set; }

        public ModConfigManager()
        {
            IsSteamAvailable = CheckSteamAvailability();
            
            InitializePath();
            LoadConfig();
        }

        private static bool CheckSteamAvailability()
        {
            try { return SteamIntegration.IsSteamInitialized(); }
            catch (Exception) { return false; }
        }

        private void InitializePath()
        {
            const string modUserPath = "user://mods/DirectConnectIP/";
            _absoluteConfigPath = ProjectSettings.GlobalizePath(modUserPath + ConfigFileName);

            var dir = Path.GetDirectoryName(_absoluteConfigPath);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
        }

        private void LoadConfig()
        {
            var config = new ConfigFile();
            if (File.Exists(_absoluteConfigPath) && config.Load(_absoluteConfigPath) == Error.Ok)
            {
                LocalPlayerId = config.GetValue(SectionProfile, "LocalPlayerId", GetSafePlayerId()).AsUInt64();
                LocalPlayerName = config.GetValue(SectionProfile, "LocalPlayerName", GetSafePlayerName()).AsString();

                EnableOfflineTakeover = config.GetValue(SectionFeatures, "EnableOfflineTakeover", true).AsBool();
                EnableAndroidCompatFix = config.GetValue(SectionFeatures, "EnableAndroidCompatFix", true).AsBool();

                RecentServers.Clear();
                for (var i = 0; i < MaxHistoryCount; i++)
                {
                    var addr = config.GetValue(SectionHistory, $"Server{i}", "").AsString();
                    if (!string.IsNullOrEmpty(addr)) RecentServers.Add(addr);
                }
            }
            else
            {
                LocalPlayerId = GetSafePlayerId();
                LocalPlayerName = GetSafePlayerName();
                EnableOfflineTakeover = true;
                EnableAndroidCompatFix = true;
                SaveConfig();
            }
        }

        private void SaveConfig()
        {
            var config = new ConfigFile();

            config.SetValue(SectionProfile, "LocalPlayerName", LocalPlayerName);
            config.SetValue(SectionProfile, "LocalPlayerId", LocalPlayerId);

            config.SetValue(SectionFeatures, "EnableOfflineTakeover", EnableOfflineTakeover);
            config.SetValue(SectionFeatures, "EnableAndroidCompatFix", EnableAndroidCompatFix);

            if (config.HasSection(SectionHistory)) config.EraseSection(SectionHistory);
            
            for (var i = 0; i < RecentServers.Count; i++)
            {
                config.SetValue(SectionHistory, $"Server{i}", RecentServers[i]);
            }

            var err = config.Save(_absoluteConfigPath);
            if (err != Error.Ok)
            {
                GD.PrintErr($"[DirectConnectIP] 无法保存配置到 {_absoluteConfigPath}: {err}");
            }
        }
        
        public void UpdateProfile(string name, ulong newId)
        {
            UpdateSettings(name, newId, EnableOfflineTakeover, EnableAndroidCompatFix);
        }

        public void UpdateSettings(string name, ulong newId, bool enableOfflineTakeover, bool enableAndroidCompatFix)
        {
            var changed = false;
            if (!string.IsNullOrWhiteSpace(name) && name != LocalPlayerName)
            {
                LocalPlayerName = name;
                changed = true;
            }
            if (newId != LocalPlayerId)
            {
                LocalPlayerId = newId;
                changed = true;
            }
            if (enableOfflineTakeover != EnableOfflineTakeover)
            {
                EnableOfflineTakeover = enableOfflineTakeover;
                changed = true;
            }
            if (enableAndroidCompatFix != EnableAndroidCompatFix)
            {
                EnableAndroidCompatFix = enableAndroidCompatFix;
                changed = true;
            }
            if (changed) SaveConfig();
        }

        public void AddSuccessfulServer(string address)
        {
            if (string.IsNullOrWhiteSpace(address)) return;
            RecentServers.Remove(address);
            RecentServers.Insert(0, address);
            if (RecentServers.Count > MaxHistoryCount)
            {
                RecentServers = RecentServers.Take(MaxHistoryCount).ToList();
            }
            SaveConfig();
        }

        private ulong GetSafePlayerId()
        {
            if (!IsSteamAvailable) return GenerateRandomId();
            try { if (SteamIntegration.TryGetSteamId(out var id)) return id; }
            catch (Exception) { }
            return GenerateRandomId();
        }

        private string GetSafePlayerName()
        {
            if (IsSteamAvailable)
            {
                try { if (SteamIntegration.TryGetSteamName(out var name) && !string.IsNullOrWhiteSpace(name)) return name; }
                catch (Exception) { }
            }
            var randomPreset = _presetNames[_random.Next(_presetNames.Length)];
            return $"{randomPreset}";
        }

        private ulong GenerateRandomId() => (ulong)_random.Next(100000, 1000000);

        private static class SteamIntegration
        {
            [MethodImpl(MethodImplOptions.NoInlining)]
            public static bool IsSteamInitialized() => MegaCrit.Sts2.Core.Platform.Steam.SteamInitializer.Initialized;

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static bool TryGetSteamId(out ulong id)
            {
                id = 0;
                if (!MegaCrit.Sts2.Core.Platform.Steam.SteamInitializer.Initialized) return false;
                id = Steamworks.SteamUser.GetSteamID().m_SteamID;
                return true;
            }

            [MethodImpl(MethodImplOptions.NoInlining)]
            public static bool TryGetSteamName(out string name)
            {
                name = null;
                if (!MegaCrit.Sts2.Core.Platform.Steam.SteamInitializer.Initialized) return false;
                name = Steamworks.SteamFriends.GetPersonaName();
                return true;
            }
        }
    }
}
