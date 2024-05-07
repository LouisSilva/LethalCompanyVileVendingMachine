using BepInEx.Configuration;
using LethalLib.Modules;

namespace LethalCompanyVileVendingMachine;

public class VileVendingMachineConfig : SyncedInstance<VileVendingMachineConfig>
{
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
    public readonly ConfigEntry<int> ExpensiveItemValue;
        
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
            "How much the probability of the vending machine killing you goes down when giving it an expensive item (the current probability is multiplied by this number, so a reduction factor of 0.25 means the probability of being killed goes down by 75%)"
        );
        
        ExpensiveItemValue = cfg.Bind(
            "General",
            "Expensive Item Scrap Value",
            -1,
            "The scrap value of an item that is deemed 'expensive' by the vending machine. Leave it at -1 for the value to be automatically set to the maximum possible value of a cola"
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