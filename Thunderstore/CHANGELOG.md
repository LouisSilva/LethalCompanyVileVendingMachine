## 1.1.0
Updated the mod to work with v56:
* Uses the new KillPlayer() function needed.
* Fixed the bug caused by the new update where colas would fall through the floor.

Other fixes (not caused by v56):
* Fixed the spaghetti netcode, so the rare clientside bugs should now be fixed.

## 1.0.12
* Fixed bug where the vending machine wouldn't be able to spawn if it was spawned right at the beginning of the round (with its enemy type set to daytime for example).

## 1.0.11
* Added way better spawn rate config.
* Added BMX Lobby Compatibility as a soft dependency.

## 1.0.10
* Added another config option to specify if you want the vending machine to use the relaxed collider if needed.
* Fixed the bug where the list of doors wouldn't shuffle properly.
* Fixed the bug where the fire exit config options weren't being used properly by the code.

## 1.0.9
* The vending machine should finally, not **ever**, spawn inside a wall.
* Changed the power level to zero.

## 1.0.8
* Fixed bug where the player could scan the plushie while its in their hand.
* Added another config that lets the player decide what the vending machine determines as an expensive item.

## 1.0.7
* Refined the vending machine placement algorithm. Most bugs with it should be eradicated.
* Fixed bug where the vending machine registry didn't get cleared after the round has ended.

## 1.0.6
* Fixed bug where the vending machine will kill everyone instead of the player who deposited the item.
* Fixed bug where the cola wouldn't roll out the vending machine for clients.

## 1.0.5
* Fixed bug where it wouldn't let you gamble the cola with the lethal casino mod.
* Fixed naming mismatch with the vending machine scan node.
* Fixed bug where the player could scan a cola while its in their hand.
* Added a secret item :)

## 1.0.4
* Fixed plushie having the wrong icon in the toolbar.
* Fixed clients not being able to place an item on the hand.
* Fixed bug where the cola wasn't being animated properly out the flap.
* Fixed other very small bugs.

## 1.0.3
* Added a plushie.
* Gave the vending machine a higher chance of spawning earlier on in the day.
* Fixed bug that causes the EnableEnemyMesh function to break and then in turn break the vending machine.
* Fixed bug where you could see two scan nodes on the company cola.

## 1.0.2
* Fixed bug to do with the Vending Machine Registry not being able to handle null string ids.

## 1.0.1
* Added cached spawn values for Experimentation, Assurance, Vow and Titan.

## 1.0.0
* Initial Release.