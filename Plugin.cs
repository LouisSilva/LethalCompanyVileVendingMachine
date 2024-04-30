using System;
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

namespace LethalCompanyVileVendingMachine;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInDependency(LethalLib.Plugin.ModGUID)]
[BepInDependency("linkoid-DissonanceLagFix-1.0.0", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("mattymatty-AsyncLoggers-1.6.2", BepInDependency.DependencyFlags.SoftDependency)]
public class VileVendingMachinePlugin : BaseUnityPlugin
{
    public const string ModGuid = $"LCM_VileVendingMachine|{ModVersion}";
    private const string ModName = "Lethal Company Vile Vending Machine Mod";
    private const string ModVersion = "1.0.6";

    private readonly Harmony _harmony = new(ModGuid);

    public static readonly ManualLogSource Mls = BepInEx.Logging.Logger.CreateLogSource(ModGuid);

    private static VileVendingMachinePlugin _instance;
    
    public static VileVendingMachineConfig VileVendingMachineConfig { get; internal set; }

    private static EnemyType _vileVendingMachineEnemyType;

    public static Item CompanyColaItem;
    public static Item CrushedCompanyColaItem;
    private static Item _plushieItem;
        
    private void Awake()
    {
        if (_instance == null) _instance = this;

        InitializeNetworkStuff();
            
        Assets.PopulateAssetsFromFile();
        if (Assets.MainAssetBundle == null)
        {
            Mls.LogError("MainAssetBundle is null");
            return;
        }
            
        _harmony.PatchAll();
        VileVendingMachineConfig = new VileVendingMachineConfig(Config);

        SetupVileVendingMachine();
        SetupCompanyCola();
        SetupCrushedCompanyCola();
        SetupPlushie();
            
        _harmony.PatchAll(typeof(VileVendingMachinePlugin));
        Mls.LogInfo($"Plugin {ModName} is loaded!");
    }

    private void SetupVileVendingMachine()
    {
        _vileVendingMachineEnemyType = Assets.MainAssetBundle.LoadAsset<EnemyType>("VileVendingMachine");
        _vileVendingMachineEnemyType.MaxCount = VileVendingMachineConfig.Instance.MaxAmount.Value;
            
        TerminalNode vileVendingMachineTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("VileVendingMachineTN");
        TerminalKeyword vileVendingMachineTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("VileVendingMachineTK");

        NetworkPrefabs.RegisterNetworkPrefab(_vileVendingMachineEnemyType.enemyPrefab);
        Utilities.FixMixerGroups(_vileVendingMachineEnemyType.enemyPrefab);
        Enemies.RegisterEnemy(
            _vileVendingMachineEnemyType,
            Mathf.Clamp(VileVendingMachineConfig.Instance.SpawnRate.Value, 0, 999),
            VileVendingMachineConfig.Instance.SpawnLevelTypes.Value,
            Enemies.SpawnType.Default,
            vileVendingMachineTerminalNode,
            vileVendingMachineTerminalKeyword
        );
    }

    private void SetupCompanyCola()
    {
        CompanyColaItem = Assets.MainAssetBundle.LoadAsset<Item>("CompanyColaItemData");
        if (CompanyColaItem == null)
        {
            Mls.LogError("Failed to load CompanyColaItemData from AssetBundle");
            return;
        }
            
        NetworkPrefabs.RegisterNetworkPrefab(CompanyColaItem.spawnPrefab);
        Utilities.FixMixerGroups(CompanyColaItem.spawnPrefab);
        Items.RegisterScrap(CompanyColaItem, 0, Levels.LevelTypes.All);
    }
    
    private void SetupCrushedCompanyCola()
    {
        CrushedCompanyColaItem = Assets.MainAssetBundle.LoadAsset<Item>("CrushedCompanyColaItemData");
        if (CrushedCompanyColaItem == null)
        {
            Mls.LogError("Failed to load CrushedCompanyColaItemData from AssetBundle");
            return;
        }
            
        NetworkPrefabs.RegisterNetworkPrefab(CrushedCompanyColaItem.spawnPrefab);
        Utilities.FixMixerGroups(CrushedCompanyColaItem.spawnPrefab);
        Items.RegisterScrap(CrushedCompanyColaItem, 0, Levels.LevelTypes.All);
    }

    private void SetupPlushie()
    {
        _plushieItem = Assets.MainAssetBundle.LoadAsset<Item>("VileVendingMachinePlushieItemData");
        if (_plushieItem == null)
        {
            Mls.LogError("Failed to load VileVendingMachinePlushieItemData from AssetBundle");
            return;
        }
        
        _plushieItem.minValue = Mathf.Clamp(VileVendingMachineConfig.Instance.PlushieMinValue.Value, 0, int.MaxValue);
        _plushieItem.maxValue = Mathf.Clamp(VileVendingMachineConfig.Instance.PlushieMaxValue.Value, 0, int.MaxValue);
            
        NetworkPrefabs.RegisterNetworkPrefab(_plushieItem.spawnPrefab);
        Utilities.FixMixerGroups(_plushieItem.spawnPrefab);
        Items.RegisterScrap(_plushieItem, Mathf.Clamp(VileVendingMachineConfig.Instance.PlushieSpawnRate.Value, 0, int.MaxValue), VileVendingMachineConfig.Instance.PlushieSpawnLevel.Value);
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

public class VileVendingMachineConfig : SyncedInstance<VileVendingMachineConfig>
{
    // Spawn values
    public readonly ConfigEntry<int> SpawnRate;
    public readonly ConfigEntry<int> MaxAmount;
    public readonly ConfigEntry<Levels.LevelTypes> SpawnLevelTypes;
    public readonly ConfigEntry<bool> CanSpawnAtMainDoorMaster;
    public readonly ConfigEntry<bool> CanSpawnAtFireExitMaster;
    public readonly ConfigEntry<bool> CanSpawnOutsideMaster;
    public readonly ConfigEntry<bool> CanSpawnInsideMaster;
    public readonly ConfigEntry<bool> AlwaysSpawnOutsideMainEntrance;
        
    public readonly ConfigEntry<float> InitialKillProbability;
    public readonly ConfigEntry<float> KillProbabilityGrowthFactor;
    public readonly ConfigEntry<float> KillProbabilityReductionFactor;
        
    public readonly ConfigEntry<float> SoundEffectsVolume;
        
    public readonly ConfigEntry<int> ColaMaxValue;
    public readonly ConfigEntry<int> ColaMinValue;
    public readonly ConfigEntry<int> CrushedColaMaxValue;
    public readonly ConfigEntry<int> CrushedColaMinValue;
    public readonly ConfigEntry<int> PlushieMaxValue;
    public readonly ConfigEntry<int> PlushieMinValue;
    public readonly ConfigEntry<int> PlushieSpawnRate;
    public readonly ConfigEntry<Levels.LevelTypes> PlushieSpawnLevel;

    public VileVendingMachineConfig(ConfigFile cfg)
    {
        InitInstance(this);
            
        SpawnRate = cfg.Bind(
            "Vending Machine Spawn Values",
            "Spawn Rate",
            20,
            "The spawn rate of the vending machine"
        );
            
        MaxAmount = cfg.Bind(
            "Vending Machine Spawn Values",
            "Max Amount",
            1,
            "The max amount of vending machines"
        );
            
        SpawnLevelTypes = cfg.Bind(
            "Vending Machine Spawn Values",
            "Spawn Level",
            Levels.LevelTypes.All,
            "The LevelTypes that the vending machine spawns in"
        );

        CanSpawnAtMainDoorMaster = cfg.Bind(
            "Vending Machine Spawn Values",
            "Can Spawn at Main Entrance",
            true,
            "Whether the vending machine can spawn at the main entrance"
        );
            
        CanSpawnAtFireExitMaster = cfg.Bind(
            "Vending Machine Spawn Values",
            "Can Spawn at Fire Exit",
            false,
            "Whether the vending machine can spawn at a fire exit"
        );
            
        CanSpawnOutsideMaster = cfg.Bind(
            "Vending Machine Spawn Values",
            "Can Spawn Outside",
            true,
            "Whether the vending machine can spawn outside"
        );
            
        CanSpawnInsideMaster = cfg.Bind(
            "Vending Machine Spawn Values",
            "Can Spawn Inside",
            true,
            "Whether the vending machine can spawn inside the dungeon"
        );
            
        AlwaysSpawnOutsideMainEntrance = cfg.Bind(
            "Vending Machine Spawn Values",
            "Always Spawn At Main Entrance First",
            true,
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
            "Item Spawn Values",
            "Company Cola Minimum Value",
            60,
            "The minimum possible value of a company cola"
        );
            
        ColaMaxValue = cfg.Bind(
            "Item Spawn Values",
            "Company Cola Maximum Value",
            90,
            "The maximum possible value of a company cola"
        );
        
        CrushedColaMinValue = cfg.Bind(
            "Item Spawn Values",
            "Crushed Company Cola Minimum Value",
            1,
            "The minimum possible value of a crushed company cola"
        );
            
        CrushedColaMaxValue = cfg.Bind(
            "Item Spawn Values",
            "Crushed Company Cola Maximum Value",
            5,
            "The maximum possible value of a crushed company cola"
        );
        
        PlushieMinValue = cfg.Bind(
            "Item Spawn Values",
            "Vile Vending Machine Plushie Minimum value",
            30,
            "The minimum value that the Vile Vending Machine Plushie can be set to"
        );
        
        PlushieMaxValue = cfg.Bind(
            "Item Spawn Values",
            "Vile Vending Machine Plushie Maximum value",
            75,
            "The maximum value that the Vile Vending Machine Plushie can be set to"
        );
        
        PlushieSpawnRate = cfg.Bind(
            "Item Spawn Values",
            "Vile Vending Machine Plushie Spawn Value",
            5,
            "The weighted spawn rarity of the Vile Vending Machine Plushie"
        );
        
        PlushieSpawnLevel = cfg.Bind(
            "Item Spawn Values",
            "Vile Vending Machine Plushie Spawn Level",
            Levels.LevelTypes.All,
            "The LevelTypes that the Vile Vending Machine Plushie spawns in"
        );

        SoundEffectsVolume = cfg.Bind(
            "Audio",
            "Sound Effects Volume",
            1f,
            "The volume of the sound effects from the vending machine"
        );
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
    
internal static class Assets
{
    private const string MainAssetBundleName = "vilevendingmachinebundle";
    public static AssetBundle MainAssetBundle;

    public static void PopulateAssetsFromFile()
    {
        if (MainAssetBundle != null) return;
        string assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (assemblyLocation != null)
        {
            MainAssetBundle = AssetBundle.LoadFromFile(Path.Combine(assemblyLocation, MainAssetBundleName));

            if (MainAssetBundle != null) return;
            string assetsPath = Path.Combine(assemblyLocation, "Assets");
            MainAssetBundle = AssetBundle.LoadFromFile(Path.Combine(assetsPath, MainAssetBundleName));
        }

        if (MainAssetBundle == null)
        {
            VileVendingMachinePlugin.Mls.LogError("Failed to load vilevendingmachine bundle");
        }
    }
}