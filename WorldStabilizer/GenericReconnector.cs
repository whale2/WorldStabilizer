using System;
using UnityEngine;

namespace WorldStabilizer
{
	public abstract class GenericReconnector : PartModule
	{
		protected int selfDestructTimer = -1;

		public GenericReconnector ()
		{
		}

		public void FixedUpdate() {
			if (selfDestructTimer > 0)
				selfDestructTimer--;
			if (selfDestructTimer == 0) {
				WorldStabilizer.printDebug ("Removing reconnector module");
				CancelInvoke ();
				part.RemoveModule (this);
			}
		}

		public void OnCollisionEnter(Collision c) {

			// It's very unlikely that we hit something else and not the ground
			// Just check for the colliding GameObject layer, should be 15

			if (c.gameObject.layer == 15) {
				reattach ();
			}
			selfDestructTimer = 3;
		}

		protected void finalCheck() {

			WorldStabilizer.printDebug ("KASReconnector: GroundContact = " + part.GroundContact);
			if (part.GroundContact) {
				reattach ();
			}
		}

		protected abstract void reattach();
	}
}

