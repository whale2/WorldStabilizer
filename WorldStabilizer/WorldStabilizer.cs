using System;
using UnityEngine;
using System.Diagnostics;
using System.Collections.Generic;

namespace WorldStabilizer
{
	[KSPAddon(KSPAddon.Startup.Flight, false)]
	public class WorldStabilizer: MonoBehaviour
	{
		private int count = 0;
		private Dictionary<Guid, int> vessel_timer;
		private Dictionary<Guid, LineRenderer> renderer;
		private Dictionary<Guid, VesselBounds> bounds;
		private Dictionary<Guid, List<PartModule>> anchors;

		private float stabilizationFactor = 3.0f;
		private int stabilizationTimer;

		public WorldStabilizer ()
		{
		}

		public void Awake() {
			printDebug("Awake");
		}

		public void Start() {
			printDebug("Start");
			vessel_timer = new Dictionary<Guid, int> ();
			//renderer = new Dictionary<Guid, LineRenderer> ();
			bounds = new Dictionary<Guid, VesselBounds> ();
			anchors = new Dictionary<Guid, List<PartModule>> ();
			GameEvents.onVesselGoOffRails.Add (onVesselGoOffRails);
			GameEvents.onVesselCreate.Add(onVesselCreate);
			GameEvents.onNewVesselCreated.Add (onNewVesselCreated);
			stabilizationTimer = (int)(stabilizationFactor / Time.fixedDeltaTime);  // If delta = 0.01, we will keep vessels 300 frames
		}

		public void OnDestroy() {
			printDebug("OnDestroy");
			GameEvents.onVesselGoOffRails.Remove (onVesselGoOffRails);
			GameEvents.onVesselCreate.Remove(onVesselCreate);
			GameEvents.onNewVesselCreated.Remove (onNewVesselCreated);
		}

		public void onVesselCreate(Vessel v) {
			printDebug ("vessel = " + v.name);
		}

		public void onNewVesselCreated(Vessel v) {
			printDebug ("vessel = " + v.name);
		}
			
		public void onVesselGoOffRails(Vessel v) {

			if (v.situation != Vessel.Situations.LANDED)
				return;

			printDebug("off rails: " + v.name + "; fixedDelta = " + Time.fixedDeltaTime + "; delta = " + Time.deltaTime);
			vessel_timer[v.id] = stabilizationTimer;
			printDebug("Timer = " + vessel_timer[v.id]);
			bounds [v.id] = new VesselBounds (v);
			//renderer[v.id] = initLR (v, bounds[v.id]);
			count ++;
			tryDetachAnchor (v); // If this vessel has anchors (from Hangar), detach them
			moveUp (v);
		}

		public void FixedUpdate() {
			if (count == 0)
				return;

			foreach (Vessel v in FlightGlobals.VesselsLoaded) {
				if (vessel_timer.ContainsKey (v.id)) {
					stabilize (v);
				}
				antiSlide (v);
			}
		}

		private void moveUp(Vessel v) {
			float alt = GetRaycastAltitude (v, bounds[v.id]);
			printDebug ("Moving up: " + v.name + "; alt = " + alt.ToString());
			Vector3 up = (v.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
			v.Translate (up);

			printDebug ("Moved: " + v.name + "; alt = " + GetRaycastAltitude(v, bounds[v.id]).ToString());
		}

		private void moveDown(Vessel v) {

			bounds [v.id].findLowestPoint ();
			float alt = GetRaycastAltitude (v, bounds[v.id]);
			printDebug ("Moving down: " + v.name + "; alt = " + alt.ToString() + "; timer = " + vessel_timer[v.id] + "; radar alt = " + v.radarAltitude);
			Vector3 up = (v.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
			v.Translate (alt * -up * 0.97f);
		}

		private void stabilize(Vessel v) {

			if (vessel_timer [v.id] > stabilizationTimer - stabilizationFactor) // first 3 ticks
			{
				moveDown (v);
			}
			
			v.IgnoreGForces(20);
			v.ResetGroundContact();
			v.ResetCollisionIgnores();
			v.SetWorldVelocity (Vector3.zero);
			v.angularMomentum = Vector3.zero;
			v.angularVelocity = Vector3.zero;

			printDebug ("Stabilizing; v = " + v.name + "; radar alt = " + v.radarAltitude);
			vessel_timer [v.id]--;
			if (vessel_timer [v.id] == 0) {
				count--;
				printDebug ("Stopping stabilizing " + v.name);
				tryAttachAnchor (v);
				if (count == 0)
					ScreenMessages.PostScreenMessage ("World has been stabilized");
			}

		}

		private List<PartModule> findAnchoredParts(Vessel v) {

			printDebug ("Looking for anchors in " + v.name);
			List<PartModule> anchorList = new List<PartModule> ();

			foreach (Part p in v.parts) {
				foreach (PartModule pm in p.Modules) {
					if (pm.moduleName == "GroundAnchor") {
						printDebug (v.name + ": Found anchor on part " + p.name + "; attached = " + pm.Fields.GetValue("isAttached"));
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
				invokeAction (pm, "Attach anchor");
			}
		}

		private void invokeAction(PartModule pm, string actionName) {
			printDebug ("Invoking action " + actionName + " on part " + pm.part.name);
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

		private LineRenderer initLR(Vessel v, VesselBounds vbounds) {

			LineRenderer Lr;
			Lr = new GameObject().AddComponent<LineRenderer>();
			Lr.material = new Material(Shader.Find("KSP/Emissive/Diffuse"));
			Lr.material.SetColor("_EmissiveColor", Color.green);
			Lr.SetWidth(0.15f, 0.15f);
			Lr.SetVertexCount (2);
			Lr.SetPosition (0, v.CoM + vbounds.bottomPoint);
			Lr.SetPosition(1, v.CoM + vbounds.bottomPoint + 
				Vector3.ProjectOnPlane(v.CoM-FlightCamera.fetch.mainCamera.transform.position, vbounds.up).normalized);
			Lr.enabled = true;
			return Lr;
		}

		public struct VesselBounds
		{

			public Vessel vessel;
			public float bottomLength;

			public Vector3 localBottomPoint;
			public Vector3 bottomPoint
			{
				get
				{
					return vessel.transform.TransformPoint(localBottomPoint);
				}
			}     
			public Vector3 up;

			public VesselBounds(Vessel v)
			{
				vessel = v;
				bottomLength = 0;
				localBottomPoint = Vector3.zero;
				up = Vector3.zero;
				findLowestPoint();
				
			}


			public void findLowestPoint() {

				Vector3 lowestPoint = Vector3.zero;
				//float maxSqrDist = 0.0f;
				Part furthestPart = new Part();
				up = (vessel.CoM-vessel.mainBody.transform.position).normalized;
				Vector3 downPoint = vessel.CoM - (2000 * up);
				Vector3 closestVert = vessel.CoM;
				float closestSqrDist = Mathf.Infinity;

				foreach (Part p in vessel.parts) {
					/*float sqrDist = Vector3.ProjectOnPlane((p.transform.position-vessel.CoM), up).sqrMagnitude;
					if(sqrDist > maxSqrDist)
					{
						maxSqrDist = sqrDist;
						furthestPart = p;
					}*/
					foreach (MeshFilter mf in p.GetComponentsInChildren<MeshFilter>()) {
						Mesh mesh = mf.mesh;
						foreach (Vector3 vert in mesh.vertices) {
							//bottom check
							Vector3 worldVertPoint = mf.transform.TransformPoint (vert);
							float bSqrDist = (downPoint - worldVertPoint).sqrMagnitude;
							if (bSqrDist < closestSqrDist) {
								closestSqrDist = bSqrDist;
								closestVert = worldVertPoint;
								furthestPart = p;
							}
						}
					}

				}
				printDebug ("vessel = " + vessel.name + "; furthest part = " + furthestPart.name);
				bottomLength = Vector3.Project(closestVert-vessel.CoM, up).magnitude;
				localBottomPoint = vessel.transform.InverseTransformPoint(closestVert);
			}

		}

		void antiSlide(Vessel v) {

			if (v.srfSpeed < 0.1) {
				v.SetWorldVelocity (Vector3.zero);
				v.angularMomentum = Vector3.zero;
				v.angularVelocity = Vector3.zero;
			}
		}

		private float GetRaycastAltitude(Vessel v, VesselBounds vbounds) 
		{
			RaycastHit hit;
			Vector3 up = (v.transform.position - FlightGlobals.currentMainBody.transform.position).normalized;
			if(Physics.Raycast(vbounds.bottomPoint, -up, out hit, (float)v.altitude, (1<<15)|(1<<0)))
			{
				return Vector3.Project(hit.point - vbounds.bottomPoint, up).magnitude;
			}
			else
			{
				//return GetRadarAltitude(vesselBounds.vessel);
				return 0.0f;
			}
		}
			
		internal static void printDebug(String message) {

			StackTrace trace = new StackTrace ();
			String caller = trace.GetFrame(1).GetMethod ().Name;
			int line = trace.GetFrame (1).GetFileLineNumber ();
			print ("WST: " + caller + ":" + line + ": " + message);
		}

	}
}

