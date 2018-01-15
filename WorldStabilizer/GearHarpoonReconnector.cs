using System;
using UnityEngine;
using System.Reflection;
using ModuleWheels;

namespace WorldStabilizer
{
	public class GearHarpoonReconnector: GenericReconnector
	{
		bool reattached = false;
		private ModuleWheelBase wheelBase = null;
		
		public GearHarpoonReconnector ()
		{
		}

		public override void OnAwake ()
		{
			base.OnAwake ();
			WorldStabilizer.printDebug ("GearHarpoonReconnector: awaking");

			// Still no idea how to reliably receive OnCollisionEnter event on wheels
			// so polling state each 0.5s and giving up in 10s
			// TODO: Make configurable
			wheelBase = (ModuleWheelBase)part.Modules ["ModuleWheelBase"];
			InvokeRepeating ("checkLanded", WorldStabilizer.checkLandedPeriod, WorldStabilizer.checkLandedPeriod);
			Invoke ("selfDestruct", WorldStabilizer.checkLandedTimeout);
		}

		private void selfDestruct() {
			selfDestructTimer = 1;
		}

		protected void checkLanded() {
			if (wheelBase.isGrounded) {
				WorldStabilizer.printDebug ("GearHarpoonReconnector: detected landed state on part " + part.name);
				reattach ();
				selfDestructTimer = 3;
			}
		}

		protected override void reattach() {

			if (reattached)
				return;
			WorldStabilizer.printDebug ("GearHarpoonReconnector: re-attaching to the ground; part = " + part.name);
			WorldStabilizer.instance.tryAttachHarpoon (vessel);
			reattached = true;
		}
	}

}

