using System;
using UnityEngine;
using System.Reflection;

namespace WorldStabilizer
{
	public class HarpoonReconnector: GenericReconnector
	{
		bool reattached = false;
		
		public HarpoonReconnector ()
		{
		}

		public override void OnAwake ()
		{
			base.OnAwake ();
			WorldStabilizer.printDebug ("HarpoonReconnector: awaking");
			Invoke ("finalCheck", WorldStabilizer.checkLandedTimeout);
		}

		protected override void reattach() {

			if (reattached)
				return;
			WorldStabilizer.printDebug ("HarpoonReconnector: re-attaching to the ground; part = " + part.name);
			KASAPI.tryAttachHarpoonImmediately (vessel);
			reattached = true;
		}
	}

}

