using System;
using HarmonyLib;
using UnityEngine;

namespace HeartopiaMod
{
	// Token: 0x02000003 RID: 3
	[HarmonyPatch(typeof(CharacterController), "Move")]
	public static class CharacterControllerPatch
	{
		// Token: 0x060000AC RID: 172 RVA: 0x0001C0E0 File Offset: 0x0001A2E0
		public static bool MovePrefix(CharacterController __instance, ref Vector3 motion)
		{
			if (!HeartopiaComplete.OverridePlayerPosition && !HeartopiaComplete.ShouldBlockGameplayInput())
			{
				return true;
			}

			if (__instance == null)
			{
				return true;
			}

			GameObject localPlayer = HeartopiaComplete.GetLocalPlayer();
			if (__instance.gameObject != localPlayer)
			{
				return true;
			}

			// Only apply motion override to the local player's CharacterController
			if (HeartopiaComplete.OverridePlayerPosition)
			{
				Vector3 position = __instance.transform.position;
				Vector3 vector = HeartopiaComplete.OverridePosition - position;
				motion = vector;
			}
			else if (HeartopiaComplete.ShouldBlockGameplayInput())
			{
				motion = Vector3.zero;
			}
			return true;
		}
	}
}
