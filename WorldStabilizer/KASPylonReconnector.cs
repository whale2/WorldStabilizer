using System;
using UnityEngine;
using System.Reflection;

namespace WorldStabilizer
{
	public class KASPylonReconnector : PartModule
	{
		private MethodInfo attachMethod;
		private PartModule moduleKISItem;

		private bool selfDestruct = false;

		public KASPylonReconnector ()
		{
		}

		public override void OnAwake ()
		{
			Debug.Log ("WST: KAS: OnAwake");
			base.OnAwake ();
			Type KISAddOnType = WorldStabilizer.findKISModule();
			if (KISAddOnType != null) {
				Debug.Log ("WST: KAS: KIS AddOn found");
				attachMethod = KISAddOnType.GetMethod ("GroundAttach");
				foreach (PartModule pm in part.Modules) {
					if (pm.name == WorldStabilizer.KISAddOnName) {
						moduleKISItem = pm;
						Debug.Log ("WST: KAS: KIS Module found for part " + part.name);
						break;
					}
				}
			}
		}

		private string nameof(UnityEngine.Object obj) {
			return obj == null ? "n/a" : obj.name;
		}

		public void FixedUpdate() {
			if (selfDestruct) {
				Debug.Log ("WST: KAS: FixedUpdate: ground contact = " + part.GroundContact);
				Debug.Log ("WST: KAS: FixedUpdate: check landed = " + part.checkLanded());
				Debug.Log ("WST: KAS: FixedUpdate: self-destruct!");
				part.RemoveModule (this);
			}
		}

		public void OnCollisionEnter(Collision c) {
			Debug.Log ("WST: KAS: rigidbody: " + nameof(c.rigidbody) + "; collider: " + nameof(c.collider) +
				"; gameObject: " + nameof(c.gameObject) + "; transform: " + nameof(c.transform) +
				"; collider rigidbody: " + nameof(c.collider.attachedRigidbody) + "; collider gameobject: " +
				nameof(c.collider.gameObject));
			Debug.Log ("WST: KAS: ground contact = " + part.GroundContact);
			Debug.Log ("WST: KAS: check landed = " + part.checkLanded());
			Debug.Log ("WST: KAS: is static = " + c.gameObject.isStatic);
			Debug.Log ("WST: KAS: layer = " + c.gameObject.layer);

			foreach (Component cmp in c.gameObject.GetComponents<Component>())
				Debug.Log ("WST: KAS: component: " + cmp.name + "; type = " + cmp.GetType ());
			
			selfDestruct = true;
		}
	}
}

