using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.Serialization.Formatters.Binary;
using BepInEx;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using LethalLib.Modules;
using LobbyCompatibility.Enums;
using LobbyCompatibility.Features;
using Unity.Collections;
using UnityEngine;
using Unity.Netcode;
using NetworkPrefabs = LethalLib.Modules.NetworkPrefabs;

namespace LethalCompanyVileVendingMachine;

[BepInPlugin(PluginInfo.PLUGIN_GUID, PluginInfo.PLUGIN_NAME, PluginInfo.PLUGIN_VERSION)]
[BepInDependency(LethalLib.Plugin.ModGUID)]
[BepInDependency("linkoid-DissonanceLagFix-1.0.0", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("mattymatty-AsyncLoggers-1.6.2", BepInDependency.DependencyFlags.SoftDependency)]
[BepInDependency("BMX.LobbyCompatibility", BepInDependency.DependencyFlags.SoftDependency)]
public class VileVendingMachinePlugin : BaseUnityPlugin
{
    public const string ModGuid = $"LCM_VileVendingMachine|{ModVersion}";
    private const string ModName = "Lethal Company Vile Vending Machine Mod";
    private const string ModVersion = "1.0.11";

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
        if (LobbyCompatibilityChecker.Enabled) LobbyCompatibilityChecker.Init();

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
        
        _harmony.PatchAll(typeof(VendingMachineRegistry));
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
        RegisterEnemyWithConfig(VileVendingMachineConfig.Instance.VendingMachineEnabled.Value, VileVendingMachineConfig.Instance.VendingMachineSpawnRarity.Value, _vileVendingMachineEnemyType, vileVendingMachineTerminalNode, vileVendingMachineTerminalKeyword);
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
        IEnumerable<Type> types;
        try
        {
            types = Assembly.GetExecutingAssembly().GetTypes();
        }
        catch (ReflectionTypeLoadException e)
        {
            types = e.Types.Where(t => t != null);
        }
        
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
    
    private void RegisterEnemyWithConfig(bool enemyEnabled, string configMoonRarity, EnemyType enemy, TerminalNode terminalNode, TerminalKeyword terminalKeyword) {
        if (enemyEnabled) { 
            (Dictionary<Levels.LevelTypes, int> spawnRateByLevelType, Dictionary<string, int> spawnRateByCustomLevelType) = ConfigParsing(configMoonRarity);
            Enemies.RegisterEnemy(enemy, spawnRateByLevelType, spawnRateByCustomLevelType, terminalNode, terminalKeyword);
                
        } else {
            Enemies.RegisterEnemy(enemy, 0, Levels.LevelTypes.All, terminalNode, terminalKeyword);
        }
    }

    // Got from the giant specimens mod
    private static (Dictionary<Levels.LevelTypes, int> spawnRateByLevelType, Dictionary<string, int> spawnRateByCustomLevelType) ConfigParsing(string configMoonRarity) {
        Dictionary<Levels.LevelTypes, int> spawnRateByLevelType = new();
        Dictionary<string, int> spawnRateByCustomLevelType = new();
        foreach (string entry in configMoonRarity.Split(',').Select(s => s.Trim())) {
            string[] entryParts = entry.Split(':');

            if (entryParts.Length != 2) continue;
            string name = entryParts[0];
            if (!int.TryParse(entryParts[1], out int spawnrate)) continue;

            if (Enum.TryParse(name, true, out Levels.LevelTypes levelType)) {
                spawnRateByLevelType[levelType] = spawnrate;
                Mls.LogDebug($"Registered spawn rate for level type {levelType} to {spawnrate}");
            } else {
                spawnRateByCustomLevelType[name] = spawnrate;
                Mls.LogDebug($"Registered spawn rate for custom level type {name} to {spawnrate}");
            }
        }
        return (spawnRateByLevelType, spawnRateByCustomLevelType);
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

public static class LobbyCompatibilityChecker 
{
    public static bool Enabled => BepInEx.Bootstrap.Chainloader.PluginInfos.ContainsKey("BMX.LobbyCompatibility");

    [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
    public static void Init() {
        PluginHelper.RegisterPlugin(PluginInfo.PLUGIN_GUID, Version.Parse(PluginInfo.PLUGIN_VERSION), CompatibilityLevel.Everyone, VersionStrictness.Patch);
    }
}