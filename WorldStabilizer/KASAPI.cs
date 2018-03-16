using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace WorldStabilizer
{
	public class KASAPI
	{

		public static bool hasKISAddOn = false;
		private static Type KISType = null;
		private static string KISAddOnName = "KIS";
		private static string KISModuleName = "KIS.ModuleKISItem";
		private static MethodInfo groundDetachMethod;
		private static MethodInfo groundAttachMethod;


		public static bool hasKASAddOn = false;
		private static Type KASType = null;
		private static string KASAddOnName = "KAS";
		private static string KASHarpoonModuleName = "KAS.KASModuleHarpoon";

		public static Dictionary<Guid, List<PartModule>> pylons = null;
		public static Dictionary<Guid, List<PartModule>> harpoons = null;
		public static Dictionary<Guid, List<PartModule>> winches = null;
		public static Dictionary<PartModule, JointBackup> springs;
		public static HashSet<Rigidbody> harpoonsToHold = null;

		public KASAPI ()
		{
		}

		public struct JointBackup
		{
			public float spring;
			public float tolerance;
			public float maxDistance;
		}

		public static void initialize() {
			
			WorldStabilizer.printDebug ("Looking for KIS/KAS");
			KISType = findKISModule ();
			if (KISType != null) {

				groundDetachMethod = KISType.GetMethod ("GroundDetach");
				groundAttachMethod = KISType.GetMethod ("GroundAttach");

				if (groundAttachMethod != null && groundDetachMethod != null) {
					hasKISAddOn = true;
					WorldStabilizer.printDebug ("KIS found");
				}
			}

			KASType = findKASModule ();
			if (KASType != null) {
				hasKASAddOn = true;
				WorldStabilizer.printDebug ("KAS found");
			}

			if (harpoons == null)
				harpoons = new Dictionary<Guid, List<PartModule>> ();
			if (winches == null)
				winches = new Dictionary<Guid, List<PartModule>> ();
			if (pylons == null)
				pylons = new Dictionary<Guid, List<PartModule>> ();
			if (springs == null)
				springs = new Dictionary<PartModule, JointBackup> ();
			if (harpoonsToHold == null)
				harpoonsToHold = new HashSet<Rigidbody> ();
		}

		public static Type findKISModule() {
			foreach (AssemblyLoader.LoadedAssembly asm in AssemblyLoader.loadedAssemblies) {
				if (asm.name.Equals (KISAddOnName)) {

					return asm.assembly.GetType (KISModuleName);
				}
			}
			return null;
		}

		public static Type findKASModule() {
			foreach (AssemblyLoader.LoadedAssembly asm in AssemblyLoader.loadedAssemblies) {
				if (asm.name.Equals (KASAddOnName)) {

					return asm.assembly.GetType (KASHarpoonModuleName);
				}
			}
			return null;
		}

		public static void tryDetachPylon(Vessel v) {

			if(!hasKISAddOn)
				return;
			List<PartModule> attachedPylons = findAttachedKASPylons (v);
			if (attachedPylons.Count > 0) {
				pylons [v.id] = findAttachedKASPylons (v);
				foreach (PartModule pm in pylons[v.id]) {
					groundDetach (pm);
				}
				WorldStabilizer.printDebug ("Added " + pylons [v.id].Count + " for vessel " + v.name + "; id=" + v.id); 
			}
		}

		public static void tryAttachPylon(Vessel v) {
			if (pylons == null || !pylons.ContainsKey (v.id))
				return;
			foreach (PartModule pm in pylons[v.id]) {
				// Adding parasite module to the part
				// It will re-activate ground conneciton upon ground contact
				// and destroy itself afterwards
				WorldStabilizer.printDebug("partModule: " + pm);
				WorldStabilizer.printDebug("part: " + pm.name);
				WorldStabilizer.printDebug("Adding KASPylonReconnector to " + pm.part.name);
				pm.part.AddModule("KASPylonReconnector", true);
			}
			pylons.Remove (v.id);
		}


		public static List<PartModule> findAttachedKASPylons(Vessel v) {
			WorldStabilizer.printDebug ("Looking for KAS pylons attached to the ground in " + v.name);
			List<PartModule> pylonList = new List<PartModule> ();

			foreach (Part p in v.parts) {
				if (!p.Modules.Contains ("ModuleKISItem"))
					continue;
				PartModule pm = p.Modules ["ModuleKISItem"];
				if (isStaticAttached(pm)) {
					WorldStabilizer.printDebug (v.name + ": Found static attached KAS part " + p.name);
					pylonList.Add (pm);
				}
			}

			WorldStabilizer.printDebug ("Found " + pylonList.Count + " pylons");
			return pylonList;
		}

		public static bool isStaticAttached(PartModule pylon) {
			object val = pylon.Fields.GetValue ("staticAttached");
			if (val == null) {
				WorldStabilizer.printDebug("No 'staticAttached' field in part " + pylon.part.name);
				return false;
			}
			return (bool)val;
		}

		public static void groundAttach(PartModule pylon) {
			groundAttachMethod.Invoke (pylon, null);
		}

		public static void groundDetach(PartModule pylon) {
			WorldStabilizer.printDebug ("detaching pylon from the ground");
			groundDetachMethod.Invoke (pylon, null);
		}

		public static List<PartModule> findStuckHarpoons(Vessel v) {

			WorldStabilizer.printDebug ("Looking for harpoons stuck in the ground in " + v.name);
			List<PartModule> harpoonList = new List<PartModule> ();

			foreach (Part p in v.parts) {
				if (!p.Modules.Contains ("KASModuleHarpoon"))
					continue;
				PartModule pm = p.Modules ["KASModuleHarpoon"];

				if (!harpoonHasStaticJoint(pm))
					continue;
				WorldStabilizer.printDebug ("Adding haroon " + pm.GetInstanceID ());
				harpoonList.Add (pm);
			}
			WorldStabilizer.printDebug ("Found " + harpoonList.Count + " stuck harpoons");
			return harpoonList;
		}

		private static bool harpoonHasStaticJoint(PartModule harpoon) {

			FieldInfo attachModeField = harpoon.GetType ().GetField ("attachMode");
			object attachMode = attachModeField.GetValue (harpoon);
			if (attachMode == null) {
				WorldStabilizer.printDebug ("No attachMode field in harpoon part " + harpoon.part.name);
				return false;
			}
			FieldInfo staticJointField = attachMode.GetType ().GetField ("StaticJoint");
			return (bool)staticJointField.GetValue (attachMode);
		}

		private static PartModule findConnectedWinch(PartModule harpoon) {

			PartModule modulePort = harpoon.part.Modules ["KASModulePort"];
			FieldInfo winchField = modulePort.GetType ().GetField ("winchConnected");
			if (winchField == null) {
				WorldStabilizer.printDebug ("Can't find connected winch for part " + harpoon.part.name);
				return null;
			}
			return (PartModule)winchField.GetValue (modulePort);
		}

		public static FixedJoint getFixedJoint(PartModule harpoon) {

			FieldInfo staticAttachField = harpoon.GetType ().GetField ("StaticAttach");
			object staticAttach = staticAttachField.GetValue (harpoon);
			FieldInfo fixedJointField = staticAttach.GetType ().GetField ("fixedJoint");
			return (FixedJoint)fixedJointField.GetValue (staticAttach);
		}


		public static bool isPlugDocked(PartModule winch) {
			return (string)winch.Fields.GetValue ("headStateField") == "Plugged(Docked)";
		}


		// Toggles attached harpoon from 'docked' to 'undocked' state and vice versa
		private static void toggleDockedState(PartModule winch) {
			
			MethodInfo toggleDockedStateMethod = winch.GetType ().GetMethod ("TogglePlugMode");
			toggleDockedStateMethod.Invoke (winch, null);
		}

		private static void releaseWinchReel(PartModule winch, bool releaseState) {

			FieldInfo releaseField = winch.GetType ().GetField ("release");
			object release = releaseField.GetValue (winch);
			FieldInfo activeField = release.GetType ().GetField ("active");
			activeField.SetValue (release, releaseState);
		}

		public static void tryDetachHarpoon(Vessel v) {
			// New plan 
			//   - find winch connected to this attached harpoon 
			//   - set the winch to undocked state and release it 
			//   - upon ground contact set back to docked state
			if (!hasKASAddOn)
				return;

			List<PartModule> stuckHarpoons = findStuckHarpoons (v);
			if (stuckHarpoons.Count > 0) {
				harpoons [v.id] = stuckHarpoons;
				winches [v.id] = new List<PartModule> ();

				foreach (PartModule harpoon in harpoons[v.id]) {
					WorldStabilizer.printDebug ("Detaching harpoon " + harpoon.GetInstanceID ());

					// Black Magic starts here
					Type attachTypeEnum = Type.GetType ("KAS.KASModuleAttachCore+AttachType,KAS");
					MethodInfo detachMethodInfo = harpoon.GetType ().GetMethod ("Detach", new Type[] { attachTypeEnum });
					object[] param = new object[1];
					param [0] = 4; // Magic number - detach from the ground
					detachMethodInfo.Invoke (harpoon, param);

					PartModule winch = findConnectedWinch (harpoon);
					if (winch == null || !isPlugDocked (winch)) {
						WorldStabilizer.printDebug ("Can't find winch for harpoon " + harpoon.GetInstanceID ());
						continue;
					}

					SpringJoint springJoint = winch.part.gameObject.GetComponentInChildren<SpringJoint> ();
					if (springJoint != null) {
						
						JointBackup backupJoint = new JointBackup ();
						backupJoint.spring = springJoint.spring;
						backupJoint.tolerance = springJoint.tolerance;
						backupJoint.maxDistance = springJoint.maxDistance + 0.03f;
						springs [winch] = backupJoint;

						springJoint.spring = 0.0f;
						springJoint.tolerance = 3f;
						springJoint.maxDistance += 3f;

					} else {
						WorldStabilizer.printDebug ("Can't find springJoint in winch " + winch.GetInstanceID ());
					}


					WorldStabilizer.printDebug ("Adding winch " + winch.GetInstanceID () + " for harpoon " + harpoon.GetInstanceID ());
					toggleDockedState (winch);
					releaseWinchReel (winch, true);
					winches [v.id].Add (winch);

					Rigidbody rb = (Rigidbody)harpoon.part.GetComponentInChildren<Rigidbody> ();
					if (rb != null)
						harpoonsToHold.Add (rb);
				}
			}
		}

		public static void tryAttachHarpoonImmediately(Vessel v) {

			if (!winches.ContainsKey (v.id)) {
				WorldStabilizer.printDebug ("No winches for vessel " + v.name);
				return;
			}

			WorldStabilizer.printDebug ("re-attaching harpoons now");
			foreach (PartModule harpoon in harpoons[v.id]) {
				MethodInfo attachMethodInfo = harpoon.GetType ().GetMethod ("AttachStaticGrapple");
				attachMethodInfo.Invoke (harpoon, null);
			}

			foreach (PartModule winch in winches[v.id]) {

				SpringJoint springJoint = winch.part.gameObject.GetComponentInChildren<SpringJoint> ();
				if (springJoint == null) {
					WorldStabilizer.printDebug ("No springJoint found for winch " + winch.GetInstanceID ());
					continue;
				}
				if (springs.ContainsKey (winch)) {
					
					springJoint.spring = springs [winch].spring;
					springJoint.tolerance = springs [winch].tolerance;
					springJoint.maxDistance = springs [winch].maxDistance;
				}
				else
					WorldStabilizer.printDebug ("Can't find original spring force for winch " + winch.GetInstanceID ());
				releaseWinchReel (winch, false);
				toggleDockedState (winch);
			}
			winches.Remove (v.id);
			harpoons.Remove (v.id);
		}
			
		public static void holdHarpoon(Rigidbody b) {

			if (b == null)
				return;
			b.angularVelocity = Vector3.zero;
			b.velocity = Vector3.zero;
			b.Sleep ();
		}
	}
}

