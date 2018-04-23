# WorldStabilizer
A way to counter ground collision bug in KSP 1.3.1

Bug #16159: https://bugs.kerbalspaceprogram.com/issues/16159

Sometimes KSP places a vessel a little beneath the ground and activates the
physics engine. As a result the vessel is pushed out with significant force.
This mod captures the vessel before physics starts and does its best to 
move it just above the ground and carefully release.

As usual, drop the mod under GameData. If something goes wrong, check the
config file inside the mod dir and send a bugreport to whale2.box@gmail.com

BACKUP YOUR GAME BEFORE TRYING IT OUT!
