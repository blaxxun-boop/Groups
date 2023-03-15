using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Groups;

public static class Interface
{
	private static Sprite groupLeaderIcon = null!;
	private static GameObject? groupMemberFirst;
	public static GameObject? groupDialog;

	public static void Init()
	{
		groupLeaderIcon = Helper.loadSprite("groupLeaderIcon.png", 32, 32);
	}

	public static void AnchorGroupInterface(object sender, EventArgs e)
	{
		if (groupMemberFirst is not null)
		{
			Transform groupRoot = groupMemberFirst.transform.parent;
			if (((Vector2)groupRoot.localPosition - Groups.groupInterfaceAnchor.Value).magnitude > 0.001)
			{
				groupRoot.localPosition = Groups.groupInterfaceAnchor.Value;
				groupRoot.GetComponent<DragNDrop>().SetPosition(groupRoot.position);
			}
		}
	}

	public static void UpdateGroupInterfaceSpacing(object sender, EventArgs e)
	{
		if (groupMemberFirst is not null)
		{
			Transform groupRoot = groupMemberFirst.transform.parent;
			for (int i = 0; i < groupRoot.childCount; ++i)
			{
				groupRoot.GetChild(i).localPosition = Groups.horizontalGroupInterface.Value == Groups.Toggle.On ? new Vector3(i * Groups.spaceBetweenGroupMembers.Value, 0) : new Vector3(0, -i * Groups.spaceBetweenGroupMembers.Value);
			}
		}
	}

	[HarmonyPatch(typeof(Hud), nameof(Hud.Awake))]
	public class AddGroupDisplay
	{
		private static void Postfix(Hud __instance)
		{
			GameObject groups = new("Groups", typeof(RectTransform));
			groups.AddComponent<DragNDrop>();
			groups.transform.SetParent(__instance.m_rootObject.transform);
			groups.transform.localPosition = Groups.groupInterfaceAnchor.Value;
			groupMemberFirst = UnityEngine.Object.Instantiate(EnemyHud.instance.m_baseHudPlayer, groups.transform);
			groupMemberFirst.transform.localPosition = Vector3.zero;
			Transform healthBar = groupMemberFirst.transform.Find("Health");
			healthBar.localScale = new Vector3(1, 3.5f, 1);
			Transform darken = healthBar.transform.Find("darken");
			darken.GetComponent<RectTransform>().sizeDelta = new Vector2(8, 3);

			GameObject leaderImage = new("Leader Icon");
			leaderImage.transform.SetParent(groupMemberFirst.transform);
			Image leaderIcon = leaderImage.AddComponent<Image>();
			leaderIcon.sprite = groupLeaderIcon;
			RectTransform leaderRect = leaderImage.GetComponent<RectTransform>();
			leaderRect.sizeDelta = new Vector2(32, 32);
			leaderRect.localPosition = new Vector2(0, 46);
			leaderImage.SetActive(false);

			GameObject lifeDisplay = new("Life Display Text", typeof(RectTransform));
			lifeDisplay.transform.SetParent(groupMemberFirst.transform, false);
			((RectTransform)lifeDisplay.transform).sizeDelta = new Vector2(300, 50);
			lifeDisplay.transform.localPosition = new Vector3(0, 5);
			Text lifeDisplayText = lifeDisplay.AddComponent<Text>();
			lifeDisplayText.font = Resources.FindObjectsOfTypeAll<Font>().FirstOrDefault(x => x.name == "AveriaSerifLibre-Bold");
			lifeDisplayText.fontSize = 14;
			lifeDisplayText.alignment = TextAnchor.MiddleCenter;
			Outline outline = lifeDisplay.AddComponent<Outline>();
			outline.effectColor = Color.black;
			outline.effectDistance = new Vector2(1, -1);
		}
	}

	[HarmonyPatch(typeof(Hud), nameof(Hud.Update))]
	public class UpdateGroupDisplay
	{
		private static void Postfix()
		{
			Transform groupRoot = groupMemberFirst!.transform.parent;
			bool wasActive = groupRoot.gameObject.activeSelf;
			groupRoot.gameObject.SetActive(Groups.ownGroup is not null);

			if (Groups.ownGroup is null)
			{
				return;
			}

			while (groupRoot != null && groupRoot.childCount < Groups.ownGroup.playerStates.Count)
			{
				GameObject newMember = UnityEngine.Object.Instantiate(groupMemberFirst, groupRoot, false);
				if (newMember != null)
				{
					newMember.transform.localPosition = Groups.horizontalGroupInterface.Value == Groups.Toggle.On
						? new Vector3((groupRoot.childCount - 1) * Groups.spaceBetweenGroupMembers.Value, 0)
						: new Vector3(0, -(groupRoot.childCount - 1) * Groups.spaceBetweenGroupMembers.Value);
					
					Transform healthSlowTransform = newMember.transform.Find("Health/health_slow");
					Transform healthFastTransform = newMember.transform.Find("Health/health_fast");

					GuiBar originalBar = groupMemberFirst.transform.Find("Health/health_slow").GetComponent<GuiBar>();
					Vector2 originalSize = new(Mathf.Max(originalBar.m_bar.sizeDelta.x, originalBar.m_width),
						originalBar.m_bar.sizeDelta.y);
					
					if (healthSlowTransform != null)
					{
						GuiBar healthSlowGuiBar = healthSlowTransform.GetComponent<GuiBar>();
						if (healthSlowGuiBar != null)
						{
							healthSlowGuiBar.m_firstSet = true;
							healthSlowGuiBar.m_bar.sizeDelta = originalSize;
						}
					}
					else
					{
						Groups.GroupsLogger.LogWarning("healthSlowTransform is null");
					}
					
					if (healthFastTransform != null)
					{
						GuiBar healthFastGuiBar = healthFastTransform.GetComponent<GuiBar>();
						if (healthFastGuiBar != null)
						{
							healthFastGuiBar.m_firstSet = true;
							healthFastGuiBar.m_bar.sizeDelta = originalSize;
						}
					}
					else
					{
						Groups.GroupsLogger.LogWarning("healthFastTransform is null");
					}


					wasActive = false;
				}
			}

			if (groupRoot == null) return;
			if (!wasActive)
			{
				groupRoot.GetComponent<DragNDrop>().SetPosition(groupRoot.transform.position);
			}

			UpdateGroupMembers(groupRoot);
		}
		
		private static void UpdateGroupMembers(Transform groupRoot)
		{
			for (int i = 0; i < groupRoot.childCount; ++i)
			{
				Transform member = groupRoot.GetChild(i);
				if (member == null) continue;

				List<KeyValuePair<PlayerReference, Group.PlayerState>> members = Groups.ownGroup.playerStates.ToList();
				bool active = i < Groups.ownGroup.playerStates.Count;
				member.gameObject.SetActive(active);

				if (active)
				{
					PlayerReference player = members[i].Key;
					Group.PlayerState playerState = members[i].Value;

					UpdateHealthBars(member, playerState);
					UpdateLifeDisplayText(member, playerState);
					UpdateMemberName(member, player);
					UpdateLeaderIcon(member, player);

					API.InvokeUIUpdate(player, member.gameObject);
				}
			}
		}

		private static void UpdateHealthBars(Transform member, Group.PlayerState playerState)
		{
			float healthValue = playerState.health / playerState.maxHealth;

			member.Find("Health/health_slow")?.GetComponent<GuiBar>()?.SetValue(healthValue);
			member.Find("Health/health_fast")?.GetComponent<GuiBar>()?.SetValue(healthValue);
		}

		private static void UpdateLifeDisplayText(Transform member, Group.PlayerState playerState)
		{
			Text lifeDisplayText = member.Find("Life Display Text")?.GetComponent<Text>();
			if (lifeDisplayText != null)
			{
				lifeDisplayText.text = playerState.health <= 0 ? "DEAD" : $"{Mathf.Ceil(playerState.health)} / {Mathf.Ceil(playerState.maxHealth)}";
			}
		}

		private static void UpdateMemberName(Transform member, PlayerReference player)
		{
			TMP_Text memberText = member.Find("Name")?.GetComponent<TMP_Text>();
			if (memberText != null)
			{
				memberText.text = player.name;
				memberText.color = player == Groups.ownGroup.leader && Groups.groupLeaderDisplay.Value == Groups.GroupLeaderDisplayOption.Color
					? Groups.groupLeaderColor.Value
					: Groups.friendlyNameColor.Value;
			}
		}

		private static void UpdateLeaderIcon(Transform member, PlayerReference player)
		{
			Transform leaderIconTransform = member.Find("Leader Icon");
			if (leaderIconTransform != null)
			{
				leaderIconTransform.gameObject.SetActive(player == Groups.ownGroup.leader && Groups.groupLeaderDisplay.Value == Groups.GroupLeaderDisplayOption.Icon);
			}
		}
	}

	[HarmonyPatch]
	public class BroadcastHealth
	{
		private static IEnumerable<MethodBase> TargetMethods()
		{
			yield return AccessTools.Method(typeof(Character), nameof(Character.SetHealth));
			yield return AccessTools.Method(typeof(Character), nameof(Character.SetMaxHealth));
		}

		private static void Postfix(Character __instance)
		{
			if (Groups.ownGroup is not null && __instance == Player.m_localPlayer)
			{
				foreach (PlayerReference player in Groups.ownGroup.playerStates.Keys)
				{
					ZRoutedRpc.instance.InvokeRoutedRPC(player.peerId, "Groups UpdateHealth", __instance.GetHealth(), __instance.GetMaxHealth());
				}
			}
		}
	}

	public static void onUpdateHealth(long senderId, float health, float maxHealth)
	{
		if (Groups.ownGroup is null || !Groups.ownGroup.playerStates.TryGetValue(PlayerReference.fromPlayerId(senderId), out Group.PlayerState playerState))
		{
			return;
		}

		playerState.health = health;
		playerState.maxHealth = maxHealth;
	}

	[HarmonyPatch(typeof(Menu), nameof(Menu.Start))]
	private class AddGroupDialog
	{
		private static void Postfix()
		{
			groupDialog = UnityEngine.Object.Instantiate(Menu.instance.m_quitDialog.gameObject, Hud.instance.m_rootObject.transform.parent.parent, true);
			Button.ButtonClickedEvent noClicked = new();
			noClicked.AddListener(RPC.onDeclineInvitation);
			groupDialog.transform.Find("dialog/Button_no").GetComponent<Button>().onClick = noClicked;
			Button.ButtonClickedEvent yesClicked = new();
			yesClicked.AddListener(RPC.onAcceptInvitation);
			groupDialog.transform.Find("dialog/Button_yes").GetComponent<Button>().onClick = yesClicked;
		}
	}

	[HarmonyPatch(typeof(Menu), nameof(Menu.IsVisible))]
	private class DisablePlayerInputInGroupDialog
	{
		private static void Postfix(ref bool __result)
		{
			if (groupDialog && groupDialog?.activeSelf == true)
			{
				__result = true;
			}
		}
	}
}
