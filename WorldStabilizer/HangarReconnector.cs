using System;
using UnityEngine;
using System.Reflection;

namespace WorldStabilizer
{
	public class HangarReconnector : GenericReconnector
	{
		private PartModule moduleHangar = null;
		private bool collisionDetected = false;

		public HangarReconnector ()
		{
		}

		public override void OnAwake ()
		{
			base.OnAwake ();
			if (moduleHangar != null)
				return;
			if (!part.Modules.Contains ("GroundAnchor"))
				return;
			moduleHangar = part.Modules ["GroundAnchor"];
			WorldStabilizer.printDebug ("Hangar Module found for part " + part.name + " (" + moduleHangar + ")");
		}

		protected override void reattach() {

			if (moduleHangar != null) {
				if (!collisionDetected) {
					WorldStabilizer.invokeAction (moduleHangar, "Attach anchor");
					collisionDetected = true;
				}
			} else {
				WorldStabilizer.printDebug ("Hangar module is null");
			}
		}
	}
}

