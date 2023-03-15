using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using UnityEngine;
using UnityEngine.UI;

namespace Groups;

public static class Map
{
	private static Sprite groupMapPlayerIcon = null!;
	private static Sprite groupMapPingIcon = null!;
	private static readonly ConditionalWeakTable<Chat.WorldTextInstance, object> groupPingTexts = new();
	private static readonly Color defaultColor = new(1f, 0.7176471f, 0.3602941f);

	public static void Init()
	{
		groupMapPlayerIcon = Helper.loadSprite("groupPlayerIcon.png", 64, 64);
		groupMapPingIcon = Helper.loadSprite("groupMapPingIcon.png", 64, 64);

		UpdateMapPinColor();
	}

	[HarmonyPatch(typeof(EnemyHud), nameof(EnemyHud.ShowHud))]
	public class ColorNames
	{
		private static void Postfix(Character c, Dictionary<Character, EnemyHud.HudData> ___m_huds)
		{
			if (c is not Player player)
			{
				return;
			}

			GameObject hudBase = ___m_huds[c].m_gui;

			Text playerName = hudBase.transform.Find("Name").GetComponent<Text>();

			playerName.color = Groups.ownGroup != null && Groups.ownGroup.playerStates.ContainsKey(PlayerReference.fromPlayer(player)) ? Groups.friendlyNameColor.Value : defaultColor;
		}
	}

	[HarmonyPatch(typeof(Chat), nameof(Chat.SendPing))]
	private static class RestrictPingsToGroupOnModifierHeld
	{
		private static void RestrictBroadcast(ZRoutedRpc instance, long targetPeerId, string methodName, params object[] parameters)
		{
			if (Groups.ownGroup is not null && targetPeerId == ZRoutedRpc.Everybody && Groups.groupPingHotkey.Value.IsPressed())
			{
				foreach (PlayerReference playerReference in Groups.ownGroup.playerStates.Keys)
				{
					instance.InvokeRoutedRPC(playerReference.peerId, "Groups MapPing", parameters);
				}
				return;
			}

			instance.InvokeRoutedRPC(targetPeerId, methodName, parameters);
		}

		private static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
		{
			MethodInfo routedRPC = AccessTools.DeclaredMethod(typeof(ZRoutedRpc), nameof(ZRoutedRpc.InvokeRoutedRPC), new[] { typeof(long), typeof(string), typeof(object[]) });
			foreach (CodeInstruction instruction in instructions)
			{
				if (instruction.opcode == OpCodes.Callvirt && instruction.OperandIs(routedRPC))
				{
					yield return new CodeInstruction(OpCodes.Call, AccessTools.DeclaredMethod(typeof(RestrictPingsToGroupOnModifierHeld), nameof(RestrictBroadcast)));
				}
				else
				{
					yield return instruction;
				}
			}
		}
	}

	[HarmonyPatch(typeof(Chat), nameof(Chat.RPC_ChatMessage))]
	private class ClearGroupPing
	{
		public static void Prefix(Chat __instance, long sender)
		{
			if (__instance.FindExistingWorldText(sender) is { } text)
			{
				groupPingTexts.Remove(text);
			}
		}
	}

	public static void onMapPing(long senderId, Vector3 position, int type, UserInfo name, string text)
	{
		Chat.instance.RPC_ChatMessage(senderId, position, type, name, text, PrivilegeManager.GetNetworkUserId());
		Chat.WorldTextInstance worldText = Chat.instance.FindExistingWorldText(senderId);
		worldText.m_textMeshField.color = Groups.friendlyNameColor.Value;
		groupPingTexts.Add(worldText, Array.Empty<object>());
	}

	public static void UpdateMapPinColor()
	{
		Color[]? pixels = Helper.loadTexture("groupPlayerIcon.png").GetPixels();
		for (int i = 0; i < pixels.Length; ++i)
		{
			if (pixels[i].r > 0.5 && pixels[i].b < 0.5 && pixels[i].g < 0.5)
			{
				pixels[i] = Groups.friendlyNameColor.Value;
			}
		}
		groupMapPlayerIcon.texture.SetPixels(pixels);
		groupMapPlayerIcon.texture.Apply();

		pixels = Helper.loadTexture("groupMapPingIcon.png").GetPixels();
		for (int i = 0; i < pixels.Length; ++i)
		{
			if (pixels[i].r > 0.5 && pixels[i].b < 0.5 && pixels[i].g < 0.5)
			{
				pixels[i].b = Groups.friendlyNameColor.Value.b;
				pixels[i].g = Groups.friendlyNameColor.Value.g;
				pixels[i].r = Groups.friendlyNameColor.Value.r;
			}
		}
		groupMapPingIcon.texture.SetPixels(pixels);
		groupMapPingIcon.texture.Apply();
	}

	[HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePlayerPins))]
	private class ChangeGroupMemberPin
	{
		private static void Postfix(Minimap __instance)
		{
			for (int index = 0; index < __instance.m_tempPlayerInfo.Count; ++index)
			{
				Minimap.PinData playerPin = __instance.m_playerPins[index];
				ZNet.PlayerInfo playerInfo = __instance.m_tempPlayerInfo[index];
				if (playerPin.m_name == playerInfo.m_name)
				{
					playerPin.m_icon = Groups.ownGroup?.playerStates.ContainsKey(PlayerReference.fromPlayerInfo(playerInfo)) == true ? groupMapPlayerIcon : __instance.GetSprite(Minimap.PinType.Player);
					if (playerPin.m_iconElement)
					{
						playerPin.m_iconElement.sprite = playerPin.m_icon;
					}
				}
			}
		}
	}

	[HarmonyPatch(typeof(Minimap), nameof(Minimap.UpdatePingPins))]
	private class ChangeGroupMemberPing
	{
		private static void Postfix(Minimap __instance)
		{
			for (int i = 0; i < __instance.m_tempShouts.Count; ++i)
			{
				Minimap.PinData pingPin = __instance.m_pingPins[i];
				Chat.WorldTextInstance tempShout = __instance.m_tempShouts[i];
				pingPin.m_icon = groupPingTexts.TryGetValue(tempShout, out _) ? groupMapPingIcon : __instance.GetSprite(Minimap.PinType.Ping);
				if (pingPin.m_iconElement)
				{
					pingPin.m_iconElement.sprite = pingPin.m_icon;
				}
			}
		}
	}
}
