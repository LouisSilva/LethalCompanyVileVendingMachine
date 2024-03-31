using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLib.Modules;
using Unity.Collections;
using UnityEngine;
using Unity.Netcode;
using NetworkPrefabs = LethalLib.Modules.NetworkPrefabs;

namespace LethalCompanyVileVendingMachine
{
    [BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
    [BepInDependency(LethalLib.Plugin.ModGUID)]
    public class VileVendingMachinePlugin : BaseUnityPlugin
    {
        public const string ModGuid = $"LCM_VolatileVendingMachine|{ModVersion}";
        private const string ModName = "Lethal Company Volatile Vending Machine Mod";
        private const string ModVersion = "1.0.0";

        private readonly Harmony _harmony = new(ModGuid);

        private readonly ManualLogSource _mls = BepInEx.Logging.Logger.CreateLogSource(ModGuid);

        private static VileVendingMachinePlugin _instance;
        
        public static VolatileVendingMachineConfig VolatileVendingMachineConfig { get; internal set; }

        private static EnemyType _volatileVendingMachineEnemyType;

        public static Item CompanyColaItem;
        
        private void Awake()
        {
            if (_instance == null) _instance = this;

            InitializeNetworkStuff();
            
            Assets.PopulateAssets();
            if (Assets.MainAssetBundle == null)
            {
                _mls.LogError("MainAssetBundle is null");
                return;
            }
            
            _harmony.PatchAll();
            VolatileVendingMachineConfig = new VolatileVendingMachineConfig(Config);

            SetupVolatileVendingMachine();
            SetupCompanyCola();
            
            _harmony.PatchAll(typeof(VileVendingMachinePlugin));
            _mls.LogInfo($"Plugin {ModName} is loaded!");
        }

        private void SetupVolatileVendingMachine()
        {
            _volatileVendingMachineEnemyType = Assets.MainAssetBundle.LoadAsset<EnemyType>("VolatileVendingMachine");
            _volatileVendingMachineEnemyType.PowerLevel = VolatileVendingMachineConfig.Instance.PowerLevel.Value;
            _volatileVendingMachineEnemyType.MaxCount = VolatileVendingMachineConfig.Instance.MaxAmount.Value;
            
            TerminalNode volatileVendingMachineTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("VolatileVendingMachineTN");
            TerminalKeyword volatileVendingMachineTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("VolatileVendingMachineTK");

            NetworkPrefabs.RegisterNetworkPrefab(_volatileVendingMachineEnemyType.enemyPrefab);
            Utilities.FixMixerGroups(_volatileVendingMachineEnemyType.enemyPrefab);
            Enemies.RegisterEnemy(
                _volatileVendingMachineEnemyType,
                Mathf.Clamp(VolatileVendingMachineConfig.Instance.SpawnRate.Value, 0, 999),
                VolatileVendingMachineConfig.Instance.SpawnLevelTypes.Value,
                Enemies.SpawnType.Default,
                volatileVendingMachineTerminalNode,
                volatileVendingMachineTerminalKeyword
                );
        }

        private void SetupCompanyCola()
        {
            CompanyColaItem = Assets.MainAssetBundle.LoadAsset<Item>("CompanyColaItemData");
            if (CompanyColaItem == null)
            {
                _mls.LogError("Failed to load CompanyColaItemData from AssetBundle");
                return;
            }
            
            NetworkPrefabs.RegisterNetworkPrefab(CompanyColaItem.spawnPrefab);
            Utilities.FixMixerGroups(CompanyColaItem.spawnPrefab);
            Items.RegisterScrap(CompanyColaItem, 0, Levels.LevelTypes.All);
        }
        
        private static void InitializeNetworkStuff()
        {
            Type[] types = Assembly.GetExecutingAssembly().GetTypes();
            foreach (Type type in types)
            {
                MethodInfo[] methods = type.GetMethods(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                foreach (MethodInfo method in methods)
                {
                    object[] attributes = method.GetCustomAttributes(typeof(RuntimeInitializeOnLoadMethodAttribute), false);
                    if (attributes.Length > 0)
                    {
                        method.Invoke(null, null);
                    }
                }
            }
        }
    }

    public class VolatileVendingMachineConfig : SyncedInstance<VolatileVendingMachineConfig>
    {
        // Spawn values
        public readonly ConfigEntry<int> SpawnRate;
        public readonly ConfigEntry<int> MaxAmount;
        public readonly ConfigEntry<int> PowerLevel;
        public readonly ConfigEntry<Levels.LevelTypes> SpawnLevelTypes;
        public readonly ConfigEntry<bool> CanSpawnAtMainDoorMaster;
        public readonly ConfigEntry<bool> CanSpawnAtFireExitMaster;
        public readonly ConfigEntry<bool> CanSpawnOutsideMaster;
        public readonly ConfigEntry<bool> CanSpawnInsideMaster;
        public readonly ConfigEntry<bool> AlwaysSpawnOutsideMainEntrance;
        
        // General
        public readonly ConfigEntry<float> InitialKillProbability;
        public readonly ConfigEntry<float> KillProbabilityGrowthFactor;
        public readonly ConfigEntry<float> KillProbabilityReductionFactor;
        

        public readonly ConfigEntry<int> ColaMaxValue;
        public readonly ConfigEntry<int> ColaMinValue;

        public VolatileVendingMachineConfig(ConfigFile cfg)
        {
            InitInstance(this);
            
            SpawnRate = cfg.Bind(
                "Spawn Values",
                "Spawn Rate",
                20,
                "The spawn rate of the vending machine"
            );
            
            MaxAmount = cfg.Bind(
                "Spawn Values",
                "Max Amount",
                1,
                "The max amount of vending machines"
            );
            
            PowerLevel = cfg.Bind(
                "Spawn Values",
                "Power Level",
                1,
                "The power level of the vending machine"
            );
            
            SpawnLevelTypes = cfg.Bind(
                "Spawn Values",
                "Spawn Level",
                Levels.LevelTypes.All,
                "The LevelTypes that the vending machine spawns in"
            );

            CanSpawnAtMainDoorMaster = cfg.Bind(
                "Spawn Values",
                "Can Spawn at Main Entrance",
                true,
                "Whether the vending machine can spawn at the main entrance"
                );
            
            CanSpawnAtFireExitMaster = cfg.Bind(
                "Spawn Values",
                "Can Spawn at Fire Exit",
                true,
                "Whether the vending machine can spawn at a fire exit"
            );
            
            CanSpawnOutsideMaster = cfg.Bind(
                "Spawn Values",
                "Can Spawn Outside",
                true,
                "Whether the vending machine can spawn outside"
            );
            
            CanSpawnInsideMaster = cfg.Bind(
                "Spawn Values",
                "Can Spawn Inside",
                true,
                "Whether the vending machine can spawn inside the dungeon"
            );
            
            AlwaysSpawnOutsideMainEntrance = cfg.Bind(
                "Spawn Values",
                "Always Spawn At Main Entrance First",
                false,
                "Whether a vending machine should always try to spawn outside the main entrance first, before considering fire exits or spawning inside the dungeon"
            );
            
            InitialKillProbability = cfg.Bind(
                "General",
                "Initial Kill Probability",
                0.01f,
                "The initial probability of the vending machine killing you when you give it an item"
            );
            
            KillProbabilityGrowthFactor = cfg.Bind(
                "General",
                "Kill Probability Growth Factor",
                4.64f,
                "How much the probability of the vending machine killing you goes up when giving it an item (exponential)"
            );
            
            KillProbabilityReductionFactor = cfg.Bind(
                "General",
                "Kill Probability Reduction Factor",
                0.25f,
                "How much the probability of the vending machine killing you goes down when giving it an expensive item (the current probability is multiplied by this number)"
            );
            
            ColaMinValue = cfg.Bind(
                "Spawn Values",
                "Company Cola Minimum Value",
                60,
                "The minimum possible value of a company cola"
            );
            
            ColaMaxValue = cfg.Bind(
                "Spawn Values",
                "Company Cola Maximum Value",
                90,
                "The maximum possible value of a company cola"
            );
        }
        
        private static void RequestSync() {
            if (!IsClient) return;

            using FastBufferWriter stream = new(IntSize, Allocator.Temp);
            MessageManager.SendNamedMessage($"{VileVendingMachinePlugin.ModGuid}_OnRequestConfigSync", 0uL, stream);
        }

        private static void OnRequestSync(ulong clientId, FastBufferReader _) {
            if (!IsHost) return;

            Debug.Log($"Config sync request received from client: {clientId}");

            byte[] array = SerializeToBytes(Instance);
            int value = array.Length;

            using FastBufferWriter stream = new(value + IntSize, Allocator.Temp);

            try {
                stream.WriteValueSafe(in value);
                stream.WriteBytesSafe(array);

                MessageManager.SendNamedMessage($"{VileVendingMachinePlugin.ModGuid}_OnReceiveConfigSync", clientId, stream);
            } catch(Exception e) {
                Debug.Log($"Error occurred syncing config with client: {clientId}\n{e}");
            }
        }

        private static void OnReceiveSync(ulong _, FastBufferReader reader) {
            if (!reader.TryBeginRead(IntSize)) {
                Debug.LogError("Config sync error: Could not begin reading buffer.");
                return;
            }

            reader.ReadValueSafe(out int val);
            if (!reader.TryBeginRead(val)) {
                Debug.LogError("Config sync error: Host could not sync.");
                return;
            }

            byte[] data = new byte[val];
            reader.ReadBytesSafe(ref data, val);

            SyncInstance(data);

            Debug.Log("Successfully synced config with host.");
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerControllerB), "ConnectClientToPlayerObject")]
        public static void InitializeLocalPlayer() {
            if (IsHost) {
                MessageManager.RegisterNamedMessageHandler($"{VileVendingMachinePlugin.ModGuid}_OnRequestConfigSync", OnRequestSync);
                Synced = true;

                return;
            }

            Synced = false;
            MessageManager.RegisterNamedMessageHandler($"{VileVendingMachinePlugin.ModGuid}_OnReceiveConfigSync", OnReceiveSync);
            RequestSync();
        }
        
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameNetworkManager), "StartDisconnect")]
        public static void PlayerLeave() {
            RevertSync();
        }
    }
    
    
    [Serializable]
    public class SyncedInstance<T>
    {
        internal static CustomMessagingManager MessageManager => NetworkManager.Singleton.CustomMessagingManager;
        internal static bool IsClient => NetworkManager.Singleton.IsClient;
        internal static bool IsHost => NetworkManager.Singleton.IsHost;
        
        [NonSerialized]
        protected static int IntSize = 4;

        public static T Default { get; private set; }
        public static T Instance { get; private set; }

        public static bool Synced { get; internal set; }

        protected void InitInstance(T instance) {
            Default = instance;
            Instance = instance;
            
            IntSize = sizeof(int);
        }

        internal static void SyncInstance(byte[] data) {
            Instance = DeserializeFromBytes(data);
            Synced = true;
        }

        internal static void RevertSync() {
            Instance = Default;
            Synced = false;
        }

        public static byte[] SerializeToBytes(T val) {
            BinaryFormatter bf = new();
            using MemoryStream stream = new();

            try {
                bf.Serialize(stream, val);
                return stream.ToArray();
            }
            catch (Exception e) {
                Debug.LogError($"Error serializing instance: {e}");
                return null;
            }
        }

        public static T DeserializeFromBytes(byte[] data) {
            BinaryFormatter bf = new();
            using MemoryStream stream = new(data);

            try {
                return (T) bf.Deserialize(stream);
            } catch (Exception e) {
                Debug.LogError($"Error deserializing instance: {e}");
                return default;
            }
        }
    }
    
    internal static class Assets
    {
        private const string MainAssetBundleName = "Assets.vilevendingmachinebundle";
        public static AssetBundle MainAssetBundle;

        private static string GetAssemblyName() => Assembly.GetExecutingAssembly().FullName.Split(',')[0];
        public static void PopulateAssets()
        {
            if (MainAssetBundle != null) return;
            using Stream assetStream = Assembly.GetExecutingAssembly().GetManifestResourceStream(GetAssemblyName() + "." + MainAssetBundleName);
            MainAssetBundle = AssetBundle.LoadFromStream(assetStream);
        }

        public static IEnumerator LoadAudioClipAsync(string clipName, Action<AudioClip> callback)
        {
            if (MainAssetBundle == null) yield break;

            AssetBundleRequest request = MainAssetBundle.LoadAssetAsync<AudioClip>(clipName);
            yield return request;
            
            AudioClip clip = request.asset as AudioClip;
            callback?.Invoke(clip);
        }
    }
}