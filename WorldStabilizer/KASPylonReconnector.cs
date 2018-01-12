using System;
using UnityEngine;
using KIS;

namespace WorldStabilizer
{
	public class KASPylonReconnector : GenericReconnector
	{
		private ModuleKISItem moduleKISItem = null;

		public KASPylonReconnector ()
		{
		}

		public override void OnAwake ()
		{
			base.OnAwake ();
			if (moduleKISItem != null)
				return;
			Type KISAddOnType = WorldStabilizer.findKISModule();
			if (KISAddOnType != null) {
				if (!part.Modules.Contains ("ModuleKISItem"))
					return;
				moduleKISItem = (ModuleKISItem)part.Modules ["ModuleKISItem"];
				WorldStabilizer.printDebug ("KAS: KIS Module found for part " + part.name + " (" + moduleKISItem + ")");
			}
		}

		protected override void reattach() {
			
			if (moduleKISItem != null) {
				WorldStabilizer.printDebug ("KAS: re-attaching to the ground");
				moduleKISItem.GroundAttach ();
			} else {
				WorldStabilizer.printDebug ("KAS: module is null");
			}
		}
	}
}

