using System;
using UnityEngine;
using System.Diagnostics;
using System.Collections.Generic;
using System.Linq;
using ModuleWheels;
using System.Reflection;
using KIS;
using KAS;
using System.Collections;

namespace WorldStabilizer
{
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class WorldStabilizer: MonoBehaviour
	{
		// configuration parameters

		// How many ticks do we hold the vessel
		public static int stabilizationTicks = 100;
		// How many ticks do we try to put the vessel down
		public static int groundingTicks = 3;

		// Should we stabilize vessels in PRELAUNCH state
		public static bool stabilizeInPrelaunch = true;
		// Should we stabilize Kerbals
		public static bool stabilizeKerbals = false;
		// Should we recalculate vessel bounds before every attempt to move it
		public static bool recalculateBounds = true;
		// Should we write debug info
		public static bool debug = true;
		// Should we draw markers around topmost and bottommost vessel points
		public static bool drawPoints = true;
		// Should we display 'World has been stabilized' message
		public static bool displayMessage = true;
		// When undocking harpoons, set joint force and torque to this value
		// Otherwise they are ripped off
		public static int harpoonReiforcedForce = 200;
		// How long to wait for harpoon reattaching after landing gear ground contact
		public static float harpoonReattachTimeout = 1;
		// How often to check for landed state 
		// (See GearHarpoonReconnector)
		public static float checkLandedPeriod = 0.5f;
		// If there was no ground contact on landing gear after waiting for this long
		// give up on harpoon reattachment
		public static float checkLandedTimeout = 10f;

		// If downmovement is below this value, leave the vessel as is
		public static float minDownMovement = 0.05f;
		// Minimum upmovement in case we're beneath the ground
		public static float upMovementStep = 0.2f;
		// Max upmovement in case upward movement is required; should cancel
		// moving the craft to space in case we messed the things up
		public static float maxUpMovement = 2.0f;

		private const int rayCastMask = (1 << 28) | (1 << 15) ;
		private const int rayCastExtendedMask = rayCastMask | 1;

		private int stabilizationTimer;
		private int count = 0;
		private Dictionary<Guid, int> vessel_timer;
		private Dictionary<Guid, LineRenderer> renderer0;
		private Dictionary<Guid, LineRenderer> renderer1;

		private Dictionary<Guid, VesselBounds> bounds;
		private Dictionary<Guid, List<PartModule>> anchors;
		private Dictionary<Guid, List<ModuleKISItem>> pylons;
		private Dictionary<Guid, List<KASModuleWinch>> winches;
		private Dictionary<Guid, List<KASModuleHarpoon>> harpoons;

		private List<string> excludeVessels;

		private bool hasKISAddOn = false;
		internal static string KISAddOnName = "KIS";
		private static string KISModuleName = "KIS.ModuleKISItem";

		private bool hasKASAddOn = false;
		internal static string KASAddOnName = "KAS";
		private static string KASHarpoonModuleName = "KAS.KASModuleHarpoon";

		public static EventVoid onWorldStabilizationStartEvent;
		public static EventVoid onWorldStabilizedEvent;

		public static WorldStabilizer instance;

		public WorldStabilizer ()
		{
			onWorldStabilizationStartEvent = new EventVoid ("onWorldStabilizationStart");
			onWorldStabilizedEvent = new EventVoid ("onWorldStabilized");
		}

		internal static Type findKISModule() {
			foreach (AssemblyLoader.LoadedAssembly asm in AssemblyLoader.loadedAssemblies) {
				if (asm.name.Equals (KISAddOnName)) {

					return asm.assembly.GetType (KISModuleName);
				}
			}
			return null;
		}

		internal static Type findKASModule() {
			foreach (AssemblyLoader.LoadedAssembly asm in AssemblyLoader.loadedAssemblies) {
				if (asm.name.Equals (KASAddOnName)) {

					return asm.assembly.GetType (KASHarpoonModuleName);
				}
			}
			return null;
		}

		public void Awake() {
			
			printDebug("Awake");

			instance = this;

			excludeVessels = new List<string>();
			configure ();

			printDebug("Looking for KIS");
			// Check if KIS present
			Type KISAddOnType = findKISModule();
			if (KISAddOnType != null) 
			{
				hasKISAddOn = true;
				printDebug ("KIS found"); 
			}

			printDebug("Looking for KAS");
			// Check if KAS present
			Type KASAddOnType = findKASModule();
			if (KASAddOnType != null) 
			{
				hasKASAddOn = true;
				printDebug ("KAS found"); 
			}
		}

		public void Start() {
			printDebug("Start");
			vessel_timer = new Dictionary<Guid, int> ();
			renderer0 = new Dictionary<Guid, LineRenderer> ();
			renderer1 = new Dictionary<Guid, LineRenderer> ();
			bounds = new Dictionary<Guid, VesselBounds> ();
			anchors = new Dictionary<Guid, List<PartModule>> ();
			pylons = new Dictionary<Guid, List<ModuleKISItem>> ();
			winches = new Dictionary<Guid, List<KASModuleWinch>> ();
			harpoons = new Dictionary<Guid, List<KASModuleHarpoon>> ();

			GameEvents.onVesselGoOffRails.Add (onVesselGoOffRails);
			GameEvents.onVesselGoOnRails.Add (onVesselGoOnRails);

			stabilizationTimer = stabilizationTicks; //(int)(stabilizationFactor / Time.fixedDeltaTime);  // If delta = 0.01, we will keep vessels 300 frames
		}

		public void OnDestroy() {
			printDebug("OnDestroy");
			GameEvents.onVesselGoOffRails.Remove (onVesselGoOffRails);
			GameEvents.onVesselGoOnRails.Remove (onVesselGoOnRails);
		}

		public void onVesselGoOnRails(Vessel v) {
			if (vessel_timer.ContainsKey (v.id) && vessel_timer [v.id] > 0) {
				vessel_timer [v.id] = 0;
				count--;
				if (drawPoints) {
					renderer0 [v.id].gameObject.DestroyGameObject ();
					renderer1 [v.id].gameObject.DestroyGameObject ();
				}
			}
		}
			
		public void onVesselGoOffRails(Vessel v) {

			if (v.situation == Vessel.Situations.LANDED ||
			    (stabilizeInPrelaunch && v.situation == Vessel.Situations.PRELAUNCH)) {

				printDebug ("off rails: " + v.name + ": packed: " + v.packed + "; loaded: " + 
					v.loaded + "; permGround: " + v.permanentGroundContact);
				if (v.isEVA && !stabilizeKerbals) // Kerbals are usually ok
					return;
				if (v.packed) // no physics, leave it alone
					return;
				if (checkExcludes (v)) // don't touch particular vessels 
					return;

				if (count == 0)
					onWorldStabilizationStartEvent.Fire ();

				vessel_timer [v.id] = stabilizationTimer;
				printDebug ("Timer = " + vessel_timer [v.id]);
				bounds [v.id] = new VesselBounds (v);
				if (drawPoints)
					initLR (v, bounds[v.id]);
				count++;
				tryDetachAnchor (v); // If this vessel has anchors (from Hangar), detach them
				tryDetachPylon(v); // Same with KAS pylons
				tryDetachHarpoon(v);
				moveUp (v);
				// Setting up attachment procedure early
				tryAttachPylon (v);
				tryAttachAnchor (v);
				scheduleHarpoonReattachment(v);
			}
		}

		public void FixedUpdate() {
			if (count == 0)
				return;

			foreach (Vessel v in FlightGlobals.VesselsLoaded) {
				if (vessel_timer.ContainsKey (v.id) && vessel_timer[v.id] > 0) {
					stabilize (v);
				}
				antiSlide (v);
			}
		}

		private void moveUp(Vessel v) {

			v.ResetGroundContact();
			v.ResetCollisionIgnores();

			// TODO: Try DisableSuspension() on wheels

			float upMovement = 0.0f;

			float vesselHeight = bounds [v.id].topLength + bounds [v.id].bottomLength;
			Vector3 up = (v.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;

			while (upMovement < maxUpMovement) {

				RayCastResult alt = GetRaycastAltitude (v, bounds [v.id].bottomPoint + up * vesselHeight, rayCastMask); // mask: ground only
				RayCastResult alt2 = GetRaycastAltitude (v, bounds [v.id].topPoint, rayCastMask); // mask: ground only

				printDebug (v.name + ": alt from top - height = " + (alt.altitude - vesselHeight) + 
					"; alt from top: " + alt + "; vessel height = " + vesselHeight);
				if (alt.altitude - vesselHeight < minDownMovement) {

					printDebug (v.name + ": hit colliders: " + alt + " and " + alt2);

					v.Translate (up * upMovementStep);
					printDebug ("Moving up: " + v.name + " by " + upMovementStep);
					upMovement += upMovementStep;
					
				} else {
					printDebug (v.name + ": minumum downmovement reached; alt from bottom: " + alt);
					break;
				}
			}	
			printDebug (v.name + "; new alt = " + GetRaycastAltitude(v, bounds[v.id].bottomPoint, rayCastMask ) +
				"; alt from top = " + GetRaycastAltitude (v, bounds [v.id].topPoint, rayCastMask));
		}

		private void moveDown(Vessel v) {

			if (recalculateBounds) {
				printDebug ("Recalculating bounds for vessel " + v.name + "; id=" + v.id);
				bounds [v.id].findBoundPoints ();
			}
			RayCastResult alt = GetRaycastAltitude (v, bounds[v.id].bottomPoint,  rayCastMask);
			RayCastResult alt3 = GetRaycastAltitude(v, bounds[v.id].topPoint, rayCastMask);

			if (alt.collider != alt3.collider) {
				printDebug (v.name + ": hit different colliders: " + alt + " and " + alt3 + "; refusing to move down");
				return;
			}

			// Re-cast raycast including parts into the mask
			alt = GetRaycastAltitude (v, bounds[v.id].bottomPoint,  rayCastExtendedMask);
			printDebug (v.name + ": raycast including parts; hit collider: " + alt);
			float downMovement = alt.altitude;

			Vector3 up = (v.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;

			if (downMovement < minDownMovement) {
				printDebug ("downmovement for " + v.name + " is below threshold; leaving as is: " + downMovement);
				return;
			}

			downMovement -= minDownMovement;

			printDebug ("Moving down: " + v.name + " by " + downMovement + "; alt = " + 
				alt.altitude + "; timer = " + vessel_timer[v.id] + "; radar alt = " + v.radarAltitude +
				"; alt from top = " + alt3.altitude);
			v.Translate (downMovement * -up);
		}

		private void stabilize(Vessel v) {

			if (vessel_timer [v.id] > stabilizationTimer - groundingTicks) // first 3(?) ticks
			{
				moveDown (v);
			}

			if (drawPoints) 
				updateLR (v, bounds [v.id]);

			v.IgnoreGForces(20);
			v.SetWorldVelocity (Vector3.zero);
			v.angularMomentum = Vector3.zero;
			v.angularVelocity = Vector3.zero;

			if (vessel_timer [v.id] % 10 == 0) {
				printDebug ("Stabilizing; v = " + v.name + "; radar alt = " + v.radarAltitude + "; timer = " + vessel_timer [v.id]);
			}
			vessel_timer [v.id]--;
			if (vessel_timer [v.id] == 0) {
				count--;
				printDebug ("Stopping stabilizing " + v.name);

				if (count == 0) {
					if (displayMessage)
						ScreenMessages.PostScreenMessage ("World has been stabilized");
					onWorldStabilizedEvent.Fire ();
				}
			}
		}
			
		private List<ModuleKISItem> findAttachedKASPylons(Vessel v) {
			printDebug ("Looking for KAS pylons attached to the ground in " + v.name);
			List<ModuleKISItem> pylonList = new List<ModuleKISItem> ();

			foreach (Part p in v.parts) {
				if (!p.Modules.Contains ("ModuleKISItem"))
					continue;
				ModuleKISItem pm = (ModuleKISItem)p.Modules ["ModuleKISItem"];
				if (pm.staticAttached) {
					printDebug (v.name + ": Found static attached KAS part " + p.name);
					pylonList.Add (pm);
				}
			}

			printDebug ("Found " + pylonList.Count + " pylons");
			return pylonList;
		}

		private void tryDetachPylon(Vessel v) {

			if (!hasKISAddOn)
				return;
			pylons [v.id] = findAttachedKASPylons (v);
			foreach (ModuleKISItem pm in pylons[v.id]) {
				pm.GroundDetach ();
			}
		}

		private void tryAttachPylon(Vessel v) {
			if (!pylons.ContainsKey (v.id))
				return;
			foreach (ModuleKISItem pm in pylons[v.id]) {
				// Adding parasite module to the part
				// It will re-activate ground conneciton upon ground contact
				// and destroy itself afterwards
				printDebug("Adding KASPylonReconnector to " + pm.part.name);
				pm.part.AddModule("KASPylonReconnector", true);
			}
			pylons.Remove (v.id);
		}

		private List<PartModule> findAnchoredParts(Vessel v) {

			printDebug ("Looking for anchors in " + v.name);
			List<PartModule> anchorList = new List<PartModule> ();

			foreach (Part p in v.parts) {
				foreach (PartModule pm in p.Modules) {
					if (pm.moduleName == "GroundAnchor") {
						printDebug (v.name + ": Found anchor on part " + p.name + "; attached = " + 
							pm.Fields.GetValue("isAttached"));
						if ((bool)pm.Fields.GetValue ("isAttached"))
							anchorList.Add (pm);
					}
				}
			}
			printDebug ("Found " + anchorList.Count + " anchors");

			return anchorList;
		}

		private void tryDetachAnchor(Vessel v) {
			
			anchors [v.id] = findAnchoredParts (v);
			foreach (PartModule pm in anchors[v.id]) {
				invokeAction (pm, "Detach anchor");
			}
		}

		private void tryAttachAnchor(Vessel v) {
			if (!anchors.ContainsKey (v.id))
				return;
			foreach (PartModule pm in anchors[v.id]) {
				// Adding parasite module to the part
				// It will re-activate ground conneciton upon ground contact
				// and destroy itself afterwards
				printDebug("Adding HangarReconnector to " + pm.part.name);
				pm.part.AddModule("HangarReconnector", true);
			}
			anchors.Remove (v.id);
		}

		private KASModuleWinch findConnectedWinch(PartModule harpoon) {

			KASModulePort modulePort = (KASModulePort)harpoon.part.Modules ["KASModulePort"];
			return modulePort.winchConnected;
		}

		private void tryDetachHarpoon(Vessel v) {
			// New plan 
			//   - find winch connected to this attached harpoon 
			//   - set the winch to undocked state and release it 
			//   - upon ground contact set back to docked state
			if (!hasKASAddOn)
				return;
			winches [v.id] = new List<KASModuleWinch> ();
			harpoons [v.id] = findStuckHarpoons (v);
			foreach (KASModuleHarpoon pm in harpoons[v.id]) {
				if (pm.StaticAttach.fixedJoint != null) {
					pm.StaticAttach.fixedJoint.breakForce = harpoonReiforcedForce;
					pm.StaticAttach.fixedJoint.breakTorque = harpoonReiforcedForce;
				}
				KASModuleWinch winch = findConnectedWinch (pm);
				if (winch == null || winch.headState != KASModuleWinch.PlugState.PlugDocked)
					continue;
				winch.ChangePlugMode(KASModuleWinch.PlugState.PlugUndocked);
				winch.release.active = true;
				winches [v.id].Add (winch);
			}
		}

		private void scheduleHarpoonReattachment(Vessel v) {
			if (!hasKASAddOn)
				return;
			if (!winches.ContainsKey (v.id) || winches[v.id].Count() == 0)
				return;
			// TODO: If bottom part is wheel/landing gear, OnCollisionEnter is not fired
			// TODO: Plus there should be some settle time.
			Part bottom = bounds [v.id].bottomPart;
			if (bottom.Modules.Contains ("ModuleWheelBase")) {
				bottom.AddModule ("GearHarpoonReconnector", true);
				printDebug ("Added GearHarpoonReconnector for part " + bottom.name);
			} else {
				bottom.AddModule ("HarpoonReconnector", true);
			}
		}

		internal void tryAttachHarpoon(Vessel v) {
			if (!winches.ContainsKey (v.id))
				return;
			StartCoroutine (this.tryAttachHarpoonCoro (v));
		}

		internal void tryAttachHarpoonImmediately(Vessel v) {
			
			if (winches.ContainsKey (v.id)) {
				printDebug ("re-attaching harpoons now");
				foreach (KASModuleWinch winch in winches[v.id]) {
					winch.ChangePlugMode (KASModuleWinch.PlugState.PlugDocked);
					winch.release.active = false;
				}
				foreach (KASModuleHarpoon harpoon in harpoons[v.id]) {
					harpoon.StaticAttach.fixedJoint.breakForce = harpoon.staticBreakForce;
					harpoon.StaticAttach.fixedJoint.breakTorque = harpoon.staticBreakForce;
				}
				winches.Remove (v.id);
				harpoons.Remove (v.id);
			}
		}

		private IEnumerator tryAttachHarpoonCoro(Vessel v) {
		
			printDebug ("re-attaching harpoons in " + harpoonReattachTimeout + " sec");
			yield return new WaitForSeconds (harpoonReattachTimeout);

			tryAttachHarpoonImmediately (v);	
		}

		private List<KASModuleHarpoon> findStuckHarpoons(Vessel v) {
			
			printDebug ("Looking for harpoons stuck in the ground in " + v.name);
			List<KASModuleHarpoon> harpoonList = new List<KASModuleHarpoon> ();

			foreach (Part p in v.parts) {
				if (p.name != "KAS.Hook.Harpoon")
					continue;
				if (!p.Modules.Contains ("KASModuleHarpoon"))
					continue;
				KASModuleHarpoon pm = (KASModuleHarpoon)p.Modules ["KASModuleHarpoon"];

				if (!pm.attachMode.StaticJoint)
					continue;
				harpoonList.Add (pm);
			}
			printDebug ("Found " + harpoonList.Count + " stuck harpoons");
			return harpoonList;
		}
			
		internal static void invokeAction(PartModule pm, string actionName) {
			printDebug ("Invoking action " + actionName + " on part " + pm.part.name);
			// https://forum.kerbalspaceprogram.com/index.php?/topic/65106-trigger-a-parts-action-from-code/
			BaseActionList bal = new BaseActionList(pm.part, pm); //create a BaseActionList bal with the available actions on the part. p being our current part, pm being our current partmodule
			if (bal.Count == 0)
				return;
			foreach (BaseAction ba in bal) //start cycling through baseActions in the BaseActionList
			{
				if (ba.guiName == actionName) //Trigger is a bool set to true via a GUI button on-screen (code not shown)
				{
					KSPActionParam ap = new KSPActionParam(KSPActionGroup.None, KSPActionType.Deactivate); //an important line, see post
					ba.Invoke(ap); //Invoke the "Extend Panel" command (our current ba. variable) with the ActionParameter from the previous line.
				}
			}
		}

		private void initLR(Vessel v, VesselBounds vbounds) {

			LineRenderer Lr0;
			Lr0 = new GameObject ().AddComponent<LineRenderer> ();
			Lr0.material = new Material (Shader.Find ("KSP/Emissive/Diffuse"));
			Lr0.useWorldSpace = true;
			Lr0.material.SetColor ("_EmissiveColor", Color.green);
			Lr0.SetWidth (0.15f, 0.15f);
			Lr0.SetVertexCount (4);
			Lr0.enabled = true;

			LineRenderer Lr1;
			Lr1 = new GameObject ().AddComponent<LineRenderer> ();
			Lr1.material = new Material (Shader.Find ("KSP/Emissive/Diffuse"));
			Lr1.useWorldSpace = true;
			Lr1.material.SetColor ("_EmissiveColor", Color.green);
			Lr1.SetWidth (0.15f, 0.15f);
			Lr1.SetVertexCount (4);
			Lr1.enabled = true;

			renderer0 [v.id] = Lr0;
			renderer1 [v.id] = Lr1;
			updateLR (v, vbounds);
		}

		private void updateLR(Vessel v, VesselBounds vbounds) {

			LineRenderer Lr0 = renderer0 [v.id];
			LineRenderer Lr1 = renderer1 [v.id];
			Lr0.SetPosition (0, vbounds.bottomPoint);
			Lr0.SetPosition (1, vbounds.bottomPoint + v.transform.TransformPoint(v.transform.forward));

			Lr0.SetPosition (2, vbounds.bottomPoint);
			Lr0.SetPosition (3, vbounds.bottomPoint + v.transform.TransformPoint(v.ReferenceTransform.right));
				//Vector3.ProjectOnPlane(v.CoM-FlightCamera.fetch.mainCamera.transform.position, vbounds.up).normalized);
			Lr1.SetPosition (0, vbounds.topPoint);
			Lr1.SetPosition (1, vbounds.topPoint + v.transform.TransformPoint(v.ReferenceTransform.forward));
			Lr1.SetPosition (2, vbounds.topPoint);
			Lr1.SetPosition (3, vbounds.topPoint + v.transform.TransformPoint(v.ReferenceTransform.right));
				//Vector3.ProjectOnPlane(v.CoM-FlightCamera.fetch.mainCamera.transform.position, vbounds.up).normalized);

			//printDebug ("line: " + vbounds.bottomPoint + " -> " + (vbounds.bottomPoint + v.transform.TransformPoint(v.transform.forward)));
			//printDebug ("line: " + vbounds.bottomPoint + " -> " + v.ReferenceTransform.TransformPoint(v.transform.forward));
		}

		public class Pair<T, U> {
			public Pair() {
			}

			public Pair(T first, U second) {
				this.First = first;
				this.Second = second;
			}

			public T First { get; set; }
			public U Second { get; set; }
		}

		public struct VesselBounds
		{

			public Vessel vessel;
			public float bottomLength;
			public float topLength;

			public Vector3 localBottomPoint;
			public Vector3 bottomPoint {
				get {
					return vessel.transform.TransformPoint(localBottomPoint);
				}
			}     

			public Vector3 localTopPoint;
			public Vector3 topPoint {
				get {
					return vessel.transform.TransformPoint (localTopPoint);
				}
			}

			public Vector3 up;
			public float maxSuspensionTravel;
			public Part bottomPart;

			public VesselBounds(Vessel v)
			{
				vessel = v;
				bottomLength = 0;
				topLength = 0;
				localBottomPoint = Vector3.zero;
				localTopPoint = Vector3.zero;
				up = Vector3.zero;
				maxSuspensionTravel = 0f;
				bottomPart = v.rootPart;
				findBoundPoints();
			}

			public void findBoundPoints() {

				Vector3 lowestPoint = Vector3.zero;
				Vector3 highestPoint = Vector3.zero;
				//float maxSqrDist = 0.0f;
				Part downwardFurthestPart = vessel.rootPart;
				Part upwardFurthestPart = vessel.rootPart;
				up = (vessel.CoM-vessel.mainBody.transform.position).normalized;
				Vector3 downPoint = vessel.CoM - (2000 * up);
				Vector3 upPoint = vessel.CoM + (2000 * up);
				Vector3 closestVert = vessel.CoM;
				Vector3 farthestVert = vessel.CoM;
				float closestSqrDist = Mathf.Infinity;
				float farthestSqrDist = Mathf.Infinity;

				foreach (Part p in vessel.parts) {

					if (p.Modules.Contains ("KASModuleHarpoon"))
						continue;

					HashSet<Pair<Transform, Mesh>> meshes = new HashSet<Pair<Transform, Mesh>>();
					foreach (MeshFilter filter in p.GetComponentsInChildren<MeshFilter>()) {

						Collider[] cdr = filter.GetComponentsInChildren<Collider> ();
						if (cdr.Length > 0 || p.Modules.Contains("ModuleWheelSuspension")) { 
							// for whatever reason suspension needs an additional treatment
							// TODO: Maybe address it by searching for wheel collider
							meshes.Add (new Pair<Transform, Mesh>(filter.transform,  filter.mesh));
						}
					}

					foreach (MeshCollider mcdr in p.GetComponentsInChildren<MeshCollider> ()) {
						meshes.Add(new Pair<Transform, Mesh>(mcdr.transform, mcdr.sharedMesh));
					}

					foreach (Pair<Transform, Mesh> meshpair in meshes) {
						Mesh mesh = meshpair.Second;
						Transform tr = meshpair.First;
						foreach (Vector3 vert in mesh.vertices) {
							//bottom check
							Vector3 worldVertPoint = tr.TransformPoint (vert);
							float bSqrDist = (downPoint - worldVertPoint).sqrMagnitude;
							if (bSqrDist < closestSqrDist) {
								closestSqrDist = bSqrDist;
								closestVert = worldVertPoint;
								downwardFurthestPart = p;

								// TODO: Not used at the moment, but we might infer amount of 
								// TODO: upward movement from this 
								// If this is a landing gear, account for suspension compression
								/*if (p.Modules.Contains ("ModuleWheelSuspension")) {
									ModuleWheelSuspension suspension = p.GetComponent<ModuleWheelSuspension> ();
									if (maxSuspensionTravel < suspension.suspensionDistance)
										maxSuspensionTravel = suspension.suspensionDistance;
									printDebug ("Suspension: dist=" + suspension.suspensionDistance + "; offset="
										+ suspension.suspensionOffset + "; pos=(" + suspension.suspensionPos.x + "; "
										+ suspension.suspensionPos.y + "; " + suspension.suspensionPos.z + ")");
								}*/
							}
							bSqrDist = (upPoint - worldVertPoint).sqrMagnitude;
							if (bSqrDist < farthestSqrDist) {
								farthestSqrDist = bSqrDist;
								farthestVert = worldVertPoint;
								upwardFurthestPart = p;
							}
						}
					}
				}

				bottomLength = Vector3.Project(closestVert - vessel.CoM, up).magnitude;
				localBottomPoint = vessel.transform.InverseTransformPoint(closestVert);
				topLength = Vector3.Project (farthestVert - vessel.CoM, up).magnitude;
				localTopPoint = vessel.transform.InverseTransformPoint (farthestVert);
				bottomPart = downwardFurthestPart;
				try {
					printDebug ("vessel = " + vessel.name + "; furthest downward part = " + downwardFurthestPart.name +
						"; upward part = " + upwardFurthestPart.name);

					printDebug ("vessel = " + vessel.name + "; bottomLength = " + bottomLength + "; bottomPoint = " +
						bottomPoint + "; topLength = " + topLength + "; topPoint = " + topPoint);
				}
				catch(Exception e) {
					printDebug ("Can't print vessel stats: " + e.ToString());
				}
			}
		}

		void antiSlide(Vessel v) {

			if (v.srfSpeed < 0.1) {
				v.SetWorldVelocity (Vector3.zero);
				v.angularMomentum = Vector3.zero;
				v.angularVelocity = Vector3.zero;
			}
		}

		public class RayCastResult
		{
			public Collider collider;
			public float altitude;

			public RayCastResult() {
				collider = null;
				altitude = 0.0f;
			}

			public override string ToString() {

				return "(alt = " + altitude + "; collider = " + (collider != null ? collider.name : "no hit)");
			}
		}

		private RayCastResult GetRaycastAltitude(Vessel v, Vector3 originPoint, int layerMask) 
		{
			RaycastHit hit;
			Vector3 up = (v.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
			RayCastResult result = new RayCastResult ();
			if(Physics.Raycast(originPoint, -up, out hit, v.vesselRanges.landed.unload, layerMask))
			{
				//printDebug (v.name + ": raycast mask: " + layerMask + "; hit collider: " + hit.collider.name);
				result.altitude = Vector3.Project(hit.point - originPoint, up).magnitude;
				result.collider = hit.collider;
			}
			return result;
		}

		private bool checkExcludes(Vessel v) {
			foreach (Part p in v.parts) {
				if (p.Modules.Contains ("LaunchClamp"))
					return true;
				if (p.Modules.Contains ("FlagSite"))
					return true;
				if (excludeVessels.Contains(v.GetName())) {
					printDebug(v.name + ": in exclusion list");
					return true;
				}
				// TODO: Check if there's KAS port in attached, but undocked state
				// TODO: Check if there's KAS winch in attached, but undocked state 
			}
			return false;
		}
			
		internal static void printDebug(String message) {

			if (!debug)
				return;
			StackTrace trace = new StackTrace ();
			String caller = trace.GetFrame(1).GetMethod ().Name;
			int line = trace.GetFrame (1).GetFileLineNumber ();
			print ("WST: " + caller + ":" + line + ": " + message);
		}

		private void configure() {
		
			// FIXME: How do I use KSPField here for configuration? 

			var config = GameDatabase.Instance.GetConfigs ("WorldStabilizer").FirstOrDefault ().config;

			string nodeValue = config.GetValue ("stabilizationTicks");
			if (nodeValue != null)
				stabilizationTicks = Int32.Parse (nodeValue);

			nodeValue = config.GetValue ("groundingTicks");
			if (nodeValue != null)
				groundingTicks = Int32.Parse (nodeValue);

			nodeValue = config.GetValue ("minDownMovement");
			if (nodeValue != null)
				minDownMovement = float.Parse (nodeValue);

			nodeValue = config.GetValue ("maxUpMovement");
			if (nodeValue != null)
				maxUpMovement = float.Parse (nodeValue);

			nodeValue = config.GetValue ("upMovementStep");
			if (nodeValue != null)
				upMovementStep = float.Parse (nodeValue);

			nodeValue = config.GetValue ("stabilizeInPrelaunch");
			if (nodeValue != null)
				stabilizeInPrelaunch = Boolean.Parse (nodeValue);

			nodeValue = config.GetValue ("stabilizeKerbals");
			if (nodeValue != null)
				stabilizeKerbals = Boolean.Parse (nodeValue);

			nodeValue = config.GetValue ("recalculateBounds");
			if (nodeValue != null)
				recalculateBounds = Boolean.Parse (nodeValue);

			nodeValue = config.GetValue ("debug");
			if (nodeValue != null)
				debug = Boolean.Parse (nodeValue);
			
			nodeValue = config.GetValue ("displayMessage");
			if (nodeValue != null)
				displayMessage = Boolean.Parse (nodeValue);

			nodeValue = config.GetValue ("drawPoints");
			if (nodeValue != null)
				drawPoints = Boolean.Parse (nodeValue);

			nodeValue = config.GetValue ("harpoonReiforcedForce");
			if (nodeValue != null)
				harpoonReiforcedForce = Int32.Parse (nodeValue);

			nodeValue = config.GetValue ("harpoonReattachTimeout");
			if (nodeValue != null)
				harpoonReattachTimeout = float.Parse (nodeValue);

			nodeValue = config.GetValue ("checkLandedPeriod");
			if (nodeValue != null)
				checkLandedPeriod = float.Parse (nodeValue);

			nodeValue = config.GetValue ("checkLandedTimeout");
			if (nodeValue != null)
				checkLandedTimeout = float.Parse (nodeValue);

			nodeValue = config.GetValue ("excludeVessels");
			if (nodeValue != null) {
				foreach(string exc in nodeValue.Split (','))
					excludeVessels.Add(exc.Trim());
			}
		}
	}
}
