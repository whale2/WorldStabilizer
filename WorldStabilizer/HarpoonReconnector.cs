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
		}

		protected override void reattach() {

			if (reattached)
				return;
			WorldStabilizer.printDebug ("Harpoon: re-attaching to the ground; part = " + part.name);
			WorldStabilizer.instance.tryAttachHarpoonImmediately (vessel);
			reattached = true;
		}
	}

}

