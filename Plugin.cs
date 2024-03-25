using System;
using System.Collections;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization.Formatters.Binary;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using LethalLib.Modules;
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

        private static EnemyType _vileVendingMachineEnemyType;

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

            SetupVileVendingMachine();
            SetupCompanyCola();
            
            _harmony.PatchAll(typeof(VileVendingMachinePlugin));
            _mls.LogInfo($"Plugin {ModName} is loaded!");
        }

        private void SetupVileVendingMachine()
        {
            _vileVendingMachineEnemyType = Assets.MainAssetBundle.LoadAsset<EnemyType>("VileVendingMachine");

            TerminalNode vileVendingMachineTerminalNode = Assets.MainAssetBundle.LoadAsset<TerminalNode>("VileVendingMachineTN");
            TerminalKeyword vileVendingMachineTerminalKeyword = Assets.MainAssetBundle.LoadAsset<TerminalKeyword>("VileVendingMachineTK");

            NetworkPrefabs.RegisterNetworkPrefab(_vileVendingMachineEnemyType.enemyPrefab);
            Utilities.FixMixerGroups(_vileVendingMachineEnemyType.enemyPrefab);
            Enemies.RegisterEnemy(
                _vileVendingMachineEnemyType,
                999,
                Levels.LevelTypes.All,
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